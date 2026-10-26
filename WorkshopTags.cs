using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace SteamCmdHelper;

/// <summary>What happened when an item's Workshop tags were set.</summary>
/// <param name="Changed">Steam got new tags; false when they were already right or the update failed.</param>
/// <param name="Tags">The item's tags afterwards (or the ones that were wanted, when it failed).</param>
public sealed record TagResult(bool Ok, bool Changed, string[] Tags, string Message);

/// <summary>
/// Workshop tags, such as RimWorld's "Mod" and "1.6" that players filter by. SteamCMD's build file cannot carry tags,
/// so they are set through the Steam client instead (<see cref="UpdateOnSteamAsync"/>), the way the game's own uploader
/// does it. The layout decides which tags a mod gets: <see cref="ProjectConfig.Tags"/> always, plus one per game
/// version listed at <see cref="ProjectConfig.VersionTagsKey"/> in the marker file (RimWorld: About.xml's
/// supportedVersions).
/// </summary>
public static class WorkshopTags
{
    /// <summary>The argument that makes the program set one item's tags and exit (run in a child process).</summary>
    public const string ChildArg = "--steam-set-tags";

    static readonly Regex VersionTag = new(@"^\d+\.\d+$");

    /// <summary>True for tags that name a game version ("1.6"); those are replaced, every other tag is kept.</summary>
    public static bool IsVersionTag(string tag) => VersionTag.IsMatch(tag.Trim());

    /// <summary>
    /// The tags the mod in <paramref name="dir"/> (a mod folder or a staged package) should have on Steam: the layout's
    /// fixed tags, then one per supported game version as "major.minor".
    /// </summary>
    public static string[] For(string dir, ProjectConfig cfg)
    {
        var tags = new List<string>();
        void Add(string t)
        {
            t = t.Trim();
            if (t.Length > 0 && !tags.Contains(t, StringComparer.OrdinalIgnoreCase)) tags.Add(t);
        }
        foreach (var t in cfg.Tags) Add(t);
        foreach (var v in SupportedVersions(Path.Combine(dir, ProjectConfig.Norm(cfg.ModMarker)), cfg.VersionTagsKey)) Add(v);
        return tags.ToArray();
    }

    /// <summary>
    /// The game versions listed at <paramref name="key"/> in <paramref name="file"/>, each cut to "major.minor"
    /// ("1.6.4104" becomes "1.6") and listed once. <paramref name="key"/> is an XPath to the elements.
    /// </summary>
    public static List<string> SupportedVersions(string file, string key)
    {
        var found = new List<string>();
        if (!File.Exists(file)) return found;
        IEnumerable<string> raw;
        try { raw = XDocument.Load(file).XPathSelectElements("/" + key.TrimStart('/')).Select(e => e.Value).ToList(); }
        catch { return found; }
        foreach (var r in raw)
        {
            var m = Regex.Match(r.Trim(), @"^v?(\d+)\.(\d+)");
            if (!m.Success) continue;
            var v = $"{int.Parse(m.Groups[1].Value)}.{int.Parse(m.Groups[2].Value)}";
            if (!found.Contains(v)) found.Add(v);
        }
        return found;
    }

    /// <summary>
    /// The tags an item should end up with: its current tags without the version tags that are no longer wanted, plus
    /// every wanted tag. Tags the layout does not manage (a "Scenario" tag, say) are kept. Wanted tags come first, in
    /// their own spelling (Steam's client hands current tags back in lower case).
    /// </summary>
    public static string[] Merge(IEnumerable<string> current, IEnumerable<string> wanted)
    {
        var result = new List<string>();
        foreach (var t in wanted.Select(t => t.Trim()).Where(t => t.Length > 0))
            if (!result.Contains(t, StringComparer.OrdinalIgnoreCase)) result.Add(t);
        foreach (var t in current.Select(t => t.Trim()).Where(t => t.Length > 0))
            if (!IsVersionTag(t) && !result.Contains(t, StringComparer.OrdinalIgnoreCase)) result.Add(t);
        return result.ToArray();
    }

    /// <summary>True when both lists hold the same tags, ignoring order and case.</summary>
    public static bool Same(IEnumerable<string> a, IEnumerable<string> b) =>
        new HashSet<string>(a.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase).SetEquals(b.Select(t => t.Trim()));

    /// <summary>"Mod, 1.5, 1.6", or "(none)".</summary>
    public static string Describe(IEnumerable<string> tags) => tags.Any() ? string.Join(", ", tags) : "(none)";

    // ------------------------------------------------------------ setting them on Steam

    /// <summary>
    /// Sets item <paramref name="workshopId"/>'s tags through the Steam client (SteamworksTags.cs). It runs
    /// in a child copy of this program, because the Steam client is told which game is asking once per process, and a
    /// crash in Valve's library must not take the window down. Needs Steam running and signed in as the item's owner.
    /// </summary>
    public static async Task<TagResult> UpdateOnSteamAsync(string appId, string workshopId, string[] tags, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return new TagResult(false, false, tags, "Workshop tags can only be set on Windows.");
        var (output, error) = await SteamClientChild.RunAsync(new[] { ChildArg, appId, workshopId }.Concat(tags), TimeSpan.FromMinutes(2), ct);
        return error != null ? new TagResult(false, false, tags, error) : ParseChild(output!, tags);
    }

    // The child writes one line: "OK <tags>" (set), "SAME <tags>" (already right) or "ERROR <why>". Tags are
    // separated by tabs, since a tag can hold a comma or a space.
    const string Sep = "\t";

    public static string ChildLine(TagResult r) =>
        !r.Ok ? "ERROR " + r.Message.Replace('\n', ' ')
        : (r.Changed ? "OK " : "SAME ") + string.Join(Sep, r.Tags);

    public static TagResult ParseChild(string output, string[] wanted)
    {
        var line = output.Replace("\r", "").Split('\n').LastOrDefault(l => l.StartsWith("OK ") || l.StartsWith("SAME ") || l.StartsWith("ERROR ") || l is "OK" or "SAME");
        if (line == null) return new TagResult(false, false, wanted, "The tag update stopped without saying why" + (output.Trim().Length > 0 ? ": " + output.Trim() : "."));
        if (line.StartsWith("ERROR ")) return new TagResult(false, false, wanted, line[6..]);
        var changed = line.StartsWith("OK");
        var rest = line.Length > (changed ? 3 : 5) ? line[(changed ? 3 : 5)..] : "";
        var tags = rest.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
        return new TagResult(true, changed, tags, changed ? "Workshop tags set to " + Describe(tags) + "." : "Workshop tags already right: " + Describe(tags) + ".");
    }
}
