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
    public static List<string> Find(ModInfo m, ProjectConfig cfg) => FindInFolder(m.Dir, cfg, m.WorkshopId);

    /// <summary>The same for a mod folder or staged package at <paramref name="dir"/>, leaving out <paramref name="ownId"/>.</summary>
    public static List<string> FindInFolder(string dir, ProjectConfig cfg, string? ownId)
    {
        var ids = new List<string>();
        foreach (var rel in new[] { cfg.ModMarker, cfg.ManifestFile })
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var path = Path.Combine(dir, ProjectConfig.Norm(rel));
            try { if (File.Exists(path)) ids.AddRange(FindIn(File.ReadAllText(path))); }
            catch (IOException) { }
        }
        return ids.Where(id => id != ownId).Distinct().ToList();
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

    /// <summary>
    /// One line per dependency Steam updated after <paramref name="since"/> (the mod's own last upload), newest first,
    /// so the author can try the mod with it before sending: a Harmony or framework update can break a mod that
    /// worked when it was last uploaded.
    /// </summary>
    public static List<string> UpdatedSince(string modName, IEnumerable<string> ids, DateTimeOffset? since,
        IReadOnlyDictionary<string, WorkshopItem> live, DateTimeOffset now)
    {
        if (since is not { } last) return new();
        return ids
            .Select(id => live.TryGetValue(id, out var item) ? item : null)
            .Where(item => item is { Public: true, Updated: { } u } && u > last)
            .OrderByDescending(item => item!.Updated)
            .Select(item => $"{modName} needs \"{(item!.Title.Length > 0 ? item.Title : item.Id)}\", which Steam updated {Ago(now - item.Updated!.Value)} "
                + $"(after {modName}'s last upload). Worth a try in game with it before sending.")
            .ToList();
    }

    /// <summary>"3 days ago", "5 hours ago", "just now".</summary>
    public static string Ago(TimeSpan t) =>
        t.TotalDays >= 2 ? $"{(int)t.TotalDays} days ago"
        : t.TotalHours >= 2 ? $"{(int)t.TotalHours} hours ago"
        : t.TotalHours >= 1 ? "an hour ago"
        : t.TotalMinutes >= 2 ? $"{(int)t.TotalMinutes} minutes ago"
        : "just now";
}
