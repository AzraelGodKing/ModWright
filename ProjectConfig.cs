using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>
/// How a RimWorld mods repo is laid out: each subfolder of the repo with an About/About.xml is a mod. The layout is
/// built into the program; the repo's ".steamhelper.json" only records the mods folder's own "Leave these out" patterns,
/// so editing that file cannot change the layout.
/// All paths are relative to each mod's own folder and use forward slashes.
/// </summary>
public sealed class ProjectConfig
{
    public const string FileName = ".steamhelper.json";
    public const string RimWorldName = "RimWorld";
    public const string RimWorldAppId = "294100";

    /// <summary>Steam app id of the game.</summary>
    public string AppId => RimWorldAppId;

    /// <summary>Shown in the window.</summary>
    public string GameName => RimWorldName;

    /// <summary>Where the layout came from, shown in the window and the CLI.</summary>
    public string Source => "RimWorld layout";

    /// <summary>A subfolder counts as a mod when this file exists inside it.</summary>
    public string ModMarker => "About/About.xml";

    /// <summary>XPaths in <see cref="ModMarker"/> of the display name, the version and the fallback Workshop description.</summary>
    public string NameKey => "ModMetaData/name";
    public string VersionKey => "ModMetaData/modVersion";
    public string DescriptionKey => "ModMetaData/description";

    /// <summary>
    /// Fluffy's Mod Manager style manifest. When it has a &lt;version&gt;, that wins over <see cref="VersionKey"/>, the
    /// same order the Add Changenote mod uses.
    /// </summary>
    public string ManifestFile => "About/Manifest.xml";

    /// <summary>
    /// Release notes, Add Changenote style: an entry per version, each starting with its version number, and lines
    /// starting with # are ignored. The change note is the entry for the mod's current version, and an upload is refused
    /// until that entry exists.
    /// </summary>
    public string Changelog => "About/changelog.txt";

    /// <summary>File that stores the item's Workshop id (a single number).</summary>
    public string WorkshopIdFile => "About/PublishedFileId.txt";

    /// <summary>Preview image, required by Steam when creating a new item.</summary>
    public string Preview => "About/Preview.png";

    /// <summary>Optional Steam BBCode description used when creating a new item.</summary>
    public string SteamDescriptionFile => "About/steam_description.txt";

    /// <summary>
    /// The tags RimWorld's own uploader sets: "Mod", plus one per game version listed at <see cref="VersionTagsKey"/>
    /// (an XPath in <see cref="ModMarker"/>), which the Workshop filters by. SteamCMD cannot set tags, so they are set
    /// through the Steam client after an upload (<see cref="WorkshopTags"/>).
    /// </summary>
    public string[] Tags => new[] { "Mod" };
    public string VersionTagsKey => "ModMetaData/supportedVersions/li";

    /// <summary>Where the game loads local mods from, inside its install folder. Used by "Try in a game beta".</summary>
    public string GameModsFolder => "Mods";

    /// <summary>
    /// The folder in each mod whose images, and YouTube links in its videos.txt, become the item's extra preview images
    /// and videos through the Steam client (<see cref="WorkshopExtras"/>). It is never packaged.
    /// </summary>
    public string GalleryFolder => "Gallery";

    /// <summary>
    /// Build outputs that must exist and are added to the package even if git does not track them, as globs inside the
    /// mod folder: Assemblies or a version folder's Assemblies.
    /// </summary>
    public string[] BuiltFiles { get; set; } = { "Assemblies/*.dll", "*/Assemblies/*.dll" };

    /// <summary>
    /// The files a package must contain. An upload is refused when packaging left one out, for example through a
    /// .gitattributes export-ignore rule, since the mod would then break for everyone who updates.
    /// </summary>
    public IEnumerable<string> RequiredInPackage() => new[] { ModMarker };

    /// <summary>
    /// Names or *.ext patterns left out of the package when the mods folder is not a git repo and is copied. In a git
    /// repo the package is "git archive HEAD", so .gitattributes export-ignore rules decide what ships.
    /// </summary>
    public string[] Exclude { get; set; } = { ".git", ".vs", "obj", "bin", "Source", "Tools", "*.md", "*.pdb" };

    /// <summary>
    /// Names or *.ext patterns this mods folder leaves out of every package, on top of the layout's own: set from the
    /// package view's "Leave these out". Unlike Exclude it applies to git packaging too. Saved in the layout file.
    /// </summary>
    public string[]? LeaveOut { get; set; }

    /// <summary>Why the layout file was not used, or null.</summary>
    public string? Error { get; set; }

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>What the layout file holds. Preset and AppId are only read to spot a file written for another game.</summary>
    sealed class LayoutFile
    {
        public string Preset { get; set; } = "";
        public string AppId { get; set; } = "";
        public string[]? LeaveOut { get; set; }
    }

    // ------------------------------------------------------------ the RimWorld layout

    public static ProjectConfig RimWorld() => new();

    // ------------------------------------------------------------ load / save

    public static string PathFor(string repo) => Path.Combine(repo, FileName);

    /// <summary>
    /// The RimWorld layout, with the mods folder's own "Leave these out" patterns from its layout file. A file written for
    /// another game (Generic, Custom, Paradox... from before 2.0.0) is not used: <see cref="Error"/> says so.
    /// </summary>
    public static ProjectConfig Load(string repo)
    {
        var c = RimWorld();
        var path = PathFor(repo);
        if (!File.Exists(path)) return c;
        try
        {
            var file = JsonSerializer.Deserialize<LayoutFile>(File.ReadAllText(path), Opts) ?? new LayoutFile();
            c.LeaveOut = file.LeaveOut;
            var other = file.Preset.Length > 0 ? !file.Preset.Equals(RimWorldName, StringComparison.OrdinalIgnoreCase)
                : file.AppId.Length > 0 && file.AppId != RimWorldAppId;
            if (other)
                c.Error = $"{FileName} is set up for {(file.Preset.Length > 0 ? "the " + file.Preset + " layout" : "app " + file.AppId)}, but Modwright is for RimWorld mods only, so the RimWorld layout is used.";
        }
        catch (Exception e)
        {
            c.Error = FileName + " could not be read (" + e.Message + "); using the RimWorld layout.";
        }
        return c;
    }

    /// <summary>Writes the layout file, replacing any existing one: the layout's name and the folder's "Leave these out" patterns.</summary>
    public string Save(string repo)
    {
        var path = PathFor(repo);
        var lean = new Dictionary<string, object> { ["Preset"] = RimWorldName };
        if (LeaveOut is { Length: > 0 } leave) lean["LeaveOut"] = leave;
        File.WriteAllText(path, JsonSerializer.Serialize(lean, Opts));
        return path;
    }

    public static string Norm(string rel) => rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
