using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdHelper;

/// <summary>
/// Describes how a mods repo is laid out, so the helper works for any Steam Workshop game.
///
/// Built-in layouts (<see cref="PresetNames"/>) live in this program and are locked: the repo's
/// ".steamhelper.json" only records WHICH one is used (plus the app id for Generic), so editing that file
/// cannot change them. A "Custom" layout is the one kind stored in full in the file and editable.
/// Without a file the layout is auto-detected (RimWorld if the mods have About/About.xml, else Generic).
/// All paths are relative to each mod's own folder and use forward slashes.
/// </summary>
public sealed class ProjectConfig
{
    public const string FileName = ".steamhelper.json";
    public const string CustomName = "Custom";
    public static readonly string[] PresetNames = { "Generic", "RimWorld", "Golf With Your Friends" };

    /// <summary>Presets that have not been proven against the real game. Real uploads ask for confirmation first.</summary>
    public static readonly string[] ExperimentalPresets = { "Golf With Your Friends" };

    /// <summary>Which layout is in use: a built-in name, or "Custom". Empty in old files = worked out from the fields.</summary>
    public string Preset { get; set; } = "";

    /// <summary>Steam app id of the game (RimWorld = 294100). Required before anything can be uploaded.</summary>
    public string AppId { get; set; } = "";

    /// <summary>Shown in the window; purely a label (filled in automatically from Steam when possible).</summary>
    public string GameName { get; set; } = "";

    // ---- everything below is only read from the file for Custom layouts; built-ins come from this program ----

    /// <summary>Folder inside the repo that holds the mods. Empty = the repo root.</summary>
    public string ModsFolder { get; set; } = "";

    /// <summary>
    /// A subfolder counts as a mod when this file exists inside it (e.g. "About/About.xml", "mod.info", "Map").
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

    /// <summary>True for the layouts built into the program. These cannot be edited.</summary>
    [JsonIgnore] public bool IsBuiltIn => PresetNames.Contains(PresetName());

    /// <summary>True when the layout itself names the game, so the app id is fixed too.</summary>
    [JsonIgnore] public bool AppIdLocked => IsBuiltIn && GetPreset(PresetName()).AppId.Length > 0;

    [JsonIgnore] public bool Experimental => ExperimentalPresets.Contains(PresetName());

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    // ------------------------------------------------------------ built-in layouts

    public static ProjectConfig GetPreset(string name) => name switch
    {
        "RimWorld" => new ProjectConfig
        {
            Preset = "RimWorld",
            AppId = "294100",
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
            BuiltFiles = new[] { "Assemblies/*.dll" },
            Exclude = new[] { ".git", ".vs", "obj", "bin", "Source", "Tools", "*.md", "*.pdb" },
            Source = "built-in RimWorld layout"
        },
        // Modelled on a downloaded Workshop item: a folder holding "Map" (JSON course file), Preview.jpg and optional Additional0.jpg...
        // Unverified: nobody has confirmed the game accepts a hand-uploaded item, and the game's own Map file carries a publishedID.
        "Golf With Your Friends" => new ProjectConfig
        {
            Preset = "Golf With Your Friends",
            AppId = "431240",
            GameName = "Golf With Your Friends",
            ModMarker = "Map",
            NameKey = "\"levelName\"\\s*:\\s*\"([^\"]*)\"",
            DescriptionKey = "\"description\"\\s*:\\s*\"([^\"]*)\"",
            ChangelogRequired = false,
            Preview = "Preview.jpg",
            Source = "built-in Golf With Your Friends layout (experimental)"
        },
        _ => new ProjectConfig { Preset = "Generic", Source = "built-in Generic layout" }
    };

    /// <summary>Switches to a built-in layout (replacing every layout field). The app id is only replaced when the layout names a game.</summary>
    public void ApplyPreset(string name)
    {
        var p = GetPreset(name);
        Preset = p.Preset;
        if (p.AppId.Length > 0) { AppId = p.AppId; GameName = p.GameName; }
        ModsFolder = p.ModsFolder; ModMarker = p.ModMarker;
        NameKey = p.NameKey; VersionKey = p.VersionKey; ManifestFile = p.ManifestFile; DescriptionKey = p.DescriptionKey;
        Changelog = p.Changelog; ChangelogRequired = p.ChangelogRequired;
        ChangelogFirstLineIsVersion = p.ChangelogFirstLineIsVersion; ChangelogByVersion = p.ChangelogByVersion;
        WorkshopIdFile = p.WorkshopIdFile; Preview = p.Preview; SteamDescriptionFile = p.SteamDescriptionFile;
        BuiltFiles = p.BuiltFiles; UseGitArchive = p.UseGitArchive; Exclude = p.Exclude;
        Source = p.Source;
    }

    /// <summary>Turns the current layout into an editable Custom one (keeping its fields as a starting point).</summary>
    public void MakeCustom() { Preset = CustomName; Source = FileName + " (custom layout)"; }

    /// <summary>The layout in use: a built-in name or "Custom". Old files without a Preset field are matched against the built-ins.</summary>
    public string PresetName()
    {
        if (Preset == CustomName || PresetNames.Contains(Preset)) return Preset;
        foreach (var n in PresetNames)
        {
            var p = GetPreset(n);
            if (p.ModMarker == ModMarker && p.Changelog == Changelog && p.WorkshopIdFile == WorkshopIdFile
                && p.NameKey == NameKey && p.VersionKey == VersionKey && p.ModsFolder == ModsFolder
                && p.ChangelogRequired == ChangelogRequired && (p.AppId.Length == 0 || p.AppId == AppId)) return n;
        }
        return CustomName;
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
            var name = c.PresetName();
            if (PresetNames.Contains(name))
            {
                // Built-in layouts are locked: whatever else the file says about the layout is ignored.
                var appId = c.AppId; var game = c.GameName;
                c.ApplyPreset(name);
                if (GetPreset(name).AppId.Length == 0) { c.AppId = appId; c.GameName = game; }
            }
            else
            {
                c.Preset = CustomName;
                c.Source = FileName + " (custom layout)";
            }
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
                var rw = GetPreset("RimWorld");
                rw.Source = "auto-detected built-in RimWorld layout";
                return rw;
            }
        }
        catch { }
        var g = GetPreset("Generic");
        g.Source = "built-in Generic layout (no " + FileName + ")";
        return g;
    }

    /// <summary>
    /// Writes the layout file, replacing any existing one. Built-in layouts save only their name (and the app id when the
    /// layout does not fix it); a Custom layout saves every field.
    /// </summary>
    public string Save(string repo)
    {
        var path = PathFor(repo);
        var name = PresetName();
        if (PresetNames.Contains(name))
        {
            var lean = new Dictionary<string, string> { ["Preset"] = name };
            if (GetPreset(name).AppId.Length == 0) { lean["AppId"] = AppId; lean["GameName"] = GameName; }
            File.WriteAllText(path, JsonSerializer.Serialize(lean, Opts));
        }
        else
        {
            Preset = CustomName;
            File.WriteAllText(path, JsonSerializer.Serialize(this, Opts));
        }
        return path;
    }

    /// <summary>Makes sure a Custom layout file exists (writing the current fields as a starting point) and returns its path.</summary>
    public string EnsureFile(string repo)
    {
        if (IsBuiltIn) throw new InvalidOperationException("Built-in layouts cannot be edited. Choose Custom first.");
        var path = PathFor(repo);
        if (!File.Exists(path)) Save(repo);
        return path;
    }

    public static string Norm(string rel) => rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
