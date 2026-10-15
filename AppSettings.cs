using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>Saved under %LocalAppData%\SteamCmdHelper\settings.json. Never holds a password.</summary>
public sealed class AppSettings
{
    public string RepoPath { get; set; } = "";
    public string SteamCmdPath { get; set; } = "";
    public string Account { get; set; } = "";
    public bool AllowCreate { get; set; }
    /// <summary>"Dark" (the default) or "Light".</summary>
    public string Theme { get; set; } = "Dark"; // Theme.DarkName; spelled out so the tests can compile this file without WinForms
    /// <summary>Look for a newer release on GitHub at start-up.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Opt-in: download a newer release in the background and install it when the window closes.</summary>
    public bool AutoUpdate { get; set; }
    /// <summary>A release version the user said not to be reminded about again.</summary>
    public string SkippedUpdate { get; set; } = "";
    /// <summary>Mods folders opened before, newest first (the drop-down next to Mods repo).</summary>
    public List<string> RecentRepos { get; set; } = new();
    /// <summary>The mod list's "Hide up to date" box.</summary>
    public bool HideUpToDate { get; set; }

    public const int MaxRecent = 10;

    /// <summary>Settings for a screenshot run: used but never written over the real file.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool DoNotSave { get; init; }

    /// <summary>Makes <paramref name="path"/> the current repo and puts it at the top of the recent list.</summary>
    public void Remember(string path)
    {
        path = Path.TrimEndingDirectorySeparator(path.Trim());
        if (path.Length == 0) return;
        RepoPath = path;
        RecentRepos.RemoveAll(p => string.Equals(Path.TrimEndingDirectorySeparator(p), path, StringComparison.OrdinalIgnoreCase));
        RecentRepos.Insert(0, path);
        if (RecentRepos.Count > MaxRecent) RecentRepos.RemoveRange(MaxRecent, RecentRepos.Count - MaxRecent);
    }

    /// <summary>The recent folders that still exist, the current one first (also when it was set before this list existed).</summary>
    public List<string> RecentThatExist()
    {
        var all = new List<string>();
        if (!string.IsNullOrWhiteSpace(RepoPath)) all.Add(RepoPath);
        all.AddRange(RecentRepos);
        return all.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Folder => Dir;

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamCmdHelper");

    static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>Where "Install SteamCMD" puts it when no other copy is found.</summary>
    public static string DefaultSteamCmd => Path.Combine(Dir, "steamcmd", "steamcmd.exe");

    /// <summary>
    /// An explicit path wins. Otherwise a copy already inside the repo (_tools\steamcmd, used by earlier versions
    /// and by the old PowerShell script) is reused, else the per-user location.
    /// </summary>
    public string EffectiveSteamCmd
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SteamCmdPath)) return SteamCmdPath;
            if (!string.IsNullOrWhiteSpace(RepoPath))
            {
                var inRepo = Path.Combine(RepoPath, "_tools", "steamcmd", "steamcmd.exe");
                if (File.Exists(inRepo)) return inRepo;
            }
            return DefaultSteamCmd;
        }
    }

    public static AppSettings Load()
    {
        AppSettings s = new();
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* fall through to defaults */ }
        return s;
    }

    public void Save()
    {
        if (DoNotSave) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are a convenience; ignore write failures */ }
    }
}
