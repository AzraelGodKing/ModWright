using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdHelper;

/// <summary>
/// Describes how a mods repo is laid out, so the helper works for any Steam Workshop game.
/// Read from ".steamhelper.json" in the repo root; if that file is missing the RimWorld defaults apply.
/// All paths are relative to each mod's own folder and use forward slashes.
/// </summary>
public sealed class ProjectConfig
{
    public const string FileName = ".steamhelper.json";

    /// <summary>Steam app id of the game (RimWorld = 294100).</summary>
    public string AppId { get; set; } = "294100";

    /// <summary>Shown in the window; purely a label.</summary>
    public string GameName { get; set; } = "RimWorld";

    /// <summary>A subfolder of the repo counts as a mod when this file exists inside it. XML files are also read for name and version.</summary>
    public string ModMarker { get; set; } = "About/About.xml";

    /// <summary>XPath (from the document root, e.g. "ModMetaData/name") for the display name in the marker file. Empty = use the folder name.</summary>
    public string NameXPath { get; set; } = "ModMetaData/name";

    /// <summary>XPath for the version in the marker file. Empty = take it from the first line of the changelog.</summary>
    public string VersionXPath { get; set; } = "ModMetaData/modVersion";

    /// <summary>XPath for a fallback Workshop description when creating a new item.</summary>
    public string DescriptionXPath { get; set; } = "ModMetaData/description";

    /// <summary>Release notes file. These become the Workshop change note.</summary>
    public string Changelog { get; set; } = "About/changelog.txt";

    /// <summary>When true, line 1 of the changelog is the version number and the rest is the note. When false the whole file is the note.</summary>
    public bool ChangelogFirstLineIsVersion { get; set; } = true;

    /// <summary>File that stores the item's Workshop id (a single number).</summary>
    public string WorkshopIdFile { get; set; } = "About/PublishedFileId.txt";

    /// <summary>Preview image, required when creating a new item.</summary>
    public string Preview { get; set; } = "About/Preview.png";

    /// <summary>Optional Steam BBCode description used when creating a new item.</summary>
    public string SteamDescriptionFile { get; set; } = "About/steam_description.txt";

    /// <summary>
    /// Build outputs that must exist and are added to the package even if git does not track them,
    /// as globs inside the mod folder. Use an empty list for mods with nothing to build.
    /// </summary>
    public string[] BuiltFiles { get; set; } = { "Assemblies/*.dll" };

    /// <summary>
    /// When true and the repo is a git repo, the package is "git archive HEAD", so .gitattributes export-ignore rules decide what ships.
    /// When false (or not a git repo) the mod folder is copied, minus <see cref="Exclude"/>.
    /// </summary>
    public bool UseGitArchive { get; set; } = true;

    /// <summary>Names or *.ext patterns left out of the package in copy mode.</summary>
    public string[] Exclude { get; set; } = { ".git", ".vs", "obj", "bin", "Source", "Tools", "*.md", "*.pdb" };

    [JsonIgnore] public string Source { get; private set; } = "built-in RimWorld defaults";
    [JsonIgnore] public string? Error { get; private set; }

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public static string PathFor(string repo) => Path.Combine(repo, FileName);

    public static ProjectConfig Load(string repo)
    {
        var path = PathFor(repo);
        if (!File.Exists(path)) return new ProjectConfig();
        try
        {
            var c = JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(path), Opts) ?? new ProjectConfig();
            c.Source = FileName + " in the repo";
            return c;
        }
        catch (Exception e)
        {
            return new ProjectConfig { Error = FileName + " could not be read (" + e.Message + "); using RimWorld defaults." };
        }
    }

    /// <summary>Writes the current (default) layout so the user can edit it. Never overwrites.</summary>
    public static string WriteDefault(string repo)
    {
        var path = PathFor(repo);
        if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(new ProjectConfig(), Opts));
        return path;
    }

    public static string Norm(string rel) => rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
