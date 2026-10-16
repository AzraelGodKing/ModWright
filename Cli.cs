namespace SteamCmdHelper;

/// <summary>Parsed command-line arguments for <see cref="Cli"/>.</summary>
public sealed class CliOptions
{
    public string Command = "help";
    public readonly List<string> Mods = new();
    public bool All, Changed, Json, DryRun, AllowCreate, AllowExperimental, CheckOnly, Test, NoBuild;
    /// <summary>upload: the steps the window offers after an upload (tag, commit new ids, Discord and GitHub release).</summary>
    public bool Tag, CommitIds, Announce;
    /// <summary>upload: draft each mod's change note from its commits since the last upload, instead of the changelog.</summary>
    public bool NoteFromGit;
    public string? Repo, Account, SteamCmd, LogFile, Note, Ref;
    /// <summary>Set when the arguments could not be understood; the command is then not run.</summary>
    public string? Error;
}

/// <summary>
/// The headless command line: <c>SteamCmdHelper upload &lt;mod&gt;...</c> for scripts and CI. It runs the same mod checks,
/// packaging and upload code as the window, and returns an exit code instead of showing dialogs.
/// </summary>
public static class Cli
{
    public const int ExitOk = 0, ExitFailed = 1, ExitUsage = 2, ExitSteam = 3;

    static readonly string[] Commands = { "upload", "list", "version", "update", "help" };

    public const string Usage = """
        SteamCMD Helper command line

        Usage:
          SteamCmdHelper upload <mod> [<mod>...] [options]   upload these mods (folder or display name)
          SteamCmdHelper upload --all [options]              upload every ready mod that is on Steam
          SteamCmdHelper upload --changed [options]          upload every ready mod whose version Steam does not
                                                             have yet (skips the ones already up to date)
          SteamCmdHelper list [--json] [options]             show the mods and whether they are ready
                                                             (--json: as JSON, for scripts)
          SteamCmdHelper version <mod> [<mod>...] <new>      set the version to upload: patch, minor, major
                [--note "<text>"]                            or a number such as 1.4.0. --note adds the new
                                                             version's changelog entry where the layout needs one
          SteamCmdHelper update [--check]                    install the newest release of this program
                                                             (--check: only say whether there is one)
          SteamCmdHelper help                                show this text

        Options:
          --repo <folder>        mods repo (default: the one picked in the window)
          --account <name>       Steam account (default: the one set in the window)
          --steamcmd <path>      steamcmd executable (default: the one the window uses)
          --dry-run              check and package only, upload nothing
          --allow-create         allow creating new PRIVATE Workshop items for mods not on Steam yet
          --allow-experimental   allow real uploads with an experimental layout
          --ref <branch|tag>     package this git branch, tag or commit instead of the checked-out one
                                 (default: Upload from in the window's Options)
          --test                 upload to each mod's private test item instead of the real one
          --no-build             skip the mods folder's build step (Options > Uploading)
          --log <file>           also write everything printed to this file

        Upload options:
          --note "<text>"        use this change note for every mod, instead of its changelog entry
          --note-from-git        draft each change note from the mod's commits since its last upload
          --tag                  tag each uploaded commit as <folder>-v<version> (git packaging only)
          --commit-ids           commit the Workshop id files of newly created items
          --announce             post to Discord and make GitHub releases, as set up in the window
          --json                 print only a JSON summary of the results; progress goes to stderr

        Every upload run's output is also saved with the window's past logs (the newest 50 are kept).

        The Steam login must already be saved: log in once from the window (Options > Log in...).
        Exit codes: 0 all done, 1 a mod failed or was not run, 2 bad arguments or nothing to upload,
        3 SteamCMD missing or not logged in.
        """;

    /// <summary>True when the arguments start with a command this class handles, so the window should not open.</summary>
    public static bool IsCommand(string[] args) =>
        args.Length > 0 && (Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase) || args[0] is "--help" or "-h" or "/?");

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        if (args.Length == 0) return o;
        o.Command = args[0].ToLowerInvariant() is "--help" or "-h" or "/?" ? "help" : args[0].ToLowerInvariant();
        if (!Commands.Contains(o.Command)) { o.Error = $"Unknown command \"{args[0]}\"."; return o; }

        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            string? Value()
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) return args[++i];
                o.Error ??= $"{a} needs a value.";
                return null;
            }
            switch (a.ToLowerInvariant())
            {
                case "--all": o.All = true; break;
                case "--changed": o.Changed = true; break;
                case "--json": o.Json = true; break;
                case "--dry-run": o.DryRun = true; break;
                case "--allow-create": o.AllowCreate = true; break;
                case "--allow-experimental": o.AllowExperimental = true; break;
                case "--repo": o.Repo = Value(); break;
                case "--account": o.Account = Value(); break;
                case "--steamcmd": o.SteamCmd = Value(); break;
                case "--log": o.LogFile = Value(); break;
                case "--note": o.Note = Value(); break;
                case "--check": o.CheckOnly = true; break;
                case "--ref": o.Ref = Value(); break;
                case "--test": o.Test = true; break;
                case "--no-build": o.NoBuild = true; break;
                case "--tag": o.Tag = true; break;
                case "--commit-ids": o.CommitIds = true; break;
                case "--announce": o.Announce = true; break;
                case "--note-from-git": o.NoteFromGit = true; break;
                default:
                    if (a.StartsWith("--")) o.Error ??= $"Unknown option {a}.";
                    else o.Mods.Add(a);
                    break;
            }
        }

        if (o.Error != null) return o;
        if (o.Changed && o.Command != "upload") { o.Error = "--changed only goes with \"upload\"."; return o; }
        if (o.Json && o.Command is not ("list" or "upload")) { o.Error = "--json only goes with \"list\" or \"upload\"."; return o; }
        if ((o.Ref != null || o.Test || o.NoBuild) && o.Command != "upload") { o.Error = "--ref, --test and --no-build only go with \"upload\"."; return o; }
        foreach (var (on, name) in new[] { (o.Tag, "--tag"), (o.CommitIds, "--commit-ids"), (o.Announce, "--announce"), (o.NoteFromGit, "--note-from-git") })
            if (on && o.Command != "upload") { o.Error = $"{name} only goes with \"upload\"."; return o; }
        if (o.NoteFromGit && o.Note != null) { o.Error = "Give --note or --note-from-git, not both."; return o; }
        if (o.Test && (o.Tag || o.CommitIds || o.Announce)) { o.Error = "--tag, --commit-ids and --announce do not go with --test: test items are private copies."; return o; }
        if (o.Command == "version")
        {
            if (o.Mods.Count < 2) o.Error = "Name the mods, then the new version: patch, minor, major or a number such as 1.4.0.";
            return o;
        }
        if (o.Note != null && o.Command != "upload") { o.Error = "--note only goes with \"version\" or \"upload\"."; return o; }
        if (o.Note != null && string.IsNullOrWhiteSpace(o.Note)) { o.Error = "--note needs some text."; return o; }
        if (o.Command == "upload" && (o.All || o.Changed) && o.Mods.Count > 0) o.Error = $"Give mod names or {(o.Changed ? "--changed" : "--all")}, not both.";
        else if (o.Command == "upload" && !o.All && !o.Changed && o.Mods.Count == 0) o.Error = "Name at least one mod to upload, or use --all or --changed.";
        else if (o.Command != "upload" && o.Mods.Count > 0) o.Error = $"\"{o.Command}\" does not take mod names.";
        else if (o.CheckOnly && o.Command != "update") o.Error = "--check only goes with \"update\".";
        return o;
    }

    /// <summary>
    /// "update": downloads the newest release, checked against the release's SHA-256 checksums, and installs it over
    /// this program once this command has ended. The new version is used from the next start. Exit code 0 when up to date or updated, 1 when it could not update.
    /// </summary>
    static async Task<int> UpdateAsync(bool checkOnly, Action<string> say, CancellationToken ct)
    {
        var current = UpdateCheck.CurrentVersion;
        var r = await UpdateCheck.FindNewerAsync(current, ct);
        if (r == null) { say($"SteamCMD Helper {current} is the newest version (or GitHub could not be reached)."); return ExitOk; }
        say($"SteamCMD Helper {r.Version} is available (you have {current}): {r.PageUrl}");
        if (checkOnly) return ExitOk;

        var dir = SelfUpdate.AppDir;
        var cannot = SelfUpdate.CannotUpdateReason(r, dir);
        if (cannot != null) { say("Cannot update in place: " + cannot + " Download it from the release page."); return ExitFailed; }
        try
        {
            var last = -10;
            var progress = new Progress<int>(p => { if (p >= last + 10) { last = p - p % 10; say($"  downloading... {last}%"); } });
            var u = await SelfUpdate.PrepareAsync(r, SelfUpdate.KindOf(dir), progress, ct);
            var exe = Environment.ProcessPath ?? Path.Combine(dir, SelfUpdate.ExeName);
            SelfUpdate.HandOff(u, dir, Path.GetFileName(exe), current, restart: false);
            say($"Downloaded {r.Version}. It is installed as soon as this command ends, and used from the next start.");
            return ExitOk;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { say("Update cancelled; nothing was changed."); return ExitFailed; }
        catch (Exception e) { say("Error: could not update: " + e.Message); return ExitFailed; }
    }

    /// <summary>
    /// The version Steam has, the way the window's Steam version column works it out: the newest change note on Steam,
    /// else this PC's last upload or record. Null when unknown. (The command line does not read the item's live
    /// "updated" time, so an upload from another PC since this one's is not noticed here.)
    /// </summary>
    public static string? SteamVersion(string? fromSteamNote, UploadEntry? last, string? recorded)
    {
        var (text, _) = UploadedVersions.Resolve(fromSteamNote, null, last, recorded);
        return text == "?" ? null : text;
    }

    /// <summary>"list --json": the repo and each mod, for scripts.</summary>
    public static string ListJson(string repo, ProjectConfig cfg, IEnumerable<ModInfo> mods) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            repo,
            appId = cfg.AppId,
            game = cfg.GameName,
            layout = cfg.Source,
            warning = cfg.Error,
            mods = mods.Select(m => new
            {
                folder = m.Folder,
                path = m.RelPath,
                name = m.Name,
                version = m.Version,
                workshopId = m.WorkshopId,
                ready = m.Ready,
                status = m.StatusText,
                problems = m.Problems,
                warnings = m.Warnings,
                uncommittedChanges = m.Dirty,
            }),
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    /// <summary>"version": writes the new version into each named mod, and its changelog entry when given one.</summary>
    static int SetVersions(CliOptions o, ProjectConfig cfg, List<ModInfo> mods, Action<string> say)
    {
        var spec = o.Mods[^1].Trim();
        ModScanner.Bump? part = spec.ToLowerInvariant() switch
        {
            "patch" => ModScanner.Bump.Patch, "minor" => ModScanner.Bump.Minor, "major" => ModScanner.Bump.Major, _ => null,
        };
        var picked = new List<ModInfo>();
        foreach (var name in o.Mods.Take(o.Mods.Count - 1))
        {
            var m = Find(mods, name);
            if (m == null) { say($"Error: {name}: no such mod in the repo. Nothing was changed."); return ExitUsage; }
            if (!picked.Contains(m)) picked.Add(m);
        }
        var failed = false;
        foreach (var m in picked)
        {
            var version = part is { } p ? ModScanner.NextVersion(m.Version, p) : spec.TrimStart('v', 'V');
            if (version == null) { say($"{m.Folder}: its version \"{m.Version}\" is not a number, so it cannot be raised. Give the new version instead."); failed = true; continue; }
            var error = ModScanner.SetVersion(m, cfg, version);
            if (error != null) { say($"{m.Folder}: {error}"); failed = true; continue; }
            var old = m.Version;
            m.Version = version;
            say($"{m.Folder}: version {(old.Length > 0 ? old : "?")} -> {version}.");
            if (!ModScanner.NeedsChangelogEntry(m, cfg)) continue;
            if (string.IsNullOrWhiteSpace(o.Note))
            {
                say($"  {cfg.Changelog} has no {version} entry yet, and the upload needs one. Add it, or run this with --note \"...\".");
                continue;
            }
            var noteError = ModScanner.AddChangelogEntry(m, cfg, version, o.Note);
            if (noteError != null) { say("  " + noteError); failed = true; }
            else say($"  Added the {version} entry to {cfg.Changelog}.");
        }
        return failed ? ExitFailed : ExitOk;
    }

    /// <summary>Finds a mod by folder name, path in the repo, or display name (case-insensitive).</summary>
    public static ModInfo? Find(IEnumerable<ModInfo> mods, string name)
    {
        var n = name.Replace('\\', '/').TrimEnd('/');
        return mods.FirstOrDefault(m => m.Folder.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? mods.FirstOrDefault(m => m.RelPath.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? mods.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs a parsed command. <paramref name="repo"/>, <paramref name="steamCmd"/> and <paramref name="account"/> already have the options applied.</summary>
    /// <param name="recordPath">Where uploaded versions are remembered; null for the per-user file.</param>
    /// <param name="tempRoot">Where packages are staged; null for the system temp folder.</param>
    /// <param name="credit">End change notes with <see cref="Publisher.CreditLine"/> (the app passes its Options setting).</param>
    /// <param name="noteTemplate">The mods folder's change note template; <paramref name="markdown"/> converts .md notes to BBCode.</param>
    /// <param name="syncDescriptions">Send each mod's changed description file with its upload (Options: Always send it).</param>
    /// <param name="buildCommand">The mods folder's build step, run before packaging unless --no-build.</param>
    /// <param name="defaultRef">The branch or tag to upload from when --ref is not given; null for the checked-out commit.</param>
    /// <param name="retryAfter">Wait before trying an update again when Steam was busy; null never retries.</param>
    /// <param name="webhook">The repo's Discord webhook, for "upload --announce"; empty when none is set.</param>
    /// <param name="gitHubReleases">Whether "upload --announce" makes GitHub releases for this repo.</param>
    /// <param name="json">Where "upload --json" writes its JSON; null for <paramref name="output"/>.</param>
    public static async Task<int> RunAsync(CliOptions o, string repo, string steamCmd, string account, TextWriter output,
        CancellationToken ct, string? recordPath = null, string? tempRoot = null, bool credit = false,
        string? noteTemplate = null, bool markdown = false, bool syncDescriptions = false,
        string? buildCommand = null, string? defaultRef = null, TimeSpan? retryAfter = null,
        string webhook = "", bool gitHubReleases = false, TextWriter? json = null)
    {
        void Say(string s) => output.WriteLine(s);
        var tags = new Dictionary<ModInfo, string>();
        // "upload --json" always ends with one JSON object, whatever stopped it.
        int End(int code, string? error = null, List<BatchItem>? ran = null)
        {
            if (o.Json && o.Command == "upload" && o.Error == null) (json ?? output).WriteLine(UploadJson(repo, o.DryRun, code, error, ran ?? new(), tags));
            return code;
        }

        if (o.Error != null) { Say("Error: " + o.Error); Say(""); Say(Usage); return ExitUsage; }
        if (o.Command == "help") { Say(Usage); return ExitOk; }
        if (o.Command == "update") return await UpdateAsync(o.CheckOnly, Say, ct);

        if (string.IsNullOrWhiteSpace(repo) || !Directory.Exists(repo))
        {
            var why = string.IsNullOrWhiteSpace(repo) ? "no mods repo. Pass --repo <folder> or pick one in the window." : "the mods repo folder does not exist: " + repo;
            Say("Error: " + why);
            return End(ExitUsage, why);
        }

        var cfg = ProjectConfig.Load(repo);
        var mods = ModScanner.Scan(repo, cfg);
        // JSON goes out alone, so a script can parse everything printed.
        if (o.Command == "list" && o.Json) { Say(ListJson(repo, cfg, mods)); return ExitOk; }
        if (cfg.Error != null) Say("Warning: " + cfg.Error);
        Say($"Repo: {repo}  |  {(cfg.GameName.Length > 0 ? cfg.GameName : "app " + cfg.AppId)}  |  layout: {cfg.Source}");
        // Kept beside the uploaded-versions record, so tests that pass their own record keep their history too.
        var historyFile = recordPath == null ? UploadHistory.DefaultFile : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recordPath))!, "history.json");

        if (o.Command == "version") return SetVersions(o, cfg, mods, Say);

        if (o.Command == "list")
        {
            if (mods.Count == 0) { Say("No mods found."); return ExitOk; }
            foreach (var m in mods)
                Say($"  {m.Folder}  |  {m.Name}  |  v{(m.Version.Length > 0 ? m.Version : "?")}  |  id {m.WorkshopId ?? "0 (new)"}  |  {m.StatusText}");
            return ExitOk;
        }

        // ---- upload: pick the mods
        var picked = new List<ModInfo>();
        if (o.All || o.Changed)
        {
            // Only ask Steam when it matters; runs with their own record (tests) stay offline.
            var history = o.Changed ? UploadHistory.Load(historyFile) : new();
            var record = o.Changed ? UploadedVersions.LoadRecord(recordPath) : new();
            foreach (var m in mods)
            {
                if (!m.Ready) Say($"Skipping {m.Folder}: {m.StatusText}");
                else if (!m.OnSteam && !o.AllowCreate) Say($"Skipping {m.Folder}: not on Steam yet (add --allow-create to create it as a private item).");
                else if (o.Changed && m.OnSteam)
                {
                    var note = recordPath == null ? await UploadedVersions.FetchAsync(m.WorkshopId!, ct) : null;
                    var last = UploadHistory.For(history, m.WorkshopId!).FirstOrDefault();
                    var onSteam = SteamVersion(note, last, record.GetValueOrDefault(m.WorkshopId!));
                    if (ModRows.State(m, onSteam, false) == RowState.UpToDate) Say($"Skipping {m.Folder}: Steam already has v{m.Version}.");
                    else
                    {
                        Say(onSteam == null ? $"{m.Folder}: the version on Steam is not known, so it is uploaded." : $"{m.Folder}: Steam has v{onSteam}, uploading v{m.Version}.");
                        picked.Add(m);
                    }
                }
                else picked.Add(m);
            }
        }
        else
        {
            var problems = new List<string>();
            foreach (var name in o.Mods)
            {
                var m = Find(mods, name);
                if (m == null) problems.Add($"{name}: no such mod in the repo.");
                else if (!m.Ready) problems.Add($"{m.Folder}: {m.StatusText}");
                else if (!m.OnSteam && !o.AllowCreate) problems.Add($"{m.Folder}: not on Steam yet. Add --allow-create to create it as a PRIVATE item.");
                else if (!picked.Contains(m)) picked.Add(m);
            }
            if (problems.Count > 0)
            {
                Say("Nothing was uploaded:");
                foreach (var p in problems) Say("  " + p);
                if (mods.Count > 0) Say("Mods in this repo: " + string.Join(", ", mods.Select(m => m.Folder)));
                return End(ExitUsage, "Nothing was uploaded: " + string.Join(" ", problems));
            }
        }
        if (picked.Count == 0 && o.Changed) { Say("Nothing to upload: every mod on Steam is up to date."); return End(ExitOk); }
        if (picked.Count == 0) { Say("Nothing to upload."); return End(ExitUsage, "Nothing to upload."); }

        if (!cfg.AppIdValid) { Say("Error: the Steam app ID is not set. Set it in the window or in " + ProjectConfig.FileName + "."); return End(ExitUsage, "The Steam app ID is not set."); }
        var gitRef = o.Ref ?? (string.IsNullOrWhiteSpace(defaultRef) ? null : defaultRef);
        if (gitRef != null && cfg.UseGitArchive && ModScanner.IsGitRepo(repo) && !GitOps.RefExists(repo, gitRef))
        {
            Say($"Error: \"{gitRef}\" is not a branch, tag or commit in {repo}.");
            return End(ExitUsage, $"\"{gitRef}\" is not a branch, tag or commit.");
        }
        if (!o.DryRun && cfg.Experimental && !o.AllowExperimental)
        {
            Say($"Error: the \"{cfg.PresetName()}\" layout is experimental. Try a new private item first; add --allow-experimental to upload anyway.");
            return End(ExitUsage, $"The \"{cfg.PresetName()}\" layout is experimental.");
        }
        var isGit = ModScanner.IsGitRepo(repo);
        if ((o.Tag || o.CommitIds || o.NoteFromGit) && !isGit)
        {
            Say("Error: --tag, --commit-ids and --note-from-git need the mods folder to be a git repo.");
            return End(ExitUsage, "The mods folder is not a git repo.");
        }
        if (o.Tag && !cfg.UseGitArchive)
        {
            Say("Error: --tag needs git packaging; this layout copies the working folder, so no commit matches what is uploaded.");
            return End(ExitUsage, "--tag needs git packaging.");
        }

        // ---- the change notes, when not the changelog's
        var notes = new Dictionary<ModInfo, string>();
        if (o.Note != null) foreach (var m in picked) notes[m] = o.Note.Trim();
        else if (o.NoteFromGit)
        {
            var history = UploadHistory.Load(historyFile);
            var record = UploadedVersions.LoadRecord(recordPath);
            foreach (var m in picked)
            {
                var last = m.OnSteam ? UploadHistory.For(history, m.WorkshopId!).FirstOrDefault() : null;
                var draft = GitOps.DraftNote(repo, m.RelPath, m.Folder, last?.Version ?? (m.OnSteam ? record.GetValueOrDefault(m.WorkshopId!) : null), last?.Time);
                if (draft == null) { Say($"{m.Folder}: no commits to draft a note from; using its changelog entry."); continue; }
                notes[m] = draft.Note;
                Say($"{m.Folder}: change note drafted from {draft.Count} commit{(draft.Count == 1 ? "" : "s")} ({draft.Source}).");
            }
        }

        if (!o.NoBuild && !await BuildStep.RunCheckedAsync(repo, buildCommand, Say, ct)) return ExitFailed;

        // ---- one login check for the whole batch
        if (!o.DryRun)
        {
            if (!SteamLogin.SteamCmdInstalled(steamCmd)) { Say("Error: SteamCMD is not installed at " + steamCmd + ". Install it from the window (Options) or pass --steamcmd."); return End(ExitSteam, "SteamCMD is not installed."); }
            if (string.IsNullOrWhiteSpace(account)) { Say("Error: no Steam account. Pass --account <name> or set it in the window."); return End(ExitSteam, "No Steam account."); }
            Say($"Checking the Steam login for {account}...");
            bool loggedIn;
            try { (loggedIn, _) = await SteamLogin.VerifyAsync(steamCmd, account, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { Say("Error: could not run SteamCMD: " + e.Message); return End(ExitSteam, "Could not run SteamCMD: " + e.Message); }
            if (!loggedIn)
            {
                Say($"Error: no saved Steam login for {account}. Log in once from the window (Options > Log in...), then run this again.");
                return End(ExitSteam, $"No saved Steam login for {account}.");
            }
            Say("Logged in.");
        }

        var sentDescriptions = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(historyFile))!, "descriptions.json");
        if (syncDescriptions && !o.DryRun && !o.Test)
        {
            var changed = await DescriptionSync.FindChangedAsync(picked, cfg, recordPath == null ? DescriptionSync.FetchFromSteam : (_, _) => Task.FromResult<string?>(null), sentDescriptions, ct);
            DescriptionSync.Stage(changed);
            foreach (var (m, _) in changed) Say($"{m.Folder}: {cfg.SteamDescriptionFile} changed, so the new description goes out with the upload.");
        }

        Say((o.DryRun ? "Dry run of " : "Uploading ") + $"{picked.Count} mod{(picked.Count == 1 ? "" : "s")}" + (o.Test ? " to their test items." : "."));
        // Workshop page changes saved in the window go out with the next upload, from here too.
        var pub = new Publisher(repo, cfg, steamCmd, account, tempRoot)
        {
            PageEdits = m => WorkshopPage.Get(m.Dir),
            NoteOverride = m => notes.GetValueOrDefault(m),
            HistoryFile = historyFile,
            ManifestDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(historyFile))!, "manifests"),
            SentDescriptions = sentDescriptions,
            // Asks Steam whether each update landed; runs with their own record (tests) stay offline.
            SteamCheck = recordPath == null ? Publisher.LiveSteamCheck : null,
            Credit = credit,
            NoteTemplate = noteTemplate,
            MarkdownToSteam = markdown,
            GitRef = gitRef,
            TestItems = o.Test,
        };
        pub.Log += l => Say("    " + l);
        var batch = new BatchUpload(pub, recordPath) { RetryAfter = retryAfter };
        batch.Retrying += (m, why, wait) => Say($"    Steam was busy or could not be reached ({why}). Trying {m.Folder} again in {wait.TotalSeconds:0} seconds...");
        batch.Starting += (m, i, n) => Say($"--- {m.Name} v{m.Version} ({i + 1} of {n}) ---");
        var uploadedCommit = isGit && cfg.UseGitArchive ? ModScanner.Git(repo, $"rev-parse {gitRef ?? "HEAD"}^{{commit}}").Trim() : "";
        var results = await batch.RunAsync(picked, !o.DryRun, ct);
        if (!o.DryRun && !o.Test) foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created)) WorkshopPage.Clear(r.Mod.Dir);

        Say("");
        Say(BatchUpload.Summary(results));
        if (o.Test && results.Any(r => r.Status == BatchStatus.Created))
            Say("New test items are PRIVATE and their ids were saved to " + ModInfo.TestIdFile + " in each mod folder: keep that file out of git.");
        var afterOk = true;
        if (o.DryRun)
        {
            if (o.Tag || o.CommitIds || o.Announce) Say("Dry run: nothing was tagged, committed or announced.");
        }
        else if (!o.Test)
        {
            afterOk &= CommitNewIds(o, repo, cfg, results, Say);
            if (o.Tag) afterOk &= TagUploads(repo, results, tags, uploadedCommit, Say);
            if (o.Announce) afterOk &= await AnnounceAsync(repo, cfg, results, m => notes.GetValueOrDefault(m) ?? Publisher.NotesFor(m, cfg), uploadedCommit, webhook, gitHubReleases, Say, ct);
        }
        return End(results.All(r => r.Ok) && afterOk ? ExitOk : ExitFailed, ran: results);
    }

    /// <summary>Commits the id files of newly created items with --commit-ids, else reminds to. False when git refused.</summary>
    static bool CommitNewIds(CliOptions o, string repo, ProjectConfig cfg, List<BatchItem> results, Action<string> say)
    {
        var created = results.Where(r => r.Status == BatchStatus.Created).ToList();
        if (created.Count == 0) return true;
        if (!o.CommitIds) { say("New items are PRIVATE and their ids were saved to " + cfg.WorkshopIdFile + ": commit them (or use --commit-ids)."); return true; }
        say("New items are PRIVATE.");
        var files = created.Select(r => (r.Mod.RelPath + "/" + cfg.WorkshopIdFile).Replace('\\', '/').TrimStart('/')).ToList();
        var names = string.Join(", ", created.Select(r => r.Mod.Name));
        var error = GitOps.CommitPaths(repo, files, $"Add Workshop ID for {names}");
        say(error == null ? $"Committed the Workshop ID file for {names}." : "Could not commit the Workshop ID: " + error);
        return error == null;
    }

    /// <summary>Tags each uploaded mod's commit as <see cref="GitOps.TagName"/>. False when a tag could not be made.</summary>
    static bool TagUploads(string repo, List<BatchItem> results, Dictionary<ModInfo, string> tags, string commit, Action<string> say)
    {
        var ok = true;
        foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created))
        {
            var tag = GitOps.TagName(r.Mod.Folder, r.Version);
            var result = GitOps.Tag(repo, tag, $"{r.Mod.Name} {r.Version} uploaded to Steam Workshop item {r.WorkshopId}", commit.Length > 0 ? commit : "HEAD");
            if (result == null) { say($"Tagged {tag}."); tags[r.Mod] = tag; }
            else if (result == "exists") { say($"Tag {tag} already exists; left as it is."); tags[r.Mod] = tag; }
            else { say($"Could not tag {tag}: {result}"); ok = false; }
        }
        return ok;
    }

    /// <summary>
    /// Posts updated mods to the repo's Discord webhook and makes GitHub releases for them, as set up in the window.
    /// New items are private, so they are not announced. False when an announcement failed.
    /// </summary>
    static async Task<bool> AnnounceAsync(string repo, ProjectConfig cfg, List<BatchItem> results, Func<ModInfo, string> noteOf,
        string commit, string webhook, bool gitHubReleases, Action<string> say, CancellationToken ct)
    {
        var done = results.Where(r => r.Status == BatchStatus.Uploaded && r.WorkshopId != null).ToList();
        if (done.Count == 0) return true;
        if (webhook.Length == 0 && !gitHubReleases)
        {
            say("Nothing to announce to: set a Discord webhook or turn on GitHub releases for this folder in the window (Options).");
            return true;
        }
        var ok = true;
        if (webhook.Length > 0)
            foreach (var r in done)
            {
                var error = await Announce.PostDiscordAsync(webhook, Announce.DiscordPayload(r.Mod.Name, r.Version, r.WorkshopId!, noteOf(r.Mod), cfg.GameName), ct: ct);
                say(error == null ? $"Announced {r.Mod.Name} {r.Version} on Discord." : $"Could not announce {r.Mod.Name} on Discord: {error}");
                ok &= error == null;
            }
        if (!gitHubReleases) return ok;
        if (commit.Length == 0) { say("No GitHub release: this layout does not upload a git commit."); return ok; }
        foreach (var r in done)
        {
            var tag = GitOps.TagName(r.Mod.Folder, r.Version);
            var note = noteOf(r.Mod) + $"\n\nSteam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id={r.WorkshopId}";
            try { say($"GitHub release {tag}: {Announce.CreateGitHubRelease(repo, tag, $"{r.Mod.Name} {r.Version}", note, commit)}"); }
            catch (Exception e) { say($"Could not make the GitHub release {tag}: {e.Message}"); ok = false; }
        }
        return ok;
    }

    /// <summary>"upload --json": what happened to each mod, for scripts. <paramref name="error"/> says why nothing ran.</summary>
    public static string UploadJson(string repo, bool dryRun, int exitCode, string? error, IEnumerable<BatchItem> results, IReadOnlyDictionary<ModInfo, string> tags) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            repo,
            dryRun,
            exitCode,
            ok = exitCode == ExitOk,
            error,
            mods = results.Select(r => new
            {
                folder = r.Mod.Folder,
                name = r.Mod.Name,
                version = r.Version,
                workshopId = r.WorkshopId ?? r.Mod.WorkshopId,
                status = r.Status.ToString().ToLowerInvariant(),
                ok = r.Ok,
                message = r.Message,
                tag = tags.GetValueOrDefault(r.Mod),
            }),
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
