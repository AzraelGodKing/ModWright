namespace SteamCmdHelper.DropPod;

/// <summary>Where a mod came from: shipped with the game (base game and DLC), the game's own Mods folder, or Steam Workshop.</summary>
public enum ModSource { Official, Local, Workshop }

/// <summary>A mod another mod needs, as its manifest names it.</summary>
/// <param name="Id">The needed mod's id (RimWorld: packageId), as written.</param>
/// <param name="WorkshopId">The Workshop item to get it from, when the manifest links one.</param>
public sealed record ModDependency(string Id, string? DisplayName, string? WorkshopId, string? Url)
{
    public string Key => ModManifest.KeyOf(Id);
}

/// <summary>
/// A list in a mod's manifest that can differ per game version (RimWorld's "loadAfter" and "loadAfterByVersion"). For a
/// given version, the version's own list is used when there is one, and the general list otherwise.
/// </summary>
public sealed class VersionedList<T>
{
    public List<T> All { get; } = new();
    /// <summary>Lists for one game version, keyed by "major.minor" ("1.5").</summary>
    public Dictionary<string, List<T>> ByVersion { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<T> For(string? gameVersion) =>
        gameVersion != null && ByVersion.TryGetValue(GameVersion.Short(gameVersion), out var v) ? v : All;

    /// <summary>Every entry, from the general list and all version lists.</summary>
    public IEnumerable<T> Everything => All.Concat(ByVersion.Values.SelectMany(l => l));
}

/// <summary>
/// One installed mod, read from its manifest (RimWorld: About/About.xml). Never null-heavy: missing fields are empty, and
/// anything that could not be read is listed in <see cref="Issues"/> instead of failing the whole scan.
/// </summary>
public sealed class ModManifest
{
    /// <summary>Full path of the mod's folder.</summary>
    public string Folder { get; init; } = "";
    public string FolderName => Path.GetFileName(Folder.TrimEnd('/', '\\'));
    public ModSource Source { get; init; }

    /// <summary>The mod's id as written (RimWorld: packageId), or "" when it has none.</summary>
    public string Id { get; set; } = "";
    /// <summary>The id used for matching: lower case (RimWorld ignores case), or "folder:&lt;name&gt;" when there is no id.</summary>
    public string Key => Id.Length > 0 ? KeyOf(Id) : "folder:" + FolderName.ToLowerInvariant();

    public string Name { get; set; } = "";
    public string DisplayName => Name.Length > 0 ? Name : FolderName;
    public List<string> Authors { get; } = new();
    public string Description { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Game versions the mod says it supports, as written ("1.4", "1.5").</summary>
    public List<string> SupportedVersions { get; } = new();
    public string? WorkshopId { get; set; }
    /// <summary>Full path of the preview image, or null when there is none.</summary>
    public string? PreviewPath { get; set; }

    public VersionedList<ModDependency> Dependencies { get; } = new();
    public VersionedList<string> LoadAfter { get; } = new();
    public VersionedList<string> LoadBefore { get; } = new();
    /// <summary>Like <see cref="LoadAfter"/>, but the game treats breaking it as an error rather than a suggestion.</summary>
    public VersionedList<string> ForceLoadAfter { get; } = new();
    public VersionedList<string> ForceLoadBefore { get; } = new();
    public VersionedList<string> Incompatible { get; } = new();

    /// <summary>What was wrong with the manifest (malformed XML, no id, ...), in words for the player.</summary>
    public List<string> Issues { get; } = new();

    /// <summary>The form of an id used to compare two ids.</summary>
    public static string KeyOf(string id) => id.Trim().ToLowerInvariant();

    public bool Supports(string? gameVersion) =>
        gameVersion == null || SupportedVersions.Count == 0 || SupportedVersions.Any(v => GameVersion.Short(v) == GameVersion.Short(gameVersion));

    public override string ToString() => $"{DisplayName} ({(Id.Length > 0 ? Id : FolderName)})";
}

/// <summary>Game version strings.</summary>
public static class GameVersion
{
    /// <summary>"major.minor" from a full version ("1.5.4104 rev435" and "v1.5" give "1.5"); the trimmed text when it has no such form.</summary>
    public static string Short(string version)
    {
        var s = version.Trim().TrimStart('v', 'V');
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+)\.(\d+)");
        return m.Success ? $"{int.Parse(m.Groups[1].Value)}.{int.Parse(m.Groups[2].Value)}" : s;
    }
}
