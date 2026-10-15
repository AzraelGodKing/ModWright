namespace SteamCmdHelper;

/// <summary>Parsed command-line arguments for <see cref="Cli"/>.</summary>
public sealed class CliOptions
{
    public string Command = "help";
    public readonly List<string> Mods = new();
    public bool All, DryRun, AllowCreate, AllowExperimental;
    public string? Repo, Account, SteamCmd, LogFile;
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

    static readonly string[] Commands = { "upload", "list", "help" };

    public const string Usage = """
        SteamCMD Helper command line

        Usage:
          SteamCmdHelper upload <mod> [<mod>...] [options]   upload these mods (folder or display name)
          SteamCmdHelper upload --all [options]              upload every ready mod that is on Steam
          SteamCmdHelper list [options]                      show the mods and whether they are ready
          SteamCmdHelper help                                show this text

        Options:
          --repo <folder>        mods repo (default: the one picked in the window)
          --account <name>       Steam account (default: the one set in the window)
          --steamcmd <path>      steamcmd executable (default: the one the window uses)
          --dry-run              check and package only, upload nothing
          --allow-create         allow creating new PRIVATE Workshop items for mods not on Steam yet
          --allow-experimental   allow real uploads with an experimental layout
          --log <file>           also write everything printed to this file

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
                case "--dry-run": o.DryRun = true; break;
                case "--allow-create": o.AllowCreate = true; break;
                case "--allow-experimental": o.AllowExperimental = true; break;
                case "--repo": o.Repo = Value(); break;
                case "--account": o.Account = Value(); break;
                case "--steamcmd": o.SteamCmd = Value(); break;
                case "--log": o.LogFile = Value(); break;
                default:
                    if (a.StartsWith("--")) o.Error ??= $"Unknown option {a}.";
                    else o.Mods.Add(a);
                    break;
            }
        }

        if (o.Error != null) return o;
        if (o.Command == "upload" && o.All && o.Mods.Count > 0) o.Error = "Give mod names or --all, not both.";
        else if (o.Command == "upload" && !o.All && o.Mods.Count == 0) o.Error = "Name at least one mod to upload, or use --all.";
        else if (o.Command != "upload" && o.Mods.Count > 0) o.Error = $"\"{o.Command}\" does not take mod names.";
        return o;
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
    public static async Task<int> RunAsync(CliOptions o, string repo, string steamCmd, string account, TextWriter output,
        CancellationToken ct, string? recordPath = null, string? tempRoot = null)
    {
        void Say(string s) => output.WriteLine(s);

        if (o.Error != null) { Say("Error: " + o.Error); Say(""); Say(Usage); return ExitUsage; }
        if (o.Command == "help") { Say(Usage); return ExitOk; }

        if (string.IsNullOrWhiteSpace(repo) || !Directory.Exists(repo))
        {
            Say(string.IsNullOrWhiteSpace(repo) ? "Error: no mods repo. Pass --repo <folder> or pick one in the window." : "Error: the mods repo folder does not exist: " + repo);
            return ExitUsage;
        }

        var cfg = ProjectConfig.Load(repo);
        if (cfg.Error != null) Say("Warning: " + cfg.Error);
        var mods = ModScanner.Scan(repo, cfg);
        Say($"Repo: {repo}  |  {(cfg.GameName.Length > 0 ? cfg.GameName : "app " + cfg.AppId)}  |  layout: {cfg.Source}");

        if (o.Command == "list")
        {
            if (mods.Count == 0) { Say("No mods found."); return ExitOk; }
            foreach (var m in mods)
                Say($"  {m.Folder}  |  {m.Name}  |  v{(m.Version.Length > 0 ? m.Version : "?")}  |  id {m.WorkshopId ?? "0 (new)"}  |  {m.StatusText}");
            return ExitOk;
        }

        // ---- upload: pick the mods
        var picked = new List<ModInfo>();
        if (o.All)
        {
            foreach (var m in mods)
            {
                if (!m.Ready) Say($"Skipping {m.Folder}: {m.StatusText}");
                else if (!m.OnSteam && !o.AllowCreate) Say($"Skipping {m.Folder}: not on Steam yet (add --allow-create to create it as a private item).");
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
                return ExitUsage;
            }
        }
        if (picked.Count == 0) { Say("Nothing to upload."); return ExitUsage; }

        if (!cfg.AppIdValid) { Say("Error: the Steam app ID is not set. Set it in the window or in " + ProjectConfig.FileName + "."); return ExitUsage; }
        if (!o.DryRun && cfg.Experimental && !o.AllowExperimental)
        {
            Say($"Error: the \"{cfg.PresetName()}\" layout is experimental. Try a new private item first; add --allow-experimental to upload anyway.");
            return ExitUsage;
        }

        // ---- one login check for the whole batch
        if (!o.DryRun)
        {
            if (!SteamLogin.SteamCmdInstalled(steamCmd)) { Say("Error: SteamCMD is not installed at " + steamCmd + ". Install it from the window (Options) or pass --steamcmd."); return ExitSteam; }
            if (string.IsNullOrWhiteSpace(account)) { Say("Error: no Steam account. Pass --account <name> or set it in the window."); return ExitSteam; }
            Say($"Checking the Steam login for {account}...");
            bool loggedIn;
            try { (loggedIn, _) = await SteamLogin.VerifyAsync(steamCmd, account, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { Say("Error: could not run SteamCMD: " + e.Message); return ExitSteam; }
            if (!loggedIn)
            {
                Say($"Error: no saved Steam login for {account}. Log in once from the window (Options > Log in...), then run this again.");
                return ExitSteam;
            }
            Say("Logged in.");
        }

        Say((o.DryRun ? "Dry run of " : "Uploading ") + $"{picked.Count} mod{(picked.Count == 1 ? "" : "s")}.");
        // Workshop page changes saved in the window go out with the next upload, from here too.
        var pub = new Publisher(repo, cfg, steamCmd, account, tempRoot)
        {
            PageEdits = m => WorkshopPage.Get(m.Dir),
            // Kept beside the uploaded-versions record, so tests that pass their own record keep their history too.
            HistoryFile = recordPath == null ? UploadHistory.DefaultFile : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recordPath))!, "history.json"),
        };
        pub.Log += l => Say("    " + l);
        var batch = new BatchUpload(pub, recordPath);
        batch.Starting += (m, i, n) => Say($"--- {m.Name} v{m.Version} ({i + 1} of {n}) ---");
        var results = await batch.RunAsync(picked, !o.DryRun, ct);
        if (!o.DryRun) foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created)) WorkshopPage.Clear(r.Mod.Dir);

        Say("");
        Say(BatchUpload.Summary(results));
        if (results.Any(r => r.Status == BatchStatus.Created))
            Say("New items are PRIVATE and their ids were saved to " + cfg.WorkshopIdFile + ": commit them.");
        return results.All(r => r.Ok) ? ExitOk : ExitFailed;
    }
}
