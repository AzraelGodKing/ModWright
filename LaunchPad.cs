using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// The version bump the commits since the last upload point to: major for a breaking change ("BREAKING CHANGE",
/// "feat!:"), minor for something new ("feat:", "add ...", "new ..."), patch for the rest. A hint only; the author decides.
/// </summary>
public static class BumpSuggestion
{
    static readonly Regex Breaking = new(@"^\w+(\([^)]*\))?!:|\bBREAKING[ -]CHANGES?\b|\bbreaks? (old )?saves?\b|\bremoved?\b.*\b(support|option|setting)s?\b", RegexOptions.IgnoreCase);
    static readonly Regex Feature = new(@"^(feat|feature)(\([^)]*\))?:|^(add|adds|added|new|introduce|introduces|support|supports)\b", RegexOptions.IgnoreCase);

    /// <summary>The part to raise and the subject that decided it, or null with no subjects.</summary>
    public static (ModScanner.Bump Part, string Reason)? Suggest(IEnumerable<string> subjects)
    {
        var list = subjects.Select(s => s.Trim().TrimStart('-', '*').Trim()).Where(s => s.Length > 0).ToList();
        if (list.Count == 0) return null;
        if (list.FirstOrDefault(s => Breaking.IsMatch(s)) is { } b) return (ModScanner.Bump.Major, b);
        if (list.FirstOrDefault(s => Feature.IsMatch(s)) is { } f) return (ModScanner.Bump.Minor, f);
        return (ModScanner.Bump.Patch, list.Count == 1 ? list[0] : $"{list.Count} commits, none adds a feature or breaks anything");
    }

    /// <summary>True when <paramref name="version"/> is <paramref name="wanted"/> or newer ("1.10.0" is newer than "1.9.0").</summary>
    public static bool AtLeast(string version, string wanted)
    {
        static int[] Parts(string v) => Regex.Matches(v, @"\d+").Take(4).Select(x => int.TryParse(x.Value, out var n) ? n : 0).ToArray();
        var (x, y) = (Parts(version), Parts(wanted));
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var (p, q) = (i < x.Length ? x[i] : 0, i < y.Length ? y[i] : 0);
            if (p != q) return p > q;
        }
        return true;
    }
}

/// <summary>Notes on a mod's subscriber history: a drop worth looking into, and round numbers passed.</summary>
public static class SubscriberAlerts
{
    public static readonly long[] Milestones = { 100, 250, 500, 1_000, 2_500, 5_000, 10_000, 25_000, 50_000, 100_000, 250_000, 500_000, 1_000_000 };

    /// <summary>
    /// "Lost 63 subscribers this week (-8%)" when the count fell by at least <paramref name="share"/> of what it was a
    /// week ago and at least <paramref name="least"/> people; null otherwise or without a week of history.
    /// </summary>
    public static string? Drop(IReadOnlyList<SubscriberPoint>? points, DateOnly today, double share = 0.05, long least = 10)
    {
        if (points == null || points.Count < 2) return null;
        var now = points[^1].Subscribers;
        if (SubscriberStats.At(points, today.AddDays(-7)) is not long week || week <= 0) return null;
        var lost = week - now;
        if (lost < least || lost < week * share) return null;
        return $"lost {lost:N0} subscribers this week (-{lost * 100.0 / week:0}%)";
    }

    /// <summary>The biggest round number passed between the last two counts, or null.</summary>
    public static long? Milestone(IReadOnlyList<SubscriberPoint>? points)
    {
        if (points == null || points.Count < 2) return null;
        var (before, now) = (points[^2].Subscribers, points[^1].Subscribers);
        return Milestones.Where(m => before < m && now >= m).Select(m => (long?)m).LastOrDefault();
    }
}

/// <summary>
/// A release checklist: the author's own steps for a mods folder ("tested on 1.5", "updated the screenshots"), shown as
/// boxes to tick in the upload confirmation. One step per line; blank lines and lines starting with # are left out.
/// </summary>
public static class ReleaseChecklist
{
    public static List<string> Items(string? text) =>
        (text ?? "").Replace("\r", "").Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '[', ']', ' ').Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
}

/// <summary>
/// Turns a README written in Markdown into Steam's BBCode for the Workshop description: headings, bold, italic,
/// strikethrough, inline code, code blocks, links, images, lists, quotes and rules. Anything else stays as it is.
/// </summary>
public static class MarkdownBBCode
{
    static readonly Regex Heading = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
    static readonly Regex Bullet = new(@"^(\s*)[-*+]\s+(.*)$");
    static readonly Regex Numbered = new(@"^(\s*)\d+[.)]\s+(.*)$");
    static readonly Regex Rule = new(@"^\s*([-*_])(\s*\1){2,}\s*$");
    static readonly Regex Image = new(@"!\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)");
    static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)");
    static readonly Regex AutoLink = new(@"<(https?://[^>\s]+)>");
    static readonly Regex Bold = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1");
    static readonly Regex Italic = new(@"(?<![\w*])([*_])(?=\S)(.+?)(?<=\S)\1(?![\w*])");
    static readonly Regex Strike = new(@"~~(?=\S)(.+?)(?<=\S)~~");
    static readonly Regex Code = new(@"`([^`]+)`");
    static readonly Regex HtmlComment = new(@"<!--.*?-->", RegexOptions.Singleline);

    public static string Convert(string markdown)
    {
        var lines = HtmlComment.Replace(markdown.Replace("\r\n", "\n").Replace('\r', '\n'), "").Split('\n');
        var sb = new StringBuilder();
        string? list = null; // "list" or "olist" while one is open
        var inCode = false;
        void CloseList() { if (list != null) { sb.Append("[/").Append(list).Append("]\n"); list = null; } }
        foreach (var raw in lines)
        {
            if (raw.TrimStart().StartsWith("```"))
            {
                CloseList();
                sb.Append(inCode ? "[/code]\n" : "[code]\n");
                inCode = !inCode;
                continue;
            }
            if (inCode) { sb.Append(raw).Append('\n'); continue; }
            if (Rule.IsMatch(raw)) { CloseList(); sb.Append("[hr][/hr]\n"); continue; }
            if (Heading.Match(raw) is { Success: true } h)
            {
                CloseList();
                var level = Math.Min(3, h.Groups[1].Value.Length);
                sb.Append($"[h{level}]").Append(Inline(h.Groups[2].Value)).Append($"[/h{level}]\n");
                continue;
            }
            var b = Bullet.Match(raw);
            var n = b.Success ? Match.Empty : Numbered.Match(raw);
            if (b.Success || n.Success)
            {
                var kind = b.Success ? "list" : "olist";
                if (list != kind) { CloseList(); sb.Append('[').Append(kind).Append("]\n"); list = kind; }
                sb.Append("[*]").Append(Inline((b.Success ? b : n).Groups[2].Value)).Append('\n');
                continue;
            }
            if (raw.StartsWith('>'))
            {
                CloseList();
                sb.Append("[quote]").Append(Inline(raw.TrimStart('>').Trim())).Append("[/quote]\n");
                continue;
            }
            if (raw.Trim().Length == 0) CloseList();
            sb.Append(Inline(raw.TrimEnd())).Append('\n');
        }
        CloseList();
        if (inCode) sb.Append("[/code]\n");
        return Regex.Replace(Regex.Replace(sb.ToString(), @"\[/quote\]\n\[quote\]", "\n"), @"\n{3,}", "\n\n").Trim();
    }

    static string Inline(string s)
    {
        // Inline code first, kept away from the other rules.
        var codes = new List<string>();
        s = Code.Replace(s, m => { codes.Add(m.Groups[1].Value); return $"\u0001{codes.Count - 1}\u0001"; });
        s = Image.Replace(s, m => $"[img]{m.Groups[2].Value}[/img]");
        s = Link.Replace(s, m => $"[url={m.Groups[2].Value}]{m.Groups[1].Value}[/url]");
        s = AutoLink.Replace(s, m => $"[url]{m.Groups[1].Value}[/url]");
        s = Bold.Replace(s, m => $"[b]{m.Groups[2].Value}[/b]");
        s = Italic.Replace(s, m => $"[i]{m.Groups[2].Value}[/i]");
        s = Strike.Replace(s, m => $"[strike]{m.Groups[1].Value}[/strike]");
        return Regex.Replace(s, "\u0001(\\d+)\u0001", m => $"[noparse]{codes[int.Parse(m.Groups[1].Value)]}[/noparse]");
    }
}

/// <summary>
/// Releasing a family of mods together: upload the ones others need first, and when one fails, skip the ones that
/// need it so players never get a mod whose required update didn't go up.
/// </summary>
public static class FamilyRelease
{
    /// <summary>
    /// <paramref name="items"/> with each one after the picked items it needs (by <paramref name="needs"/>, matched on
    /// <paramref name="key"/>), keeping the original order otherwise. A loop is left in the original order.
    /// </summary>
    public static List<T> Order<T>(IReadOnlyList<T> items, Func<T, string?> key, Func<T, IEnumerable<string>> needs)
    {
        var byKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items) if (key(i) is { Length: > 0 } k) byKey.TryAdd(k, i);
        var done = new HashSet<T>();
        var visiting = new HashSet<T>();
        var result = new List<T>();
        void Visit(T i)
        {
            if (done.Contains(i) || !visiting.Add(i)) return;
            foreach (var n in needs(i))
                if (byKey.TryGetValue(n, out var dep) && !EqualityComparer<T>.Default.Equals(dep, i)) Visit(dep);
            visiting.Remove(i);
            if (done.Add(i)) result.Add(i);
        }
        foreach (var i in items) Visit(i);
        return result;
    }

    /// <summary>The picked items that need a failed one, directly or through another picked item.</summary>
    public static HashSet<string> Dependents<T>(IEnumerable<string> failed, IReadOnlyList<T> items, Func<T, string?> key, Func<T, IEnumerable<string>> needs)
    {
        var bad = new HashSet<string>(failed, StringComparer.OrdinalIgnoreCase);
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool grew;
        do
        {
            grew = false;
            foreach (var i in items)
                if (key(i) is { Length: > 0 } k && !bad.Contains(k) && needs(i).Any(bad.Contains))
                {
                    bad.Add(k);
                    skipped.Add(k);
                    grew = true;
                }
        } while (grew);
        return skipped;
    }
}
