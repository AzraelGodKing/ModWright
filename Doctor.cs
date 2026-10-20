using System.Diagnostics;

namespace SteamCmdHelper;

/// <summary>How a setup check came out: fine, worth a look, or something that stops uploads.</summary>
public enum CheckLevel { Ok, Warn, Fail }

/// <summary>One line of the setup check.</summary>
public sealed record SetupCheck(CheckLevel Level, string Name, string Detail)
{
    public override string ToString() => (Level switch { CheckLevel.Ok => "OK    ", CheckLevel.Warn => "CHECK ", _ => "FIX   " }) + Name + ": " + Detail;
}

/// <summary>
/// "Check setup" (Options > About) and "SteamCmdHelper doctor": goes through everything an upload needs, from the mods
/// folder to SteamCMD, the saved login, git, gh and the Steam client, and says what to fix. Nothing is changed.
/// </summary>
public static class Doctor
{
    /// <summary>What the checks look at outside the settings, so tests can stand in for this PC.</summary>
    public sealed class Probes
    {
        /// <summary>True when a command-line tool (git, gh) runs.</summary>
        public Func<string, bool> ToolRuns { get; init; } = DefaultToolRuns;
        /// <summary>True when the Steam client is running.</summary>
        public Func<bool> SteamRunning { get; init; } = DefaultSteamRunning;
        /// <summary>The Steam client's state, for bringing it back online after uploads.</summary>
        public Func<SteamRestart.State> SteamState { get; init; } = SteamRestart.ReadState;
        /// <summary>Free bytes on the drive that holds a folder.</summary>
        public Func<string, long?> FreeBytes { get; init; } = DefaultFreeBytes;
        /// <summary>The real login check (SteamCMD with the saved login); null skips it.</summary>
        public Func<string, string, CancellationToken, Task<bool>>? Login { get; init; }
    }

    /// <summary>Less free space than this where packages are staged is worth a warning.</summary>
    public const long LowSpace = 2L * 1024 * 1024 * 1024;

    /// <param name="repo">The mods folder, steamCmd and account as the run uses them (the command line can override the saved ones).</param>
    public static async Task<List<SetupCheck>> RunAsync(AppSettings s, string repo, string steamCmd, string account, Probes? probes = null, string? tempRoot = null, CancellationToken ct = default)
    {
        probes ??= new Probes();
        var checks = new List<SetupCheck>();
        void Add(CheckLevel level, string name, string detail) => checks.Add(new SetupCheck(level, name, detail));

        // ---- the mods folder and its layout
        ProjectConfig? cfg = null;
        List<ModInfo> mods = new();
        if (string.IsNullOrWhiteSpace(repo)) Add(CheckLevel.Fail, "Mods folder", "none picked yet (Upload tab > Browse..., or --repo).");
        else if (!Directory.Exists(repo)) Add(CheckLevel.Fail, "Mods folder", repo + " does not exist any more.");
        else
        {
            cfg = ProjectConfig.Load(repo);
            mods = ModScanner.Scan(repo, cfg);
            var broken = mods.Count(m => !m.Ready);
            Add(mods.Count == 0 ? CheckLevel.Warn : CheckLevel.Ok, "Mods folder",
                $"{repo}: {mods.Count} mod{(mods.Count == 1 ? "" : "s")}" + (broken > 0 ? $", {broken} need fixing before upload (see their Status)" : "") + ".");
            if (cfg.Error != null) Add(CheckLevel.Fail, "Layout", cfg.Error);
            else Add(CheckLevel.Ok, "Layout", cfg.Source);
            if (!cfg.AppIdValid) Add(CheckLevel.Fail, "Steam app ID", "not set: enter the game's app ID (the number in its store page address).");
            else Add(CheckLevel.Ok, "Steam app ID", cfg.AppId + (cfg.GameName.Length > 0 ? $" ({cfg.GameName})" : "") + ".");
        }

        // ---- SteamCMD and the login
        var hasSteamCmd = SteamLogin.SteamCmdInstalled(steamCmd);
        Add(hasSteamCmd ? CheckLevel.Ok : CheckLevel.Fail, "SteamCMD", hasSteamCmd ? steamCmd : $"not installed at {steamCmd}: Options > SteamCMD > Install SteamCMD.");
        if (string.IsNullOrWhiteSpace(account)) Add(CheckLevel.Fail, "Steam account", "none set: Options > Account > Account name.");
        else if (!hasSteamCmd) Add(CheckLevel.Warn, "Steam login", "can't be checked until SteamCMD is installed.");
        else if (probes.Login != null)
        {
            var ok = false;
            try { ok = await probes.Login(steamCmd, account, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { }
            Add(ok ? CheckLevel.Ok : CheckLevel.Fail, "Steam login", ok ? $"{account} is logged in and the login is saved." : $"no saved login for {account}: Options > Account > Log in....");
        }
        else if (SteamLogin.LooksCached(steamCmd, account)) Add(CheckLevel.Ok, "Steam login", $"{account} has a saved login (not tried; doctor --login tries it).");
        else Add(CheckLevel.Warn, "Steam login", $"no saved login found for {account}; the first upload asks for the password in SteamCMD's window.");

        // ---- git, gh and the Steam client, only when something here uses them
        if (cfg != null)
        {
            var isGit = ModScanner.IsGitRepo(repo);
            if (cfg.UseGitArchive)
            {
                if (!probes.ToolRuns("git")) Add(CheckLevel.Fail, "git", "this layout packages with git, but git is not on PATH. Install Git for Windows.");
                else Add(CheckLevel.Ok, "git", isGit ? "found; the mods folder is a git repo." : "found; the mods folder is not a git repo, so the working folder is uploaded as it is.");
            }
            if (s.GitHubReleasesFor(repo))
                Add(probes.ToolRuns("gh") ? CheckLevel.Ok : CheckLevel.Fail, "gh (GitHub releases)", probes.ToolRuns("gh") ? "found." : "GitHub releases are on for this folder, but the gh tool is not on PATH (cli.github.com, then gh auth login).");
            if (s.WebhookFor(repo) is { Length: > 0 } hook && !Announce.IsDiscordWebhook(hook))
                Add(CheckLevel.Fail, "Discord webhook", "the link set for this folder is not a Discord webhook link.");
            var viaClient = (s.UpdateTags && WorkshopTags.LayoutHasTags(cfg)) || s.UpdateRequired || s.UpdatePageExtras;
            if (viaClient)
                Add(probes.SteamRunning() ? CheckLevel.Ok : CheckLevel.Warn, "Steam client",
                    probes.SteamRunning() ? "running (tags, required items, gallery and translations go through it)."
                                          : "not running: tags, required items, gallery and translations are set through it after each upload, so start Steam as the mods' owner first.");
            if (s.SteamBackOnline && probes.SteamState() is { Running: true } steam)
            {
                if (steam.RememberedAccount.Length == 0)
                    Add(CheckLevel.Warn, "Steam restart", "Steam has no saved login, so when it is restarted after an upload it waits for your password. " +
                        "Sign in to Steam once with \"Remember me\" ticked, or turn off Options > Uploading > Bring the Steam client back online.");
                else if (account.Length > 0 && !steam.RememberedAccount.Equals(account, StringComparison.OrdinalIgnoreCase))
                    Add(CheckLevel.Ok, "Steam restart", $"Steam signs in as {steam.RememberedAccount}, not {account}, so uploads do not log it out and it is left alone.");
                else
                    Add(CheckLevel.Ok, "Steam restart", $"after an upload, Steam is restarted and signs back in as {steam.RememberedAccount}.");
            }
            if (s.ThunderstoreAfterUpload(repo) && string.IsNullOrEmpty(s.ThunderstoreToken))
                Add(CheckLevel.Fail, "Thunderstore", "publishing after each upload is on, but no token is set (Options > Uploading > Thunderstore).");
            if (s.BuildFor(repo) is { Length: > 0 } build) Add(CheckLevel.Ok, "Build step", build);
        }

        // ---- room to work
        var temp = tempRoot ?? Path.GetTempPath();
        if (probes.FreeBytes(temp) is { } free)
            Add(free < LowSpace ? CheckLevel.Warn : CheckLevel.Ok, "Free space", $"{PackageCheck.Size(free)} free where packages are built ({temp})" + (free < LowSpace ? ": a big mod may not fit." : "."));
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            var probe = Path.Combine(AppSettings.Folder, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            Add(CheckLevel.Ok, "Data folder", AppSettings.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Add(CheckLevel.Fail, "Data folder", $"{AppSettings.Folder} can't be written ({e.Message}), so settings and history are not kept.");
        }
        return checks;
    }

    /// <summary>One line for the end: all fine, or how many things to fix and check.</summary>
    public static string Summary(IReadOnlyCollection<SetupCheck> checks)
    {
        var fail = checks.Count(c => c.Level == CheckLevel.Fail);
        var warn = checks.Count(c => c.Level == CheckLevel.Warn);
        return fail == 0 && warn == 0 ? "Everything an upload needs is in place."
            : string.Join(" and ", new[] { fail > 0 ? $"{fail} thing{(fail == 1 ? "" : "s")} to fix" : null, warn > 0 ? $"{warn} to check" : null }.Where(x => x != null)) + ".";
    }

    static bool DefaultToolRuns(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return false;
            p.StandardOutput.ReadToEnd();
            return p.WaitForExit(10000) && p.ExitCode == 0;
        }
        catch { return false; }
    }

    static bool DefaultSteamRunning()
    {
        try { return Process.GetProcessesByName("steam").Length > 0; } catch { return false; }
    }

    static long? DefaultFreeBytes(string folder)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace; } catch { return null; }
    }
}
