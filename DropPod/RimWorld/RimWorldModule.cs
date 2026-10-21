using System.Xml.Linq;

namespace SteamCmdHelper.DropPod.RimWorld;

/// <summary>RimWorld for the Drop Pod: where it lives, its mod folders, About.xml and ModsConfig.xml.</summary>
public sealed class RimWorldModule : IGameModule
{
    public const string SteamAppId = "294100";
    public string Name => "RimWorld";
    public string AppId => SteamAppId;

    /// <summary>The folder name under steamapps/common when the app manifest does not say.</summary>
    const string InstallDir = "RimWorld";

    public GameInstall? Detect(SteamLibraries steam, string? gameFolder, string? dataFolder)
    {
        var game = !string.IsNullOrWhiteSpace(gameFolder) ? gameFolder : steam.FindApp(AppId, InstallDir);
        if (game == null || !Directory.Exists(game)) return null;
        return new GameInstall(game, ReadVersion(game), steam.WorkshopFolder(AppId, game),
            !string.IsNullOrWhiteSpace(dataFolder) ? dataFolder : DefaultDataFolder());
    }

    public IReadOnlyList<ModFolder> ModFolders(GameInstall install)
    {
        var list = new List<ModFolder>();
        if (SafeFiles.Child(install.GameFolder, "Data", directory: true) is { } data) list.Add(new ModFolder(data, ModSource.Official));
        if (SafeFiles.Child(install.GameFolder, "Mods", directory: true) is { } mods) list.Add(new ModFolder(mods, ModSource.Local));
        if (install.WorkshopFolder != null) list.Add(new ModFolder(install.WorkshopFolder, ModSource.Workshop));
        return list;
    }

    public ModManifest? ReadMod(string folder, ModSource source) => RimWorldAbout.ReadFolder(folder, source);

    public IReadOnlyList<string>? ReadActiveMods(GameInstall install)
    {
        var file = ModsConfigPath(install.DataFolder);
        return File.Exists(file) ? ModsConfig.Parse(File.ReadAllText(file)).Active : null;
    }

    /// <summary>The game version from Version.txt in the install ("1.5.4104 rev435"), or null.</summary>
    public static string? ReadVersion(string gameFolder)
    {
        try
        {
            return SafeFiles.Child(gameFolder, "Version.txt") is { } f && File.ReadLines(f).FirstOrDefault()?.Trim() is { Length: > 0 } v ? v : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Where RimWorld keeps config and saves for this user (Windows: AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios).</summary>
    public static string DefaultDataFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows()) return Path.Combine(home, "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios");
        if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "RimWorld");
        return Path.Combine(home, ".config", "unity3d", "Ludeon Studios", "RimWorld by Ludeon Studios");
    }

    public static string ModsConfigPath(string dataFolder) => Path.Combine(dataFolder, "Config", "ModsConfig.xml");
}

/// <summary>RimWorld's Config/ModsConfig.xml: the game version that wrote it, the active mods in load order and the known expansions.</summary>
public sealed class ModsConfig
{
    public string Version { get; set; } = "";
    /// <summary>Active mod ids in load order, as written (the game writes them in lower case).</summary>
    public List<string> Active { get; } = new();
    public List<string> KnownExpansions { get; } = new();

    public static ModsConfig Parse(string xml)
    {
        var c = new ModsConfig();
        var root = SafeFiles.ParseXml(xml).Root;
        if (root == null) return c;
        XElement? El(string n) => root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(n, StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> Items(string n) => El(n)?.Elements().Select(e => e.Value.Trim()).Where(s => s.Length > 0) ?? Enumerable.Empty<string>();
        c.Version = El("version")?.Value.Trim() ?? "";
        c.Active.AddRange(Items("activeMods"));
        c.KnownExpansions.AddRange(Items("knownExpansions"));
        return c;
    }
}
