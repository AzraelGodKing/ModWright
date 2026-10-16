using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <param name="Skipped">Nothing was sent because the user chose not to upload an unchanged package (counts as OK).</param>
public sealed record PublishResult(bool Ok, string Message, string? NewId = null, bool Skipped = false);

/// <summary>
/// Packages one mod, writes the SteamCMD build file and runs the upload. Updates send the content and the change note,
/// plus any Workshop page changes from <see cref="PageEdits"/> (otherwise title, description and preview on Steam are
/// left alone); creating makes a PRIVATE item unless a page change picks another visibility.
/// </summary>
public sealed class Publisher
{
    readonly string _repo;
    readonly ProjectConfig _cfg;
    readonly string _steamCmd;
    readonly string _account;
    readonly string _tempRoot;

    /// <summary>Every staging folder is named with this prefix inside the temp folder.</summary>
    public const string StagePrefix = "steamcmd_helper_";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    public event Action<string>? Log;
    public event Action<string, int>? Stage;

    /// <summary>Workshop page changes to send with a mod's upload (title, description, visibility), or null for none.</summary>
    public Func<ModInfo, WorkshopPageEdit?>? PageEdits { get; set; }

    /// <summary>A change note typed in the upload confirmation, used instead of the changelog entry; null keeps the entry.</summary>
    public Func<ModInfo, string?>? NoteOverride { get; set; }

    /// <summary>Ends each change note sent to Steam with <see cref="CreditLine"/> (Options > Change notes; on by default in the app).</summary>
    public bool Credit { get; init; }

    /// <summary>Wraps each change note sent to Steam (see <see cref="ChangeNotes.Apply"/>); null or empty sends the note as it is.</summary>
    public string? NoteTemplate { get; init; }

    /// <summary>Turns Markdown in change notes into Steam's BBCode when the layout's changelog is a .md file.</summary>
    public bool MarkdownToSteam { get; init; }

    /// <summary>The change note as Steam gets it: the template filled in, Markdown converted, without the credit line.</summary>
    public string Shape(ModInfo m, string notes)
    {
        var shaped = ChangeNotes.Apply(NoteTemplate, notes, m.Name, m.Version, _cfg.GameName);
        return MarkdownToSteam && ChangeNotes.IsMarkdown(_cfg.Changelog) ? ChangeNotes.MarkdownToBBCode(shaped) : shaped;
    }

    /// <summary>The line <see cref="Credit"/> adds, in Steam's BBCode, linking to the newest release.</summary>
    public const string CreditLine = "[i]Uploaded via [url=" + UpdateCheck.ReleasesPage + "/latest]SteamCMD Helper[/url][/i]";

    /// <summary>Raised once a mod is packaged, with what the package holds (also used by "Preview package").</summary>
    public event Action<ModInfo, PackageReport>? Packaged;

    /// <summary>
    /// When set, each upload's file list is kept in this folder (<see cref="PackageDiffs"/>) and the next package of the
    /// same item is compared with it.
    /// </summary>
    public string? ManifestDir { get; init; }

    /// <summary>
    /// Asked when a package is identical to the last upload and no page change goes with it: true uploads it anyway (Steam
    /// then gets only the change note), false skips the mod. Null uploads it with a warning.
    /// </summary>
    public Func<ModInfo, PackageDiff, bool>? UploadUnchanged { get; set; }

    /// <summary>When set, each successful upload is added to this <see cref="UploadHistory"/> file.</summary>
    public string? HistoryFile { get; init; }

    /// <summary>
    /// Reads an item's live details from Steam after an update, to confirm it arrived. Null skips the check (tests,
    /// offline use). Only public and unlisted items can be read this way.
    /// </summary>
    public Func<string, CancellationToken, Task<WorkshopItem?>>? SteamCheck { get; init; }

    /// <summary>How far Steam's clock and this PC's may disagree when matching Steam's "updated" time to the upload.</summary>
    public static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(2);

    /// <summary>The standard check: Steam's public details API, skipping the cache so the answer is fresh.</summary>
    public static async Task<WorkshopItem?> LiveSteamCheck(string id, CancellationToken ct)
    {
        WorkshopStatus.Forget(id);
        return (await WorkshopStatus.FetchAsync(new[] { id }, ct)).GetValueOrDefault(id);
    }

    public Publisher(string repo, ProjectConfig cfg, string steamCmd, string account, string? tempRoot = null)
    {
        _repo = repo; _cfg = cfg; _steamCmd = steamCmd; _account = account;
        _tempRoot = tempRoot ?? Path.GetTempPath();
    }

    void Say(string s) => Log?.Invoke(s);
    void At(string s, int pct) => Stage?.Invoke(s, pct);

    public async Task<PublishResult> RunAsync(ModInfo m, bool upload, CancellationToken ct)
    {
        At("Reading changelog...", 5);
        var typed = NoteOverride?.Invoke(m);
        var notes = typed ?? NotesFor(m, _cfg);
        if (!_cfg.AppIdValid) return new PublishResult(false, "The Steam app ID is not set. Enter the game's app ID in the window first.");
        if (_cfg.ChangelogByVersion && notes.Length == 0 && typed == null)
            return new PublishResult(false, $"{_cfg.Changelog} has no entry for version {(m.Version.Length > 0 ? m.Version : "(unknown)")}.");
        var shaped = Shape(m, notes);
        var sent = Credit ? shaped + (shaped.Length > 0 ? "\n\n" : "") + CreditLine : shaped;
        if (sent.Length > 7900)
            sent = shaped.Substring(0, 7900 - (Credit ? CreditLine.Length + 2 : 0)) + "\n[i]...see the full changelog on the project page.[/i]"
                + (Credit ? "\n" + CreditLine : "");

        SweepStale(_tempRoot, StaleAfter);
        var stageRoot = Path.Combine(_tempRoot, StagePrefix + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(stageRoot, ProjectConfig.Norm(m.RelPath));
        Directory.CreateDirectory(stageRoot);

        // The package is removed on every way out (success, dry run, a check failing, packaging throwing,
        // cancellation) except a failed upload, where it is kept so the build file can be inspected.
        var keep = false;
        try
        {
            At("Packaging...", 10);
            await PackageAsync(m, stageRoot, stage, ct);

            var report = PackageCheck.Inspect(stage, hash: ManifestDir != null);
            var last = ManifestDir != null && m.OnSteam ? PackageDiffs.Load(ManifestDir, m.WorkshopId!) : null;
            if (last != null) report = report with { Diff = PackageDiffs.Compare(last, report.Files) };
            Packaged?.Invoke(m, report);
            if (report.Files.Count == 0) return new PublishResult(false, "The package is empty, so the upload would remove every file from the item on Steam.");
            var missing = PackageCheck.MissingRequired(report.Files, _cfg.RequiredInPackage());
            if (missing.Count > 0)
                return new PublishResult(false, $"The package has no {string.Join(", ", missing)}, so the mod would break for everyone who updates. " +
                    (_cfg.UseGitArchive && ModScanner.IsGitRepo(_repo) ? "Check it is committed and not left out by an export-ignore rule in .gitattributes." : "Check it exists and is not in the layout's Exclude list."));
            foreach (var w in report.Warnings) Say("Warning: the package " + w + ".");
            if (report.Diff is { } diff)
            {
                foreach (var line in PackageDiffs.Describe(diff)) Say(line);
                foreach (var w in diff.Warnings()) Say("Warning: the package " + w + ".");
            }

            var page = PageEdits?.Invoke(m);
            if (page != null && WorkshopPage.Validate(page) is { } bad) return new PublishResult(false, "Workshop page: " + bad);
            string extra;
            var visibility = page?.Visibility ?? WorkshopPage.Private;
            if (!m.OnSteam)
            {
                var desc = page?.Description ?? ReadDescription(m);
                if (desc.Length < 20) return new PublishResult(false, $"No description found. Add {_cfg.SteamDescriptionFile}.");
                var previewLine = string.IsNullOrWhiteSpace(_cfg.Preview)
                    ? ""
                    : $"\n    \"previewfile\"     \"{Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.Preview))}\"";
                extra = WorkshopPage.VdfLines(page?.Title ?? m.Name, desc, null) + previewLine + WorkshopPage.VdfLines(null, null, visibility);
            }
            else
            {
                extra = page == null ? "" : WorkshopPage.VdfLines(page.Title, page.Description, page.Visibility);
            }

            var vdf = $$"""
                "workshopitem"
                {
                    "appid"           "{{_cfg.AppId}}"
                    "publishedfileid" "{{m.WorkshopId ?? "0"}}"
                    "contentfolder"   "{{stage}}"{{extra}}
                    "changenote"      "{{Esc(sent)}}"
                }
                """;
            var vdfPath = Path.Combine(stageRoot, m.Folder + ".vdf");
            await File.WriteAllTextAsync(vdfPath, vdf, new UTF8Encoding(false), ct);

            if (m.OnSteam && page != null) Say("Workshop page changes: " + page.Summary());
            Say($"{(m.OnSteam ? "Workshop id " + m.WorkshopId : $"New {WorkshopPage.VisibilityName(visibility).ToLowerInvariant()} item")} | version {m.Version} | {report.Files.Count} files, {report.MegaBytes} MB | notes {notes.Length} chars");
            At("Package ready", 20);

            if (!upload)
            {
                Say("Dry run: nothing uploaded. The package was built and checked, then removed.");
                return new PublishResult(true, "Dry run OK");
            }

            if (report.Diff is { Identical: true } same && page == null)
            {
                if (UploadUnchanged != null && !UploadUnchanged(m, same))
                {
                    Say($"Skipped {m.Name}: its package is the same as {same.LastUpload}.");
                    return new PublishResult(true, "Skipped: nothing changed " + same.Since, Skipped: true);
                }
                Say($"Warning: no file changed {same.Since}, so subscribers get only the change note.");
            }

            var started = DateTimeOffset.UtcNow;
            var output = new List<string>();
            var exit = await RunProcAsync(_steamCmd,
                new[] { "+login", _account, "+workshop_build_item", vdfPath, "+quit" },
                Path.GetDirectoryName(_steamCmd) ?? "",
                line =>
                {
                    Say(line);
                    lock (output) output.Add(line);
                    MapStage(line);
                }, ct);

            string? vdfAfter = null;
            try { vdfAfter = await File.ReadAllTextAsync(vdfPath, ct); } catch (IOException) { }
            UploadOutcome outcome;
            lock (output) outcome = SteamCmdOutput.Evaluate(output, exit, m.WorkshopId, vdfAfter);

            // SteamCMD's console output is not always complete. For an existing item, ask Steam whether the update
            // landed: a clean run that never said "Success" counts once Steam shows the item updated since it started.
            var steamSays = m.OnSteam && (outcome.Ok || outcome.Unconfirmed) && SteamCheck != null
                ? await ConfirmOnSteamAsync(m.WorkshopId!, started, ct)
                : null;
            if (!outcome.Ok && steamSays == true)
            {
                Say("SteamCMD did not print \"Success\", but Steam shows the item updated just now, so the upload went through.");
                outcome = outcome with { Ok = true, Reason = "Uploaded", Unconfirmed = false };
            }
            else if (outcome.Ok && steamSays == false)
                Say("Steam's page does not show this update yet. It usually appears within a minute; check the item if it does not.");

            // Save a new item's id as soon as Steam hands one out, even if the run then failed: creating it
            // again would make a duplicate Workshop item.
            var newId = m.OnSteam ? null : outcome.PublishedFileId;
            if (newId != null) await SaveIdAsync(m, newId, ct);

            if (!outcome.Ok)
            {
                keep = true;
                Say("Package and build file kept for inspection in " + stageRoot);
                var msg = outcome.Reason;
                if (outcome.Unconfirmed && m.OnSteam && SteamCheck != null)
                    msg += steamSays == false
                        ? " Steam's page still shows the previous update time."
                        : " Steam could not confirm it either (private items can't be read); check the item's change notes on Steam before uploading again.";
                if (!m.OnSteam)
                    msg += newId != null
                        ? $" Steam did create item {newId} and its id was saved to {_cfg.WorkshopIdFile}: do NOT create it again."
                        : $" Check your Steam profile before trying again (a new item may not be public); if it was created, put its number in {_cfg.WorkshopIdFile} and do NOT create it again.";
                return new PublishResult(false, msg, newId);
            }

            if (newId != null) Say($"Created {m.Name} {m.Version} as {WorkshopPage.VisibilityName(visibility).ToUpperInvariant()} item {newId}. Saved to {_cfg.WorkshopIdFile} (commit it).");
            else Say($"Uploaded {m.Name} {m.Version}");

            if ((m.WorkshopId ?? newId) is { } id)
            {
                if (HistoryFile != null) UploadHistory.Add(new UploadEntry(DateTimeOffset.UtcNow, id, m.Name, m.Version, notes, newId != null, _cfg.AppId,
                    report.TotalBytes, (DateTimeOffset.UtcNow - started).TotalSeconds), HistoryFile);
                WorkshopStatus.Forget(id);
                if (ManifestDir != null) PackageDiffs.Save(ManifestDir, id, new PackageManifest(DateTimeOffset.UtcNow, m.Version, report.Files.ToList()));
            }

            At("Uploaded", 100);
            return new PublishResult(true, outcome.Reason, newId);
        }
        finally
        {
            if (!keep) TryDelete(stageRoot);
        }
    }

    /// <summary>
    /// True when Steam shows the item updated at or after <paramref name="started"/>, false when it shows an older
    /// time, null when Steam could not say (private item, offline, Steam down).
    /// </summary>
    async Task<bool?> ConfirmOnSteamAsync(string id, DateTimeOffset started, CancellationToken ct)
    {
        At("Checking Steam...", 97);
        WorkshopItem? item;
        try { item = await SteamCheck!(id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
        if (item is not { Public: true, Updated: { } updated }) return null;
        var ok = updated >= started - ClockSlack;
        if (ok) Say($"Steam confirms the update (item updated {updated.ToLocalTime():HH:mm:ss}).");
        return ok;
    }

    /// <summary>The change note the changelog gives for the mod's current version (empty when there is none).</summary>
    public static string NotesFor(ModInfo m, ProjectConfig cfg)
    {
        var clPath = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.Changelog));
        try { return File.Exists(clPath) ? ModScanner.ReadChangelog(clPath, cfg, m.Version).notes : ""; }
        catch (IOException) { return ""; }
    }

    async Task SaveIdAsync(ModInfo m, string id, CancellationToken ct)
    {
        var idFile = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.WorkshopIdFile));
        Directory.CreateDirectory(Path.GetDirectoryName(idFile)!);
        var text = id;
        if (!string.IsNullOrWhiteSpace(_cfg.WorkshopIdKey))
            text = ModScanner.WriteKey(File.Exists(idFile) ? await File.ReadAllTextAsync(idFile, ct) : "", _cfg.WorkshopIdKey, id);
        await File.WriteAllTextAsync(idFile, text, new UTF8Encoding(false), ct);
    }

    /// <summary>Removes staging folders older than <paramref name="age"/> left behind by failed uploads or a crash.</summary>
    public static void SweepStale(string tempRoot, TimeSpan age)
    {
        try
        {
            var cutoff = DateTime.UtcNow - age;
            foreach (var d in Directory.GetDirectories(tempRoot, StagePrefix + "*"))
                if (Directory.GetLastWriteTimeUtc(d) < cutoff) TryDelete(d);
        }
        catch { }
    }

    // ------------------------------------------------------------ packaging

    async Task PackageAsync(ModInfo m, string stageRoot, string stage, CancellationToken ct)
    {
        if (_cfg.UseGitArchive && ModScanner.IsGitRepo(_repo))
        {
            var zip = Path.Combine(stageRoot, "package.zip");
            var code = await RunProcAsync("git", new[] { "archive", "--format=zip", "-o", zip, "HEAD", m.RelPath }, _repo, Say, ct);
            if (code != 0 || !File.Exists(zip)) throw new InvalidOperationException("git archive failed. Is this mod committed?");
            ZipFile.ExtractToDirectory(zip, stageRoot, true);
            File.Delete(zip);
            if (!Directory.Exists(stage)) throw new InvalidOperationException("git archive produced nothing for " + m.Folder + ". Commit the mod first.");
        }
        else
        {
            CopyDir(m.Dir, stage, _cfg.Exclude ?? Array.Empty<string>());
        }

        // Build outputs usually are not tracked by git, so take them from the working tree.
        foreach (var glob in _cfg.BuiltFiles ?? Array.Empty<string>())
        {
            foreach (var f in ModScanner.MatchGlob(m.Dir, glob))
            {
                var rel = Path.GetRelativePath(m.Dir, f);
                var dest = Path.Combine(stage, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }
    }

    static void CopyDir(string src, string dst, string[] exclude)
    {
        Directory.CreateDirectory(dst);
        bool Skip(string name) => exclude.Any(p => p.Contains('*')
            ? Regex.IsMatch(name, "^" + Regex.Escape(p).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase)
            : name.Equals(p, StringComparison.OrdinalIgnoreCase));
        foreach (var f in Directory.GetFiles(src))
            if (!Skip(Path.GetFileName(f))) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            if (!Skip(Path.GetFileName(d))) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)), exclude);
    }

    string ReadDescription(ModInfo m) => DescriptionFor(m, _cfg);

    /// <summary>The description a new item is created with: the Steam description file, else the layout's description field.</summary>
    public static string DescriptionFor(ModInfo m, ProjectConfig cfg)
    {
        var f = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.SteamDescriptionFile));
        if (File.Exists(f)) return File.ReadAllText(f).Trim();
        if (string.IsNullOrWhiteSpace(cfg.ModMarker)) return "";
        var marker = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.ModMarker));
        try { return ModScanner.ReadMeta(marker, cfg.DescriptionKey) ?? ""; }
        catch { return ""; }
    }

    // ------------------------------------------------------------ helpers

    void MapStage(string line)
    {
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;
        if (line.Contains("Logging in", ic)) At("Logging in to Steam...", 30);
        else if (line.Contains("Logged in OK", ic) || line.Contains("Waiting for user info...OK", ic)) At("Logged in", 35);
        else if (line.Contains("Uploading content", ic)) At("Uploading files...", 50);
        else if (line.Contains("Uploading preview", ic)) At("Uploading preview image...", 70);
        else if (line.Contains("Committing", ic)) At("Committing update...", 88);
        else if (line.Contains("Success", ic)) At("Steam accepted the update", 95);
    }

    /// <summary>Escapes text for a Valve .vdf quoted value.</summary>
    /// <summary>
    /// Makes text safe inside a quoted .vdf value. SteamCMD reads build files with escape sequences off, so \n, \" and
    /// \\ reach Steam as typed: line breaks stay real line breaks (a quoted value may span lines), backslashes are left
    /// alone, and straight double quotes, which would end the value, become curly ones.
    /// </summary>
    public static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        var open = true;
        foreach (var c in s.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '"') { sb.Append(open ? '\u201C' : '\u201D'); open = !open; }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static void TryDelete(string dir) { try { Directory.Delete(dir, true); } catch { } }

    static async Task<int> RunProcAsync(string file, IEnumerable<string> args, string workDir, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.StandardInput.Close(); // no keyboard: a login prompt fails fast instead of hanging

        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        await Task.Run(() => p.WaitForExit());
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }
}
