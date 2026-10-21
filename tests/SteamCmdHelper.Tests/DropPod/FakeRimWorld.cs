using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>
/// A throwaway Steam install with RimWorld in a second library: libraryfolders.vdf, the app manifest, the game folder with
/// Version.txt, Data (base game), Mods, the Workshop folder and a player data folder.
/// </summary>
public sealed class FakeRimWorld : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "droppod_" + Guid.NewGuid().ToString("N"));
    public string Steam => Path.Combine(Root, "Steam");
    public string Library => Path.Combine(Root, "Library2");
    public string Game => Path.Combine(Library, "steamapps", "common", "RimWorld");
    public string Workshop => Path.Combine(Library, "steamapps", "workshop", "content", RimWorldModule.SteamAppId);
    public string Data => Path.Combine(Root, "UserData");

    public FakeRimWorld(string version = "1.5.4104 rev435")
    {
        Directory.CreateDirectory(Path.Combine(Steam, "steamapps"));
        File.WriteAllText(Path.Combine(Steam, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0" { "path" "{{Steam.Replace("\\", "\\\\")}}" "apps" { "228980" "1" } }
                "1" { "path" "{{Library.Replace("\\", "\\\\")}}" "apps" { "294100" "123" } }
            }
            """);
        Directory.CreateDirectory(Path.Combine(Library, "steamapps"));
        File.WriteAllText(Path.Combine(Library, "steamapps", "appmanifest_294100.acf"), "\"AppState\" { \"appid\" \"294100\" \"installdir\" \"RimWorld\" }");
        Directory.CreateDirectory(Game);
        File.WriteAllText(Path.Combine(Game, "Version.txt"), version + "\n");
        Directory.CreateDirectory(Path.Combine(Game, "Mods"));
        Directory.CreateDirectory(Workshop);
        Directory.CreateDirectory(Path.Combine(Data, "Config"));
        AddMod(Path.Combine(Game, "Data", "Core"), About("Core", "Ludeon.RimWorld", "1.5"));
    }

    public SteamLibraries Libraries => SteamLibraries.FromSteamFolder(Steam);

    public GameInstall Install() => new RimWorldModule().Detect(Libraries, null, Data)!;

    public string Local(string folder, string about) => AddMod(Path.Combine(Game, "Mods", folder), about);

    public string FromWorkshop(string id, string about) => AddMod(Path.Combine(Workshop, id), about);

    public void ModsConfig(params string[] active) => File.WriteAllText(Path.Combine(Data, "Config", "ModsConfig.xml"),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModsConfigData><version>1.5.4104 rev435</version><activeMods>"
        + string.Concat(active.Select(a => $"<li>{a}</li>")) + "</activeMods><knownExpansions><li>ludeon.rimworld.royalty</li></knownExpansions></ModsConfigData>");

    static string AddMod(string dir, string about)
    {
        Directory.CreateDirectory(Path.Combine(dir, "About"));
        File.WriteAllText(Path.Combine(dir, "About", "About.xml"), about);
        return dir;
    }

    public static string About(string name, string? packageId, params string[] versions) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModMetaData>"
        + $"<name>{name}</name><author>Someone</author>"
        + (packageId != null ? $"<packageId>{packageId}</packageId>" : "")
        + "<supportedVersions>" + string.Concat(versions.Select(v => $"<li>{v}</li>")) + "</supportedVersions>"
        + "</ModMetaData>";

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}
