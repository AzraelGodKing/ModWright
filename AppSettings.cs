using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>A named set of mods folder, Steam account and SteamCMD, to switch between in one click.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "";
    public string RepoPath { get; set; } = "";
    public string Account { get; set; } = "";
    public string SteamCmdPath { get; set; } = "";
}

/// <summary>Saved under %LocalAppData%\SteamCmdHelper\settings.json. Never holds a password.</summary>
public sealed class AppSettings
{
    public string RepoPath { get; set; } = "";
    public string SteamCmdPath { get; set; } = "";
    public string Account { get; set; } = "";
    public bool AllowCreate { get; set; }
    /// <summary>"Dark" (the default), "Light" or "System" (follow Windows' app mode).</summary>
    public string Theme { get; set; } = Display.Dark;
    /// <summary>Text size in percent (Options > Appearance), on top of Windows' own display scaling.</summary>
    public int TextSize { get; set; } = 100;
    /// <summary>Height of the log on the Upload tab, in 96-DPI pixels; 0 = the default.</summary>
    public int LogHeight { get; set; }
    /// <summary>Look for a newer release on GitHub at start-up.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Opt-in: download a newer release in the background and install it when the window closes.</summary>
    public bool AutoUpdate { get; set; }
    /// <summary>A release version the user said not to be reminded about again.</summary>
    public string SkippedUpdate { get; set; } = "";
    /// <summary>Mods folders opened before, newest first (the drop-down next to Mods repo).</summary>
    public List<string> RecentRepos { get; set; } = new();
    /// <summary>After an upload, tag HEAD in git as "&lt;mod folder&gt;-v&lt;version&gt;".</summary>
    public bool TagUploads { get; set; }
    /// <summary>End each change note sent to Steam with "Uploaded via SteamCMD Helper" and a link to the newest release.</summary>
    public bool UploadCredit { get; set; } = true;
    /// <summary>Commit a newly created item's Workshop id file without asking.</summary>
    public bool CommitNewIds { get; set; }
    /// <summary>Saved profiles (mods folder, account, SteamCMD), by name.</summary>
    public List<Profile> Profiles { get; set; } = new();
    /// <summary>Discord webhook to announce uploads to, per mods folder. Kept here, not in the repo, because the link is a secret.</summary>
    public Dictionary<string, string> DiscordWebhooks { get; set; } = new();
    /// <summary>Mods folders whose uploads also get a GitHub release (made with the gh tool).</summary>
    public List<string> GitHubReleaseRepos { get; set; } = new();
    /// <summary>The mod list's "Hide up to date" box.</summary>
    public bool HideUpToDate { get; set; }

    public const int MaxRecent = 10;

    static string Key(string repo) => repo.Trim().TrimEnd('\\', '/');

    static bool Same(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The Discord webhook for <paramref name="repo"/>, or "" for none.</summary>
    public string WebhookFor(string repo) => DiscordWebhooks.FirstOrDefault(kv => Same(kv.Key, repo)).Value ?? "";

    public void SetWebhook(string repo, string url)
    {
        foreach (var k in DiscordWebhooks.Keys.Where(k => Same(k, repo)).ToList()) DiscordWebhooks.Remove(k);
        if (!string.IsNullOrWhiteSpace(url)) DiscordWebhooks[Key(repo)] = url.Trim();
    }

    public bool GitHubReleasesFor(string repo) => GitHubReleaseRepos.Any(r => Same(r, repo));

    public void SetGitHubReleases(string repo, bool on)
    {
        GitHubReleaseRepos.RemoveAll(r => Same(r, repo));
        if (on) GitHubReleaseRepos.Add(Key(repo));
    }

    /// <summary>Saves the current folder, account and SteamCMD under <paramref name="name"/> (replacing one with that name).</summary>
    public Profile SaveProfile(string name)
    {
        Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var p = new Profile { Name = name.Trim(), RepoPath = RepoPath, Account = Account, SteamCmdPath = SteamCmdPath };
        Profiles.Add(p);
        Profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return p;
    }

    /// <summary>Switches to a saved profile.</summary>
    public void UseProfile(Profile p)
    {
        Account = p.Account;
        SteamCmdPath = p.SteamCmdPath;
        if (p.RepoPath.Length > 0) Remember(p.RepoPath); else RepoPath = "";
    }

    /// <summary>The saved profile that matches the current folder and account, if any.</summary>
    public Profile? CurrentProfile() => Profiles.FirstOrDefault(p =>
        Same(p.RepoPath, RepoPath) &&
        string.Equals(p.Account, Account, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(p.SteamCmdPath, SteamCmdPath, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Where settings, history and the default SteamCMD live: %LocalAppData%\SteamCmdHelper, or in portable mode (a file
    /// named <see cref="PortableMarker"/> next to the exe) a "data" folder beside the exe, so the whole tool can live on
    /// a USB stick or in a synced folder.
    /// </summary>
    public static string Folder => Dir;

    public const string PortableMarker = "portable.txt";

    /// <summary>True when the program runs in portable mode.</summary>
    public static bool Portable => File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    static string Dir => Portable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamCmdHelper");

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
