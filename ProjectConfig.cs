using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdHelper;

/// <summary>
/// Describes how a mods repo is laid out. Modwright is for RimWorld, so there is one layout, built into the program
/// (<see cref="RimWorld"/>): the repo's ".steamhelper.json" only records its name and the mods folder's own
/// "Leave these out" patterns, so editing that file cannot change the layout.
/// All paths are relative to each mod's own folder and use forward slashes.
/// </summary>
public sealed class ProjectConfig
{
    public const string FileName = ".steamhelper.json";
    public const string RimWorldName = "RimWorld";
    public const string RimWorldAppId = "294100";

    /// <summary>The layout's name, as the layout file records it.</summary>
    public string Preset { get; set; } = "";

    /// <summary>Steam app id of the game (RimWorld = 294100). Required before anything can be uploaded.</summary>
    public string AppId { get; set; } = "";

    /// <summary>Shown in the window; purely a label (filled in automatically from Steam when possible).</summary>
    public string GameName { get; set; } = "";

    // ---- the layout itself: always the built-in RimWorld one, never read from the file ----

    /// <summary>Folder inside the repo that holds the mods. Empty = the repo root.</summary>
    public string ModsFolder { get; set; } = "";

    /// <summary>
    /// A subfolder counts as a mod when this file exists inside it (e.g. "About/About.xml").
    /// Empty = every subfolder counts as a mod.
    /// </summary>
    public string ModMarker { get; set; } = "";

    /// <summary>
    /// Where to read the display name from the marker file. Empty = use the folder name.
    /// The marker's extension decides how this is read: .xml = XPath ("ModMetaData/name"),
    /// .json = dotted path ("info.title"), anything else = a key name ("name" matches name=Foo, name = "Foo", name: Foo)
    /// or, if it contains "(", a regex whose first group is the value.
    /// </summary>
    public string NameKey { get; set; } = "";

    /// <summary>Same idea for the version. Empty = take the version from the first line of the changelog (if that option is on).</summary>
    public string VersionKey { get; set; } = "";

    /// <summary>
    /// Optional Fluffy's Mod Manager style manifest (e.g. "About/Manifest.xml"). When it has a &lt;version&gt;, that wins
    /// over <see cref="VersionKey"/>, the same order the Add Changenote mod uses.
    /// </summary>
    public string ManifestFile { get; set; } = "";

    /// <summary>Same idea for a fallback Workshop description when creating a new item.</summary>
    public string DescriptionKey { get; set; } = "";

    /// <summary>Release notes file. Its text becomes the Workshop change note.</summary>
    public string Changelog { get; set; } = "CHANGELOG.md";

    /// <summary>When false, a missing changelog is fine and the change note is left empty.</summary>
    public bool ChangelogRequired { get; set; } = true;

    /// <summary>When true, line 1 of the changelog is the version number and the rest is the note. When false the whole file is the note.</summary>
    public bool ChangelogFirstLineIsVersion { get; set; } = false;

    /// <summary>
    /// Add Changenote style (RimWorld): the changelog holds an entry per version, each starting with its version number,
    /// and lines starting with # are ignored. The change note is the entry for the mod's current version, and an upload
    /// is refused until that entry exists.
    /// </summary>
    public bool ChangelogByVersion { get; set; } = false;

    /// <summary>File that stores the item's Workshop id (a single number, unless <see cref="WorkshopIdKey"/> is set).</summary>
    public string WorkshopIdFile { get; set; } = "workshop_id.txt";

    /// <summary>
    /// When set, the id is one value inside <see cref="WorkshopIdFile"/> (read like <see cref="NameKey"/>) and a new item's id is written back to that key.
    /// </summary>
    public string WorkshopIdKey { get; set; } = "";

    /// <summary>Preview image, required by Steam when creating a new item. Empty = none.</summary>
    public string Preview { get; set; } = "preview.png";

    /// <summary>Optional Steam BBCode description used when creating a new item.</summary>
    public string SteamDescriptionFile { get; set; } = "steam_description.txt";

    /// <summary>
    /// Build outputs that must exist and are added to the package even if git does not track them,
    /// as globs inside the mod folder (e.g. "Assemblies/*.dll"). Empty list for mods with nothing to build.
    /// </summary>
    public string[] BuiltFiles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true and the repo is a git repo, the package is "git archive HEAD", so .gitattributes export-ignore rules decide what ships.
    /// When false (or not a git repo) the mod folder is copied, minus <see cref="Exclude"/>.
    /// </summary>
    public bool UseGitArchive { get; set; } = true;

    /// <summary>
    /// Files every package must contain, as paths or globs inside the mod folder (e.g. "Assemblies/*.dll"). The marker
    /// file always counts too. An upload is refused when packaging left one out, for example through a .gitattributes
    /// export-ignore rule, since the mod would then break for everyone who updates.
    /// </summary>
    public string[] RequiredFiles { get; set; } = Array.Empty<string>();

    /// <summary>The files a package must contain: the marker file (when the layout has one) and <see cref="RequiredFiles"/>.</summary>
    public IEnumerable<string> RequiredInPackage() =>
        (string.IsNullOrWhiteSpace(ModMarker) ? Array.Empty<string>() : new[] { ModMarker }).Concat(RequiredFiles ?? Array.Empty<string>());

    /// <summary>
    /// Workshop tags every mod gets (RimWorld: "Mod"). SteamCMD cannot set tags, so they are set through the Steam client
    /// after an upload (<see cref="WorkshopTags"/>). Empty, with no <see cref="VersionTagsKey"/>, leaves tags alone.
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Where the marker file lists the game versions a mod supports, one Workshop tag each as "major.minor": an XPath to
    /// the elements in .xml (RimWorld: "ModMetaData/supportedVersions/li"), a dotted path to an array in .json. Version
    /// tags the mod no longer lists are taken off. Empty = no version tags.
    /// </summary>
    public string VersionTagsKey { get; set; } = "";

    /// <summary>
    /// Where the game loads local mods from, inside its install folder (RimWorld: "Mods"). Used by "Try in a game beta"
    /// to put a mod and the Workshop items it needs into a test install. Empty = the game is installed without mods.
    /// </summary>
    public string GameModsFolder { get; set; } = "";

    /// <summary>
    /// A folder in each mod (one name, such as "Gallery") whose images, and YouTube links in its videos.txt, become the
    /// item's extra preview images and videos through the Steam client (<see cref="WorkshopExtras"/>). It is never
    /// packaged. Empty = no gallery.
    /// </summary>
    public string GalleryFolder { get; set; } = "";

    /// <summary>Names or *.ext patterns left out of the package in copy mode.</summary>
    public string[] Exclude { get; set; } = { ".git", ".vs", "obj", "bin", "*.pdb" };

    /// <summary>
    /// Names or *.ext patterns this mods folder leaves out of every package, on top of the layout's own: set from the
    /// package view's "Leave these out". Unlike Exclude it applies to git packaging too, and built-in layouts keep it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? LeaveOut { get; set; }

    // Layout files written by version 1.1.0 used these names; they are still read, never written.
    [JsonPropertyName("NameXPath"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyName { get => null; set { if (!string.IsNullOrEmpty(value)) NameKey = value; } }
    [JsonPropertyName("VersionXPath"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyVersion { get => null; set { if (!string.IsNullOrEmpty(value)) VersionKey = value; } }
    [JsonPropertyName("DescriptionXPath"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyDescription { get => null; set { if (!string.IsNullOrEmpty(value)) DescriptionKey = value; } }

    [JsonIgnore] public string Source { get; set; } = "generic layout";
    [JsonIgnore] public string? Error { get; set; }
    [JsonIgnore] public bool AppIdValid => !string.IsNullOrWhiteSpace(AppId) && AppId.All(char.IsDigit);

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    // ------------------------------------------------------------ the RimWorld layout

    public static ProjectConfig RimWorld() => new()
    {
        Preset = RimWorldName,
        AppId = RimWorldAppId,
        GameName = "RimWorld",
        ModMarker = "About/About.xml",
        NameKey = "ModMetaData/name",
        VersionKey = "ModMetaData/modVersion",
        ManifestFile = "About/Manifest.xml",
        DescriptionKey = "ModMetaData/description",
        Changelog = "About/changelog.txt",
        ChangelogByVersion = true,
        WorkshopIdFile = "About/PublishedFileId.txt",
        Preview = "About/Preview.png",
        SteamDescriptionFile = "About/steam_description.txt",
        BuiltFiles = new[] { "Assemblies/*.dll", "*/Assemblies/*.dll" },
        Exclude = new[] { ".git", ".vs", "obj", "bin", "Source", "Tools", "*.md", "*.pdb" },
        // The tags RimWorld's own uploader sets: "Mod" and each supported version, which the Workshop filters by.
        Tags = new[] { "Mod" },
        VersionTagsKey = "ModMetaData/supportedVersions/li",
        GameModsFolder = "Mods",
        GalleryFolder = "Gallery",
        Source = "RimWorld layout"
    };

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
            var file = JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(path), Opts) ?? new ProjectConfig();
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
