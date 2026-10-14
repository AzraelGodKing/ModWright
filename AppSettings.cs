using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>Saved under %LocalAppData%\SteamCmdHelper\settings.json. Never holds a password.</summary>
public sealed class AppSettings
{
    public string RepoPath { get; set; } = @"F:\Repositories\rimworld_mods";
    public string SteamCmdPath { get; set; } = "";
    public string Account { get; set; } = "";
    public bool AllowCreate { get; set; }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamCmdHelper", "settings.json");

    public string EffectiveSteamCmd =>
        string.IsNullOrWhiteSpace(SteamCmdPath)
            ? Path.Combine(RepoPath, "_tools", "steamcmd", "steamcmd.exe")
            : SteamCmdPath;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are a convenience; ignore write failures */ }
    }
}
