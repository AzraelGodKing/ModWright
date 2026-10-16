using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Shapes a change note before it goes to Steam: an optional template around it (per mods folder) and Markdown turned
/// into the BBCode Steam's change notes understand. Neither touches the changelog file, the upload history or the
/// Discord announcement.
/// </summary>
public static class ChangeNotes
{
    /// <summary>The placeholders a template may use.</summary>
    public static readonly string[] Placeholders = { "{note}", "{name}", "{version}", "{date}", "{game}" };

    /// <summary>
    /// Fills a template: {note} is the change note, {name}, {version} and {game} the mod's, {date} today's date
    /// (yyyy-MM-dd). A template without {note} gets the note after it. An empty template leaves the note as it is.
    /// </summary>
    public static string Apply(string? template, string note, string name, string version, string game, DateTime? today = null)
    {
        if (string.IsNullOrWhiteSpace(template)) return note;
        var t = template.Replace("\r\n", "\n").Trim('\n');
        if (!t.Contains("{note}", StringComparison.OrdinalIgnoreCase)) t += "\n\n{note}";
        var date = (today ?? DateTime.Now).ToString("yyyy-MM-dd");
        // One pass, so a note that itself contains "{name}" is left as typed.
        return Regex.Replace(t, @"\{(note|name|version|date|game)\}", m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "note" => note,
            "name" => name,
            "version" => version,
            "date" => date,
            _ => game,
        }, RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>True when the layout's changelog is a Markdown file, so its notes are worth converting.</summary>
    public static bool IsMarkdown(string changelog) =>
        changelog.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || changelog.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    static readonly Regex Heading = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
    static readonly Regex Bullet = new(@"^\s*[-*+]\s+(.*)$");
    static readonly Regex Numbered = new(@"^\s*\d+[.)]\s+(.*)$");
    static readonly Regex Rule = new(@"^\s*([-*_])(\s*\1){2,}\s*$");
    static readonly Regex Fence = new(@"^\s*(```|~~~)");
    static readonly Regex Link = new(@"\[([^\]]+)\]\((https?://[^)\s]+)\)");
    static readonly Regex AutoLink = new(@"<(https?://[^>\s]+)>");
    static readonly Regex Code = new(@"`([^`]+)`");
    static readonly Regex Bold = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1");
    static readonly Regex Strike = new(@"~~(?=\S)(.+?)(?<=\S)~~");
    static readonly Regex ItalicStar = new(@"(?<![\w*])\*(?=\S)([^*]+?)(?<=\S)\*(?![\w*])");
    // Underscores inside words (snake_case names) are not emphasis.
    static readonly Regex ItalicUnderscore = new(@"(?<![\w_])_(?=\S)([^_]+?)(?<=\S)_(?![\w_])");

    /// <summary>
    /// Turns Markdown into Steam BBCode: headings become [h1] to [h3], bullet and numbered lists [list] and [olist],
    /// **bold**, *italic*, ~~strike~~, [links](https://...), &gt; quotes, --- rules and ``` code blocks their tags.
    /// Inline `code` keeps its text. BBCode already in the note is left as it is.
    /// </summary>
    public static string MarkdownToBBCode(string md)
    {
        var lines = md.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        string? openList = null; // "list" or "olist"
        var inQuote = false;
        var inCode = false;

        void CloseList() { if (openList != null) { sb.Append("[/").Append(openList).Append("]\n"); openList = null; } }
        void CloseQuote() { if (inQuote) { sb.Append("[/quote]\n"); inQuote = false; } }

        foreach (var raw in lines)
        {
            if (Fence.IsMatch(raw))
            {
                CloseList(); CloseQuote();
                sb.Append(inCode ? "[/code]\n" : "[code]\n");
                inCode = !inCode;
                continue;
            }
            if (inCode) { sb.Append(raw).Append('\n'); continue; }

            var line = raw;
            if (line.TrimStart().StartsWith('>'))
            {
                CloseList();
                if (!inQuote) { sb.Append("[quote]\n"); inQuote = true; }
                sb.Append(Inline(line.TrimStart()[1..].TrimStart())).Append('\n');
                continue;
            }
            CloseQuote();

            Match m;
            if ((m = Bullet.Match(line)).Success && !Rule.IsMatch(line)) { OpenList("list"); sb.Append("[*]").Append(Inline(m.Groups[1].Value)).Append('\n'); continue; }
            if ((m = Numbered.Match(line)).Success) { OpenList("olist"); sb.Append("[*]").Append(Inline(m.Groups[1].Value)).Append('\n'); continue; }
            // A blank line inside a list ends nothing yet; the next line decides.
            if (line.Trim().Length == 0 && openList != null) continue;
            CloseList();

            if ((m = Heading.Match(line)).Success)
            {
                var level = Math.Min(3, m.Groups[1].Value.Length);
                sb.Append($"[h{level}]").Append(Inline(m.Groups[2].Value)).Append($"[/h{level}]\n");
            }
            else if (Rule.IsMatch(line)) sb.Append("[hr][/hr]\n");
            else sb.Append(Inline(line)).Append('\n');
        }
        if (inCode) sb.Append("[/code]\n");
        CloseList(); CloseQuote();
        return sb.ToString().Trim('\n');

        void OpenList(string kind)
        {
            if (openList == kind) return;
            CloseList();
            sb.Append('[').Append(kind).Append("]\n");
            openList = kind;
        }
    }

    static string Inline(string s)
    {
        // Inline code and links first, so nothing inside them (an underscore in a URL) is read as emphasis; they are put
        // back at the end, code as plain text.
        var kept = new List<string>();
        string Keep(string text) { kept.Add(text); return $"\u0001{kept.Count - 1}\u0001"; }
        s = Code.Replace(s, m => Keep(m.Groups[1].Value));
        s = Link.Replace(s, m => Keep($"[url={m.Groups[2].Value}]{m.Groups[1].Value}[/url]"));
        s = AutoLink.Replace(s, m => Keep($"[url={m.Groups[1].Value}]{m.Groups[1].Value}[/url]"));
        s = Bold.Replace(s, "[b]$2[/b]");
        s = Strike.Replace(s, "[strike]$1[/strike]");
        s = ItalicStar.Replace(s, "[i]$1[/i]");
        s = ItalicUnderscore.Replace(s, "[i]$1[/i]");
        return Regex.Replace(s, "\u0001(\\d+)\u0001", m => kept[int.Parse(m.Groups[1].Value)]);
    }
}
