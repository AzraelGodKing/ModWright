using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Installs a second copy of the game with SteamCMD, on any Steam branch (usually a beta such as RimWorld's
/// "unstable"), and puts mods and the Workshop items they need into it. Mods can then be tried on the next game version
/// without touching the normal Steam install. Needs the saved login of an account that owns the game.
/// </summary>
public static class TestInstall
{
    const RegexOptions Ic = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// The arguments. force_install_dir must come before the login (SteamCMD's rule). The beta password, when one is
    /// needed, is on SteamCMD's command line for the length of the run.
    /// </summary>
    public static string[] Args(string folder, string account, string appId, string branch, string? password)
    {
        var args = new List<string>(SteamCmd.FailFast) { "+force_install_dir", folder };
        args.AddRange(SteamCmd.Login(account));
        args.AddRange(new[] { "+app_update", appId });
        if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("public", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[] { "-beta", branch.Trim() });
            if (!string.IsNullOrEmpty(password)) args.AddRange(new[] { "-betapassword", password });
        }
        args.AddRange(new[] { "validate", "+quit" });
        return args.ToArray();
    }

    /// <summary>
    /// Why <paramref name="folder"/> should not be installed into, or null when it is fine: it must be empty, or already
    /// hold an install of this game made the same way (so it is updated), so game files never land among other files.
    /// </summary>
    public static string? FolderProblem(string folder, string appId)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Pick a folder for the test install.";
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any()) return null;
        if (File.Exists(Path.Combine(folder, "steamapps", $"appmanifest_{appId}.acf"))) return null;
        return "That folder is not empty and holds no test install of this game. Pick an empty folder, so the game's files don't land among other files.";
    }

    // "Update state (0x61) downloading, progress: 12.34 (123456 / 1000000)"
    static readonly Regex Progress = new(@"Update state \(0x[0-9a-f]+\)\s*([a-z][a-z ]*?),\s*progress:\s*([\d.]+)", Ic);
    static readonly Regex Installed = new(@"Success!\s*App '(\d+)'.*fully installed", Ic);
    static readonly Regex Failed = new(@"ERROR!.*|Error! App '\d+'.*|(?:^|\.\.\.)\s*FAILED.*", Ic);

    /// <summary>What SteamCMD is doing and how far it is (0 to 100), from one of its progress lines.</summary>
    public static (string State, double Percent)? ParseProgress(string line)
    {
        var m = Progress.Match(SteamCmd.Clean(line));
        if (!m.Success || !double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) return null;
        return (m.Groups[1].Value.Trim(), Math.Clamp(pct, 0, 100));
    }

    /// <summary>Whether the install worked, from SteamCMD's output.</summary>
    public static (bool Ok, string Message) Evaluate(IReadOnlyList<string> lines, int exit, string appId)
    {
        var clean = lines.Select(SteamCmd.Clean).ToList();
        if (SteamCmd.LoginFailure(clean) is { } login) return (false, "Steam refused the login: " + login);
        if (clean.Any(l => Installed.Match(l) is { Success: true } m && m.Groups[1].Value == appId)) return (true, "The game is installed.");
        var failed = clean.FirstOrDefault(l => Failed.IsMatch(l));
        if (failed != null)
            return (false, "SteamCMD could not install the game: " + failed +
                (Regex.IsMatch(failed, "password|Invalid Password|beta", Ic) ? " (A beta that needs a password only installs with the right one.)" : ""));
        return (false, exit != 0 ? $"SteamCMD exited with code {exit} before the game was installed." : "SteamCMD finished without saying the game was installed.");
    }

    /// <summary>Installs (or updates) the game into <paramref name="folder"/> on <paramref name="branch"/>.</summary>
    public static async Task<(bool Ok, string Message)> InstallGameAsync(string steamCmd, string account, string appId, string folder,
        string branch, string? password, Action<string>? onLine, Action<string, double>? onProgress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var run = await SteamCmd.RunAsync(steamCmd, Args(Path.GetFullPath(folder), account, appId, branch, password), line =>
        {
            if (ParseProgress(line) is { } p) onProgress?.Invoke(p.State, p.Percent);
            else onLine?.Invoke(line);
        }, ct);
        return Evaluate(run.Lines, run.Exit, appId);
    }

    /// <summary>
    /// Puts each mod, packaged as an upload would package it, into the game's mods folder
    /// (<see cref="ProjectConfig.GameModsFolder"/>), then downloads every Workshop item they name as a dependency into the
    /// same folder. Returns one line per problem; empty when everything is in place.
    /// </summary>
    public static async Task<List<string>> AddModsAsync(Publisher packager, string steamCmd, string account, ProjectConfig cfg, string folder,
        IReadOnlyList<ModInfo> mods, Action<string> say, CancellationToken ct)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(cfg.GameModsFolder)) { problems.Add("This layout does not say where the game loads mods from, so no mods were added."); return problems; }
        var modsDir = Path.Combine(folder, ProjectConfig.Norm(cfg.GameModsFolder));
        Directory.CreateDirectory(modsDir);

        foreach (var m in mods)
        {
            try
            {
                await packager.PackageToAsync(m, Path.Combine(modsDir, m.Folder), ct);
                say($"Added {m.Name} to {modsDir}.");
            }
            catch (Exception e) when (e is not OperationCanceledException) { problems.Add($"{m.Name}: could not package it: {e.Message}"); }
        }

        var own = mods.Select(m => m.WorkshopId).Where(id => id != null).ToHashSet();
        var deps = mods.SelectMany(m => Dependencies.Find(m, cfg)).Where(id => !own.Contains(id)).Distinct().ToList();
        foreach (var id in deps)
        {
            say($"Downloading Workshop item {id}, which a mod needs...");
            var d = await WorkshopDownload.DownloadAsync(steamCmd, account, cfg.AppId, id, null, ct);
            if (!d.Ok) { problems.Add($"Workshop item {id}: {d.Message}"); continue; }
            try
            {
                var dest = Path.Combine(modsDir, id);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                say($"Added Workshop item {id} ({WorkshopDownload.CopyTo(d.Folder!, dest)} files).");
            }
            catch (Exception e) { problems.Add($"Workshop item {id}: could not copy it: {e.Message}"); }
            finally { WorkshopDownload.Forget(steamCmd, d.Folder); }
        }
        return problems;
    }
}
