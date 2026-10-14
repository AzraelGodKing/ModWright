using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdHelper;

/// <summary>
/// Describes how a mods repo is laid out, so the helper works for any Steam Workshop game.
/// Read from ".steamhelper.json" in the repo root. Without that file the layout is auto-detected
/// (RimWorld if the mods have About/About.xml, otherwise the generic layout).
/// All paths are relative to each mod's own folder and use forward slashes.
/// </summary>
public sealed class ProjectConfig
{
    public const string FileName = ".steamhelper.json";
    public static readonly string[] PresetNames = { "Generic", "RimWorld" };

    /// <summary>Steam app id of the game (RimWorld = 294100). Required before anything can be uploaded.</summary>
    public string AppId { get; set; } = "";

    /// <summary>Shown in the window; purely a label (filled in automatically from Steam when possible).</summary>
    public string GameName { get; set; } = "";

    /// <summary>Folder inside the repo that holds the mods. Empty = the repo root.</summary>
    public string ModsFolder { get; set; } = "";

    /// <summary>
    /// A subfolder counts as a mod when this file exists inside it (e.g. "About/About.xml", "mod.info", "addon.json").
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

    /// <summary>Same idea for a fallback Workshop description when creating a new item.</summary>
    public string DescriptionKey { get; set; } = "";

    /// <summary>Release notes file. Its text becomes the Workshop change note.</summary>
    public string Changelog { get; set; } = "CHANGELOG.md";

    /// <summary>When false, a missing changelog is fine and the change note is left empty.</summary>
    public bool ChangelogRequired { get; set; } = true;

    /// <summary>When true, line 1 of the changelog is the version number and the rest is the note. When false the whole file is the note.</summary>
    public bool ChangelogFirstLineIsVersion { get; set; } = false;

    /// <summary>File that stores the item's Workshop id (a single number).</summary>
    public string WorkshopIdFile { get; set; } = "workshop_id.txt";

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

    /// <summary>Names or *.ext patterns left out of the package in copy mode.</summary>
    public string[] Exclude { get; set; } = { ".git", ".vs", "obj", "bin", "*.pdb" };

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

    // ------------------------------------------------------------ presets

    public static ProjectConfig Preset(string name) => name switch
    {
        "RimWorld" => new ProjectConfig
        {
            AppId = "294100",
            GameName = "RimWorld",
            ModMarker = "About/About.xml",
            NameKey = "ModMetaData/name",
            VersionKey = "ModMetaData/modVersion",
            DescriptionKey = "ModMetaData/description",
            Changelog = "About/changelog.txt",
            ChangelogFirstLineIsVersion = true,
            WorkshopIdFile = "About/PublishedFileId.txt",
            Preview = "About/Preview.png",
            SteamDescriptionFile = "About/steam_description.txt",
            BuiltFiles = new[] { "Assemblies/*.dll" },
            Exclude = new[] { ".git", ".vs", "obj", "bin", "Source", "Tools", "*.md", "*.pdb" },
            Source = "RimWorld preset"
        },
        _ => new ProjectConfig { Source = "generic layout" }
    };

    /// <summary>Replaces the layout fields with a preset's. The app id is only replaced when the preset names a game.</summary>
    public void ApplyPreset(string name)
    {
        var p = Preset(name);
        if (p.AppId.Length > 0) { AppId = p.AppId; GameName = p.GameName; }
        ModsFolder = p.ModsFolder; ModMarker = p.ModMarker;
        NameKey = p.NameKey; VersionKey = p.VersionKey; DescriptionKey = p.DescriptionKey;
        Changelog = p.Changelog; ChangelogRequired = p.ChangelogRequired; ChangelogFirstLineIsVersion = p.ChangelogFirstLineIsVersion;
        WorkshopIdFile = p.WorkshopIdFile; Preview = p.Preview; SteamDescriptionFile = p.SteamDescriptionFile;
        BuiltFiles = p.BuiltFiles; UseGitArchive = p.UseGitArchive; Exclude = p.Exclude;
    }

    /// <summary>Which preset the current layout matches ("Custom" if neither).</summary>
    public string PresetName()
    {
        foreach (var n in PresetNames)
        {
            var p = Preset(n);
            if (p.ModMarker == ModMarker && p.Changelog == Changelog && p.WorkshopIdFile == WorkshopIdFile
                && p.NameKey == NameKey && p.VersionKey == VersionKey && p.ModsFolder == ModsFolder) return n;
        }
        return "Custom";
    }

    // ------------------------------------------------------------ load / save

    public static string PathFor(string repo) => Path.Combine(repo, FileName);

    public static ProjectConfig Load(string repo)
    {
        var path = PathFor(repo);
        if (!File.Exists(path)) return Detect(repo);
        try
        {
            var c = JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(path), Opts) ?? new ProjectConfig();
            c.Source = FileName + " in the repo";
            return c;
        }
        catch (Exception e)
        {
            var c = Detect(repo);
            c.Error = FileName + " could not be read (" + e.Message + "); using the auto-detected layout.";
            return c;
        }
    }

    /// <summary>No layout file: RimWorld if the folders look like RimWorld mods, otherwise the generic layout.</summary>
    static ProjectConfig Detect(string repo)
    {
        try
        {
            if (Directory.Exists(repo) && Directory.GetDirectories(repo).Any(d => File.Exists(Path.Combine(d, "About", "About.xml"))))
            {
                var rw = Preset("RimWorld");
                rw.Source = "auto-detected RimWorld layout";
                return rw;
            }
        }
        catch { }
        var g = Preset("Generic");
        g.Source = "generic layout (no " + FileName + ")";
        return g;
    }

    /// <summary>Writes the layout file, replacing any existing one.</summary>
    public string Save(string repo)
    {
        var path = PathFor(repo);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Opts));
        Source = FileName + " in the repo";
        return path;
    }

    /// <summary>Makes sure the layout file exists (writing the current layout if not) and returns its path, ready to edit.</summary>
    public string EnsureFile(string repo)
    {
        var path = PathFor(repo);
        if (!File.Exists(path)) Save(repo);
        return path;
    }

    public static string Norm(string rel) => rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
