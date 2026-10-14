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
    public string Theme { get; set; } = SteamCmdHelper.Theme.DarkName;

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
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are a convenience; ignore write failures */ }
    }
}
