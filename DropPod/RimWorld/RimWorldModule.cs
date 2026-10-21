using System.Xml.Linq;

namespace SteamCmdHelper.DropPod.RimWorld;

/// <summary>RimWorld for the Drop Pod: where it lives, its mod folders, About.xml and ModsConfig.xml.</summary>
public sealed class RimWorldModule : IGameModule
{
    public const string SteamAppId = "294100";
    public string Name => "RimWorld";
    public string AppId => SteamAppId;
    public IReadOnlyList<string> ProcessNames { get; } = new[] { "RimWorldWin64", "RimWorldWin", "RimWorldLinux", "RimWorldMac" };

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

    public void WriteActiveMods(GameInstall install, IReadOnlyList<string> ids)
    {
        var file = ModsConfigPath(install.DataFolder);
        var config = File.Exists(file) ? ModsConfig.Parse(File.ReadAllText(file)) : new ModsConfig { Version = install.Version ?? "" };
        config.Active.Clear();
        config.Active.AddRange(ids.Select(ModManifest.KeyOf).Distinct());
        SafeFiles.WriteWithBackup(file, config.ToXml());
    }

    public GameLaunch? Launch(GameInstall install, bool ownDataFolder, string? logFile)
    {
        var names = OperatingSystem.IsWindows() ? new[] { "RimWorldWin64.exe", "RimWorldWin.exe" } : OperatingSystem.IsLinux() ? new[] { "RimWorldLinux" } : Array.Empty<string>();
        var exe = names.Select(n => SafeFiles.Child(install.GameFolder, n)).FirstOrDefault(f => f != null);
        if (exe == null) return null;
        var args = new List<string>();
        // RimWorld's own switch: config, ModsConfig.xml and saves all go to this folder.
        if (ownDataFolder) args.Add("-savedatafolder=" + install.DataFolder);
        // Unity's switch for the log file.
        if (logFile != null) { args.Add("-logFile"); args.Add(logFile); }
        return new GameLaunch(exe, args, install.GameFolder);
    }

    public string LogFile(GameInstall install) =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Ludeon Studios", "RimWorld by Ludeon Studios", "Player.log")
            : Path.Combine(install.DataFolder, "Player.log");

    // ModsConfig.xml carries the known expansions too; a new instance gets its own mod list written over it.
    public IReadOnlyList<string> SettingsFiles { get; } = new[] { Path.Combine("Config", "Prefs.xml"), Path.Combine("Config", "KeyPrefs.xml"), Path.Combine("Config", "ModsConfig.xml") };

    public IReadOnlyList<string> SaveFiles(GameInstall install) => RimWorldSaves.Files(install);

    public SaveInfo ReadSave(string path) => RimWorldSaves.Read(path);

    public string ActiveKey(string id) => RimWorldIds.WithoutSteamSuffix(ModManifest.KeyOf(id));

    /// <summary>The base game first, then the expansions in release order, then everything else.</summary>
    public int SortTier(ModManifest mod)
    {
        if (!RimWorldIds.IsOfficial(mod.Key)) return 100;
        var i = Array.IndexOf(Expansions, mod.Key);
        return mod.Key == RimWorldIds.Core ? 0 : i >= 0 ? i + 1 : Expansions.Length + 1;
    }

    static readonly string[] Expansions = { "ludeon.rimworld.royalty", "ludeon.rimworld.ideology", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey" };

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

    /// <summary>The file as RimWorld writes it.</summary>
    public string ToXml()
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("ModsConfigData",
            new XElement("version", Version),
            new XElement("activeMods", Active.Select(a => new XElement("li", a))),
            new XElement("knownExpansions", KnownExpansions.Select(a => new XElement("li", a)))));
        using var w = new Utf8StringWriter();
        doc.Save(w);
        return w.ToString();
    }

    sealed class Utf8StringWriter : StringWriter { public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false); }

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
