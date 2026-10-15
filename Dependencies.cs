using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Finds the Workshop items a mod says it needs (links such as "steam://url/CommunityFilePage/123" or
/// ".../filedetails/?id=123" in its metadata, as in RimWorld's modDependencies) and checks that Steam still shows them
/// publicly. A dependency that is private or removed means subscribers cannot get it.
/// </summary>
public static class Dependencies
{
    static readonly Regex Link = new(@"(?:CommunityFilePage/|filedetails/\?(?:[^""<\s]*&(?:amp;)?)?id=)(\d{6,})", RegexOptions.IgnoreCase);

    /// <summary>The Workshop ids named in the mod's marker file (and Manifest, when the layout has one), without its own id.</summary>
    public static List<string> Find(ModInfo m, ProjectConfig cfg)
    {
        var ids = new List<string>();
        foreach (var rel in new[] { cfg.ModMarker, cfg.ManifestFile })
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var path = Path.Combine(m.Dir, ProjectConfig.Norm(rel));
            try { if (File.Exists(path)) ids.AddRange(FindIn(File.ReadAllText(path))); }
            catch (IOException) { }
        }
        return ids.Where(id => id != m.WorkshopId).Distinct().ToList();
    }

    public static IEnumerable<string> FindIn(string text) => Link.Matches(text).Select(x => x.Groups[1].Value);

    /// <summary>One warning per dependency Steam does not show publicly (ids Steam did not answer for are skipped).</summary>
    public static List<string> Problems(IEnumerable<string> ids, IReadOnlyDictionary<string, WorkshopItem> live)
    {
        var w = new List<string>();
        foreach (var id in ids)
        {
            if (!live.TryGetValue(id, out var item)) continue;
            if (!item.Public) w.Add($"needs Workshop item {id}, which is not public (private, friends only or removed)");
            else if (item.Banned) w.Add($"needs \"{item.Title}\" ({id}), which is banned on Steam");
        }
        return w;
    }
}
