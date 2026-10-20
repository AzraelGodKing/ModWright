using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <param name="Skipped">Nothing was sent because the user chose not to upload an unchanged package (counts as OK).</param>
/// <param name="Version">The version that was sent, when it differs from the mod's (an upload from an older git ref).</param>
public sealed record PublishResult(bool Ok, string Message, string? NewId = null, bool Skipped = false, string? Version = null);

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

    /// <summary>Ends each change note sent to Steam with <see cref="CreditLine"/> (Options > Workshop page > Change notes; on by default in the app).</summary>
    public bool Credit { get; init; }

    /// <summary>Wraps each change note sent to Steam (see <see cref="ChangeNotes.Apply"/>); null or empty sends the note as it is.</summary>
    public string? NoteTemplate { get; init; }

    /// <summary>The mods folder's shared footer, added to the description a new item is created with.</summary>
    public string? DescriptionFooter { get; init; }

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
    /// <summary>When set, a description sent with an upload is remembered here (<see cref="DescriptionSync"/>).</summary>
    public string? SentDescriptions { get; init; }
    /// The git branch, tag or commit to package instead of HEAD (git packaging only); null packages HEAD. The version
    /// and change note are then read from that commit, not from the working folder.
    /// </summary>
    public string? GitRef { get; init; }

    /// <summary>
    /// Upload to each mod's private test item (<see cref="ModInfo.TestWorkshopId"/>, created when missing) instead of its
    /// real one. Page changes, the upload history and the other records are left alone.
    /// </summary>
    public bool TestItems { get; init; }

    /// <summary>When set, each successful upload is added to this <see cref="UploadHistory"/> file.</summary>
    public string? HistoryFile { get; init; }

    /// <summary>
    /// Reads an item's live details from Steam after an update, to confirm it arrived. Null skips the check (tests,
    /// offline use). Only public and unlisted items can be read this way.
    /// </summary>
    public Func<string, CancellationToken, Task<WorkshopItem?>>? SteamCheck { get; init; }

    /// <summary>
    /// Sets an item's Workshop tags after a successful upload (app id, item id, the tags the layout gives the mod), since
    /// SteamCMD cannot. Null leaves tags alone (tests, offline use); see <see cref="WorkshopTags.UpdateOnSteamAsync"/>.
    /// </summary>
    public Func<string, string, string[], CancellationToken, Task<TagResult>>? TagUpdater { get; init; }

    /// <summary>
    /// When set, after each upload page changes SteamCMD cannot make go through the Steam client (<see cref="WorkshopExtras"/>):
    /// the "Required items" from the Workshop links in the mod's metadata (<see cref="SetRequired"/>), and the gallery and
    /// translated descriptions (<see cref="SetGalleryAndLanguages"/>).
    /// </summary>
    public Func<ExtrasRequest, CancellationToken, Task<ExtrasResult>>? PageExtras { get; init; }

    public bool SetRequired { get; init; } = true;
    public bool SetGalleryAndLanguages { get; init; } = true;

    /// <summary>Where <see cref="PageExtras"/> keeps the required items it set; null for the per-user file.</summary>
    public string? RequiredRecord { get; init; }

    /// <summary>Where <see cref="PageExtras"/> remembers the gallery images and translations it sent; null for the per-user file.</summary>
    public string? SentRecord { get; init; }

    /// <summary>
    /// After each successful upload, download the item back with SteamCMD (as a subscriber gets it) and compare it with
    /// what was sent. Off by default: it downloads the whole mod again.
    /// </summary>
    public bool VerifyUploads { get; init; }

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

    /// <summary>True when <see cref="GitRef"/> applies: one is set and the layout packages with git.</summary>
    bool? _isGit;
    /// <summary>Whether the mods folder is a git repo, asked of git once per publisher instead of once per mod.</summary>
    bool IsGit => _isGit ??= ModScanner.IsGitRepo(_repo);

    bool FromRef => GitRef != null && _cfg.UseGitArchive && IsGit;

    /// <summary>A mod that is packaged, checked and has its build file written, ready for SteamCMD.</summary>
    sealed class Prepared
    {
        public required ModInfo Mod { get; init; }
        public required string Version { get; init; }
        public required string Notes { get; init; }
        public required PackageReport Report { get; init; }
        public required string[] Tags { get; init; }
        public string[] Required { get; init; } = Array.Empty<string>();
        public WorkshopPageEdit? Page { get; init; }
        public required int Visibility { get; init; }
        public required string StageRoot { get; init; }
        public required string VdfPath { get; init; }
        /// <summary>Steam's copy, when a one-login batch already downloaded it back to check the upload.</summary>
        public DownloadResult? Downloaded { get; set; }
    }

    public async Task<PublishResult> RunAsync(ModInfo m, bool upload, CancellationToken ct)
    {
        if (TestItems) m = m.AsTestItem();
        var (early, p) = await PrepareAsync(m, upload, ct);
        if (early != null) return early;

        // The package is removed on every way out except a failed upload, where it is kept so the build file can be inspected.
        var keep = false;
        try
        {
            var started = DateTimeOffset.UtcNow;
            var output = new List<string>();
            var exit = await RunProcAsync(_steamCmd,
                SteamCmd.FailFast.Concat(new[] { "+login", _account, "+workshop_build_item", p!.VdfPath, "+quit" }),
                Path.GetDirectoryName(_steamCmd) ?? "",
                line =>
                {
                    Say(line);
                    lock (output) output.Add(line);
                    MapStage(line);
                }, ct);
            List<string> lines;
            lock (output) lines = output.ToList();
            (var result, keep) = await FinishAsync(p, lines, exit, started, ct);
            return result;
        }
        finally
        {
            if (!keep) TryDelete(p!.StageRoot);
        }
    }

    /// <summary>
    /// Uploads several mods with one SteamCMD run: one login, then one workshop_build_item per mod, so a batch does not
    /// log in and start SteamCMD again for every mod. Each mod is packaged and checked first, the same as
    /// <see cref="RunAsync"/>; a mod that fails or is skipped there never reaches SteamCMD. SteamCMD's output is split
    /// into one part per mod at each "Success" or failure line, and each mod is then judged on its own part (and asked
    /// of Steam, as a single upload is). Returns one result per mod, in order; null for a mod that was not reached
    /// because the run was cancelled.
    /// </summary>
    /// <param name="starting">Called with a mod's index when SteamCMD starts on it.</param>
    public async Task<PublishResult?[]> RunSessionAsync(IReadOnlyList<ModInfo> mods, Action<int>? starting, CancellationToken ct)
    {
        var results = new PublishResult?[mods.Count];
        var ready = new List<(int Index, Prepared Item)>();
        var keep = new HashSet<string>();
        PackageAhead(mods, ct);
        try
        {
            for (var i = 0; i < mods.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var m = TestItems ? mods[i].AsTestItem() : mods[i];
                Say($"Packaging {m.Name} ({i + 1} of {mods.Count})...");
                try
                {
                    var (early, p) = await PrepareAsync(m, true, ct);
                    if (early != null) results[i] = early;
                    else ready.Add((i, p!));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e) { results[i] = new PublishResult(false, e.Message); }
            }
            if (ready.Count == 0) return results;

            Say($"Uploading {ready.Count} mod{(ready.Count == 1 ? "" : "s")} with one SteamCMD login.");
            var args = new List<string>(SteamCmd.NoPrompt) { "+login", _account };
            foreach (var (_, p) in ready) args.AddRange(new[] { "+workshop_build_item", p.VdfPath });
            // Checking uploads downloads each existing item back in the same run, so the check needs no login of its own.
            // New items get their id from this run, so they are checked afterwards as before.
            var checkInRun = VerifyUploads ? ready.Where(r => r.Item.Mod.WorkshopId != null).Select(r => r.Item.Mod.WorkshopId!).Distinct().ToList() : new List<string>();
            foreach (var id in checkInRun) args.AddRange(new[] { "+workshop_download_item", _cfg.AppId, id, "validate" });
            args.Add("+quit");

            // SteamCMD's lines arrive on another thread; the window's handlers expect to be called on its own.
            var ctx = SynchronizationContext.Current;
            void Starting(int index) { if (ctx != null) ctx.Post(_ => starting?.Invoke(index), null); else starting?.Invoke(index); }
            var parts = ready.Select(_ => new List<string>()).ToList();
            var downloads = new List<string>();
            var current = 0;
            var gate = new object();
            var started = DateTimeOffset.UtcNow;
            starting?.Invoke(ready[0].Index);
            var exit = await RunProcAsync(_steamCmd, args, Path.GetDirectoryName(_steamCmd) ?? "", line =>
            {
                Say(line);
                int? next = null;
                lock (gate)
                {
                    if (current >= parts.Count && checkInRun.Count > 0) downloads.Add(line); // after the last upload: the checks
                    else parts[Math.Min(current, parts.Count - 1)].Add(line);
                    if (current < parts.Count && SteamCmdOutput.EndsItem(line) && SteamCmd.LoginFailure(new[] { line }) == null)
                    {
                        current++;
                        if (current < parts.Count) next = ready[current].Index;
                    }
                }
                MapStage(line);
                if (next is { } n) Starting(n);
            }, ct);

            // A refused login fails every mod the same way.
            var all = parts.SelectMany(x => x).ToList();
            var login = SteamCmd.LoginFailure(all);
            for (var k = 0; k < ready.Count; k++)
            {
                var (index, p) = ready[k];
                if (login != null)
                {
                    keep.Add(p.StageRoot);
                    results[index] = new PublishResult(false, "SteamCMD reported a failure: " + login);
                    continue;
                }
                // A part that ended with "Success" is judged on its own lines; SteamCMD's exit code belongs to the run as a whole.
                var mine = parts[k];
                var itemExit = mine.Any(l => SteamCmdOutput.EndsItem(l)) ? 0 : exit;
                if (p.Mod.WorkshopId is { } checkedId && checkInRun.Contains(checkedId))
                    p.Downloaded = WorkshopDownload.Evaluate(downloads, exit, _steamCmd, _cfg.AppId, checkedId);
                var (result, kept) = await FinishAsync(p, mine, itemExit, started, ct);
                if (kept) keep.Add(p.StageRoot);
                results[index] = result;
            }
            return results;
        }
        finally
        {
            foreach (var (_, p) in ready) if (!keep.Contains(p.StageRoot)) TryDelete(p.StageRoot);
            await DropAheadAsync();
        }
    }

    /// <summary>
    /// Packages the mod, runs every check and writes the build file. Returns a finished result when the mod stops here
    /// (a dry run, a failed check, an unchanged package the user skipped), else the prepared upload. The package is
    /// removed unless a prepared upload is returned.
    /// </summary>
    async Task<(PublishResult? Done, Prepared? Item)> PrepareAsync(ModInfo m, bool upload, CancellationToken ct)
    {
        At("Reading changelog...", 5);
        var typed = NoteOverride?.Invoke(m);
        var notes = typed ?? NotesFor(m, _cfg);
        var version = m.Version;
        if (!_cfg.AppIdValid) return (new PublishResult(false, "The Steam app ID is not set. Enter the game's app ID in the window first."), null);
        if (GitRef != null && !FromRef) Say($"Note: \"{GitRef}\" is a git ref, but this layout does not package with git, so the working folder is uploaded.");
        if (_cfg.ChangelogByVersion && notes.Length == 0 && typed == null && !FromRef)
            return (new PublishResult(false, $"{_cfg.Changelog} has no entry for version {(m.Version.Length > 0 ? m.Version : "(unknown)")}."), null);
        var ahead = TakeAhead(m);
        if (ahead == null) SweepStale(_tempRoot, StaleAfter);
        var stageRoot = ahead?.Root ?? NewStageRoot();
        var stage = Path.Combine(stageRoot, ProjectConfig.Norm(m.RelPath));
        Directory.CreateDirectory(stageRoot);

        // The package is removed on every way out (dry run, a check failing, packaging throwing, cancellation) except
        // handing back a prepared upload.
        var handedOver = false;
        try
        {
            At("Packaging...", 10);
            if (ahead != null) await ahead.Value.Done;
            else await PackageAsync(m, stageRoot, stage, ct);
            File.Delete(Path.Combine(stage, ModInfo.TestIdFile)); // a test item's id is for this PC, not for players

            // From another commit, the version and change note are that commit's, read from the package.
            if (FromRef)
            {
                version = ModScanner.VersionIn(stage, _cfg) ?? m.Version;
                if (typed == null) notes = NotesFor(new ModInfo { Dir = stage, Version = version }, _cfg);
                if (_cfg.ChangelogByVersion && notes.Length == 0 && typed == null)
                    return (new PublishResult(false, $"{_cfg.Changelog} at {GitRef} has no entry for version {(version.Length > 0 ? version : "(unknown)")}."), null);
                Say($"Packaged {m.Name} from {GitRef} (version {(version.Length > 0 ? version : "unknown")}).");
            }
            // The template's {version} is the version sent, which differs from the mod's for an older git ref.
            var shaped = Shape(version == m.Version ? m : new ModInfo { Name = m.Name, Version = version }, notes);
            var sent = Credit ? shaped + (shaped.Length > 0 ? "\n\n" : "") + CreditLine : shaped;
            if (sent.Length > 7900)
                sent = shaped.Substring(0, 7900 - (Credit ? CreditLine.Length + 2 : 0)) + "\n[i]...see the full changelog on the project page.[/i]"
                    + (Credit ? "\n" + CreditLine : "");

            var report = PackageCheck.Inspect(stage, hash: ManifestDir != null || VerifyUploads);
            var last = ManifestDir != null && m.OnSteam ? PackageDiffs.Load(ManifestDir, m.WorkshopId!) : null;
            if (last != null) report = report with { Diff = PackageDiffs.Compare(last, report.Files) };
            // git archive only has what git tracks, so anything ignored (a built .dll above all) would silently stay behind.
            // From another commit the working folder is a different version, so there is nothing to compare with.
            var gitPackaging = _cfg.UseGitArchive && IsGit;
            if (gitPackaging && !FromRef) report = report with { LeftOut = PackageCheck.LeftOut(m.Dir, report.Files, PackageExclude) };
            Packaged?.Invoke(m, report);
            if (report.Files.Count == 0) return (new PublishResult(false, "The package is empty, so the upload would remove every file from the item on Steam."), null);
            var missing = PackageCheck.MissingRequired(report.Files, _cfg.RequiredInPackage());
            if (missing.Count > 0)
                return (new PublishResult(false, $"The package has no {string.Join(", ", missing)}, so the mod would break for everyone who updates. " +
                    (gitPackaging ? "Check it is committed and not left out by an export-ignore rule in .gitattributes." : "Check it exists and is not in the layout's Exclude list.")), null);
            var code = report.LeftOut.Where(PackageCheck.IsCode).ToList();
            if (code.Count > 0)
                return (new PublishResult(false, $"The package leaves out {PackageCheck.Examples(code)}, which {(code.Count == 1 ? "is" : "are")} in the mod folder but not in git " +
                    "(ignored by .gitignore or not committed), so players would get the mod without its code. Commit it, or add where the build lands to the " +
                    "layout's BuiltFiles (for example \"1.6/Assemblies/*.dll\"), or delete it if it is left over."), null);
            if (report.LeftOut.Count > 0)
                Say($"Warning: the package leaves out {report.LeftOut.Count} file{(report.LeftOut.Count == 1 ? "" : "s")} in the mod folder that git does not have (ignored or not committed): {PackageCheck.Examples(report.LeftOut)}.");
            foreach (var w in report.Warnings) Say("Warning: the package " + w + ".");
            if (report.Diff is { } diff)
            {
                foreach (var line in PackageDiffs.Describe(diff)) Say(line);
                foreach (var w in diff.Warnings()) Say("Warning: the package " + w + ".");
            }

            // Read from the package, so an upload from another git ref gets that commit's supported versions.
            var tags = WorkshopTags.LayoutHasTags(_cfg) ? WorkshopTags.For(stage, _cfg) : Array.Empty<string>();
            var required = Dependencies.FindInFolder(stage, _cfg, m.WorkshopId).ToArray();

            // A test item gets no page changes: they are meant for the real item's next upload.
            var page = TestItems ? new WorkshopPageEdit { Title = m.Name + " (test)" } : PageEdits?.Invoke(m);
            if (page != null && WorkshopPage.Validate(page) is { } bad) return (new PublishResult(false, "Workshop page: " + bad), null);
            string extra;
            var visibility = page?.Visibility ?? WorkshopPage.Private;
            if (!m.OnSteam)
            {
                var desc = page?.Description ?? ReadDescription(m);
                if (page?.Description == null && desc.Length > 0) desc = DescriptionSync.WithFooter(desc, DescriptionFooter, m.Name, "", _cfg.GameName);
                if (desc.Length < 20) return (new PublishResult(false, $"No description found. Add {_cfg.SteamDescriptionFile}."), null);
                var preview = page?.Preview ?? (string.IsNullOrWhiteSpace(_cfg.Preview) ? null : Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.Preview)));
                extra = WorkshopPage.VdfLines(page?.Title ?? m.Name, desc, null, preview) + WorkshopPage.VdfLines(null, null, visibility);
            }
            else
            {
                extra = page == null ? "" : WorkshopPage.VdfLines(page.Title, page.Description, page.Visibility, page.Preview);
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
            Say($"{(m.OnSteam ? "Workshop id " + m.WorkshopId : $"New {WorkshopPage.VisibilityName(visibility).ToLowerInvariant()} item")} | version {version} | {report.Files.Count} files, {report.MegaBytes} MB | notes {notes.Length} chars"
                + (TestItems ? " | TEST item" : ""));
            At("Package ready", 20);

            if (!upload)
            {
                if (tags.Length > 0) Say("Workshop tags to set after the upload: " + WorkshopTags.Describe(tags) + ".");
                Say("Dry run: nothing uploaded. The package was built and checked, then removed.");
                return (new PublishResult(true, "Dry run OK", null, Version: version != m.Version ? version : null), null);
            }

            if (report.Diff is { Identical: true } same && page == null)
            {
                if (UploadUnchanged != null && !UploadUnchanged(m, same))
                {
                    Say($"Skipped {m.Name}: its package is the same as {same.LastUpload}.");
                    return (new PublishResult(true, "Skipped: nothing changed " + same.Since, Skipped: true), null);
                }
                Say($"Warning: no file changed {same.Since}, so subscribers get only the change note.");
            }

            handedOver = true;
            return (null, new Prepared
            {
                Mod = m, Version = version, Notes = notes, Report = report, Tags = tags, Required = required, Page = page, Visibility = visibility,
                StageRoot = stageRoot, VdfPath = vdfPath,
            });
        }
        finally
        {
            if (!handedOver) TryDelete(stageRoot);
        }
    }

    /// <summary>
    /// Judges one mod's SteamCMD output and does everything after a successful upload. Returns the result and whether
    /// the package should be kept for inspection (a failed upload).
    /// </summary>
    async Task<(PublishResult Result, bool Keep)> FinishAsync(Prepared p, IReadOnlyList<string> output, int exit, DateTimeOffset started, CancellationToken ct)
    {
        var m = p.Mod;
        var version = p.Version;
        string? vdfAfter = null;
        try { vdfAfter = await File.ReadAllTextAsync(p.VdfPath, ct); } catch (IOException) { }
        var outcome = SteamCmdOutput.Evaluate(output, exit, m.WorkshopId, vdfAfter);

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
            Say("Package and build file kept for inspection in " + p.StageRoot);
            var msg = outcome.Reason;
            if (outcome.Unconfirmed && m.OnSteam && SteamCheck != null)
                msg += steamSays == false
                    ? " Steam's page still shows the previous update time."
                    : " Steam could not confirm it either (private items can't be read); check the item's change notes on Steam before uploading again.";
            if (!m.OnSteam)
                msg += newId != null
                    ? $" Steam did create item {newId} and its id was saved to {_cfg.WorkshopIdFile}: do NOT create it again."
                    : $" Check your Steam profile before trying again (a new item may not be public); if it was created, put its number in {_cfg.WorkshopIdFile} and do NOT create it again.";
            return (new PublishResult(false, msg, newId), true);
        }

        if (newId != null && TestItems) Say($"Created a PRIVATE test item {newId} for {m.Name} {version}. Saved to {ModInfo.TestIdFile} in the mod folder (keep it out of git).");
        else if (newId != null) Say($"Created {m.Name} {version} as {WorkshopPage.VisibilityName(p.Visibility).ToUpperInvariant()} item {newId}. Saved to {_cfg.WorkshopIdFile} (commit it).");
        else Say($"Uploaded {m.Name} {version}" + (TestItems ? $" to its test item {m.WorkshopId}" : ""));

        if (TagUpdater != null && p.Tags.Length > 0 && (m.WorkshopId ?? newId) is { } tagged)
        {
            At("Setting Workshop tags...", 98);
            var t = await TagUpdater(_cfg.AppId, tagged, p.Tags, ct);
            Say(t.Ok ? t.Message : $"Warning: the upload went through, but the Workshop tags were not set: {t.Message} " +
                "(\"Update Workshop tags\" on the mod's right-click menu tries again without uploading.)");
        }

        if (PageExtras != null && !TestItems && (m.WorkshopId ?? newId) is { } parent
            && WorkshopExtras.For(_cfg.AppId, parent, _cfg, m.Dir, SetRequired ? p.Required : Array.Empty<string>(), SetGalleryAndLanguages, RequiredRecord, SentRecord) is { } req)
        {
            if (!SetRequired) req.Required = null;
            if (req.Required != null || req.Gallery != null || req.Languages != null)
            {
                At("Updating the Workshop page...", 98);
                var x = await PageExtras(req, ct);
                if (x.RequiredNow != null) WorkshopExtras.Remember(parent, x.RequiredNow, RequiredRecord);
                if (x.Ok && x.PageSent) WorkshopExtras.RememberSent(req, SentRecord);
                foreach (var d in x.Done) Say("Workshop page: " + d + ".");
                if (!x.Ok) Say($"Warning: the upload went through, but the Workshop page was not fully updated: {x.Error} " +
                    "(\"Update required items, gallery and languages\" on the mod's right-click menu tries again without uploading.)");
                else if (x.Done.Count == 0) Say("Workshop page extras already right.");
            }
        }

        if (VerifyUploads && (m.WorkshopId ?? newId) is { } check) await VerifyAsync(m, check, p.Report.Files, p.Downloaded, ct);

        if (!TestItems && (m.WorkshopId ?? newId) is { } id)
        {
            if (HistoryFile != null) UploadHistory.Add(new UploadEntry(DateTimeOffset.UtcNow, id, m.Name, version, p.Notes, newId != null, _cfg.AppId,
                p.Report.TotalBytes, (DateTimeOffset.UtcNow - started).TotalSeconds), HistoryFile);
            WorkshopStatus.Forget(id);
            if (ManifestDir != null) PackageDiffs.Save(ManifestDir, id, new PackageManifest(DateTimeOffset.UtcNow, m.Version, p.Report.Files.ToList()));
            if (SentDescriptions != null && p.Page?.Description != null) DescriptionSync.Remember(id, p.Page.Description, SentDescriptions);
        }

        At("Uploaded", 100);
        return (new PublishResult(true, outcome.Reason, newId, Version: version != m.Version ? version : null), false);
    }

    /// <summary>
    /// Downloads the item back the way a subscriber gets it and compares it with what was just sent. Only warns: the
    /// upload itself already went through.
    /// </summary>
    async Task VerifyAsync(ModInfo m, string id, IReadOnlyList<PackageFile> sent, DownloadResult? downloaded, CancellationToken ct)
    {
        At("Checking what Steam hands out...", 99);
        DownloadResult d;
        if (downloaded != null) d = downloaded;
        else
        {
            Say($"Downloading {m.Name} back from Steam to check it...");
            d = await WorkshopDownload.DownloadAsync(_steamCmd, _account, _cfg.AppId, id, null, ct);
        }
        if (!d.Ok) { Say("Warning: could not download the item back to check it: " + d.Message); return; }
        try { foreach (var line in WorkshopDownload.Describe(WorkshopDownload.Compare(sent, d.Folder!), "this upload")) Say(line); }
        finally { WorkshopDownload.Forget(_steamCmd, d.Folder); }
    }

    /// <summary>
    /// Builds the mod's package exactly as an upload would (git archive or copy, plus build outputs) into
    /// <paramref name="dest"/>, for trying it in a game install. Nothing is checked or sent.
    /// </summary>
    public async Task PackageToAsync(ModInfo m, string dest, CancellationToken ct)
    {
        var stageRoot = Path.Combine(_tempRoot, StagePrefix + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(stageRoot, ProjectConfig.Norm(m.RelPath));
        Directory.CreateDirectory(stageRoot);
        try
        {
            await PackageAsync(m, stageRoot, stage, ct);
            File.Delete(Path.Combine(stage, ModInfo.TestIdFile));
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            CopyDir(stage, dest, Array.Empty<string>());
        }
        finally { TryDelete(stageRoot); }
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
        if (m.IsTest)
        {
            await File.WriteAllTextAsync(Path.Combine(m.Dir, ModInfo.TestIdFile), id, new UTF8Encoding(false), ct);
            return;
        }
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

    string NewStageRoot() => Path.Combine(_tempRoot, StagePrefix + Guid.NewGuid().ToString("N"));

    /// <summary>Packages started early by <see cref="PackageAhead"/>, by mod folder: where each goes, and its packaging.</summary>
    readonly Dictionary<string, (string Root, Task Done)> _ahead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many mods <see cref="PackageAhead"/> packages at the same time.</summary>
    const int PackageAheadAtOnce = 3;

    /// <summary>
    /// Starts packaging <paramref name="mods"/> in the background, a few at a time, so a batch doesn't wait for each
    /// package in turn: the next mods are ready while the current one is checked or uploaded. An upload of one of these
    /// mods then takes its package (<see cref="TakeAhead"/>) instead of building it again; everything else (the checks,
    /// the log lines, the questions) still happens in order. <see cref="DropAheadAsync"/> removes any not used.
    /// </summary>
    public void PackageAhead(IEnumerable<ModInfo> mods, CancellationToken ct)
    {
        SweepStale(_tempRoot, StaleAfter);
        var gate = new SemaphoreSlim(PackageAheadAtOnce);
        foreach (var m in mods)
        {
            if (_ahead.ContainsKey(m.Dir)) continue;
            var root = NewStageRoot();
            var stage = Path.Combine(root, ProjectConfig.Norm(m.RelPath));
            _ahead[m.Dir] = (root, Task.Run(async () =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    Directory.CreateDirectory(root);
                    await PackageAsync(m, root, stage, ct);
                }
                finally { gate.Release(); }
            }, ct));
        }
    }

    (string Root, Task Done)? TakeAhead(ModInfo m)
    {
        if (!_ahead.Remove(m.Dir, out var a)) return null;
        return a;
    }

    /// <summary>Waits for packages started by <see cref="PackageAhead"/> that no upload took, and removes them.</summary>
    public async Task DropAheadAsync()
    {
        var left = _ahead.Values.ToList();
        _ahead.Clear();
        foreach (var (root, done) in left)
        {
            try { await done; } catch { }
            TryDelete(root);
        }
    }

    async Task PackageAsync(ModInfo m, string stageRoot, string stage, CancellationToken ct)
    {
        if (_cfg.UseGitArchive && IsGit)
        {
            var zip = Path.Combine(stageRoot, "package.zip");
            var commit = GitRef ?? "HEAD";
            var code = await RunProcAsync("git", new[] { "archive", "--format=zip", "-o", zip, commit, "--", m.RelPath }, _repo, Say, ct);
            if (code != 0 || !File.Exists(zip))
                throw new InvalidOperationException(GitRef != null ? $"git archive of {GitRef} failed. Does that branch, tag or commit exist, and does it have this mod?" : "git archive failed. Is this mod committed?");
            ZipFile.ExtractToDirectory(zip, stageRoot, true);
            File.Delete(zip);
            if (!Directory.Exists(stage)) throw new InvalidOperationException("git archive produced nothing for " + m.Folder + ". Commit the mod first.");
        }
        else
        {
            CopyDir(m.Dir, stage, PackageExclude);
        }
        // The gallery is for the Workshop page, not for players.
        if ((_cfg.GalleryFolder ?? "").Trim() is { Length: > 0 } gallery && Directory.Exists(Path.Combine(stage, gallery))) Directory.Delete(Path.Combine(stage, gallery), true);

        // Build outputs usually are not tracked by git, so take them from the working tree. From another commit, ones
        // that commit has are kept; the working tree's would belong to different code.
        foreach (var glob in _cfg.BuiltFiles ?? Array.Empty<string>())
        {
            if (FromRef && ModScanner.MatchGlob(stage, glob).Count > 0) continue;
            if (FromRef && ModScanner.MatchGlob(m.Dir, glob).Count > 0)
                Say($"Warning: {GitRef} has nothing matching {glob}, so the working folder's build is used. Make sure it matches that version.");
            foreach (var f in ModScanner.MatchGlob(m.Dir, glob))
            {
                var rel = Path.GetRelativePath(m.Dir, f);
                var dest = Path.Combine(stage, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }
    }

    /// <summary>The layout's Exclude list plus the gallery folder.</summary>
    string[] PackageExclude => (_cfg.Exclude ?? Array.Empty<string>()).Concat((_cfg.GalleryFolder ?? "").Trim() is { Length: > 0 } g ? new[] { g } : Array.Empty<string>()).ToArray();

    static void CopyDir(string src, string dst, string[] exclude)
    {
        Directory.CreateDirectory(dst);
        bool Skip(string name) => PackageCheck.Excluded(name, exclude);
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

    internal static async Task<int> RunProcAsync(string file, IEnumerable<string> args, string workDir, Action<string> onLine, CancellationToken ct)
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
