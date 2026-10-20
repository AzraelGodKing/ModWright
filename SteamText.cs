using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Steam's BBCode, the formatting of Workshop descriptions and change notes: finding tags left open or closed twice
/// before they reach a page, and turning it into HTML that looks like Steam's page, to preview before uploading.
/// </summary>
public static class SteamText
{
    /// <summary>Tags that come in [tag]...[/tag] pairs. [*] (a list item) stands alone.</summary>
    static readonly HashSet<string> Paired = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "b", "i", "u", "strike", "spoiler", "noparse", "url", "list", "olist", "quote", "code", "table", "tr", "th", "td", "img", "hr",
    };

    static readonly Regex Tag = new(@"\[(/?)([a-zA-Z0-9]+)(=[^\]\n]*)?\]");

    /// <summary>What is wrong with the BBCode in <paramref name="text"/>, for people ("[b] is never closed"). Empty when it is fine.</summary>
    public static List<string> Problems(string text)
    {
        var problems = new List<string>();
        var open = new Stack<string>();
        string? raw = null; // inside [code] or [noparse] nothing is a tag
        foreach (Match m in Tag.Matches(text))
        {
            var name = m.Groups[2].Value.ToLowerInvariant();
            if (!Paired.Contains(name)) continue;
            var closing = m.Groups[1].Value == "/";
            if (raw != null)
            {
                if (closing && name == raw) { raw = null; open.Pop(); }
                continue;
            }
            if (!closing)
            {
                open.Push(name);
                if (name is "code" or "noparse") raw = name;
                continue;
            }
            if (open.Count == 0 || !open.Contains(name)) { problems.Add($"has a [/{name}] with no [{name}] before it"); continue; }
            while (open.Peek() != name) problems.Add($"closes [{name}] before the [{open.Pop()}] inside it");
            open.Pop();
        }
        foreach (var t in open.Reverse()) problems.Add($"never closes its [{t}]");
        return problems.Distinct().ToList();
    }

    /// <summary>
    /// Steam's BBCode as HTML: headings, bold, italic, underline, strike, links, lists, quotes, code, spoilers, rules,
    /// tables and images. Everything else is escaped, so the result is safe to open in a browser.
    /// </summary>
    public static string ToHtml(string bbcode)
    {
        var text = bbcode.Replace("\r\n", "\n");
        // [noparse] and [code] keep their text exactly: take them out first and put them back at the end.
        var kept = new List<string>();
        string Keep(string html) { kept.Add(html); return $"\u0001{kept.Count - 1}\u0001"; }
        text = Regex.Replace(text, @"\[noparse\](.*?)\[/noparse\]", m => Keep(Enc(m.Groups[1].Value)), RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[code\]\n?(.*?)\[/code\]\n?", m => Keep("<pre>" + Enc(m.Groups[1].Value) + "</pre>"), RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Enc(text);

        string R(string s, string pattern, MatchEvaluator e) => Regex.Replace(s, pattern, e, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = R(text, @"\[url=([^\]\n]+)\](.*?)\[/url\]", m => Link(m.Groups[1].Value, m.Groups[2].Value));
        text = R(text, @"\[url\](.*?)\[/url\]", m => Link(m.Groups[1].Value, m.Groups[1].Value));
        text = R(text, @"\[img\](.*?)\[/img\]", m => Safe(m.Groups[1].Value) is { } src ? $"<img src=\"{src}\" alt=\"\">" : m.Value);
        text = R(text, @"\[quote(?:=([^\]\n]*))?\]\n?", m => "<blockquote>" + (m.Groups[1].Success ? $"<div class=\"by\">Originally posted by <b>{m.Groups[1].Value}</b>:</div>" : ""));
        text = R(text, @"\[/quote\]\n?", _ => "</blockquote>");
        text = R(text, @"\[(h[1-3])\](.*?)\[/\1\]\n?", m => $"<{m.Groups[1].Value.ToLowerInvariant()}>{m.Groups[2].Value}</{m.Groups[1].Value.ToLowerInvariant()}>");
        text = R(text, @"\[hr\]\[/hr\]\n?|\[hr\]\n?", _ => "<hr>");
        text = R(text, @"\[(list|olist)\]\n?", m => m.Groups[1].Value.Equals("list", StringComparison.OrdinalIgnoreCase) ? "<ul>" : "<ol>");
        text = R(text, @"\n?\[/(list|olist)\]\n?", m => m.Groups[1].Value.Equals("list", StringComparison.OrdinalIgnoreCase) ? "</ul>" : "</ol>");
        text = R(text, @"\[\*\]\s*(.*?)(?=\n?\[\*\]|\n?</[uo]l>)", m => "<li>" + m.Groups[1].Value.TrimEnd('\n') + "</li>");
        foreach (var (bb, html) in new[] { ("b", "b"), ("i", "i"), ("u", "u"), ("strike", "s") })
            text = R(text, $@"\[{bb}\](.*?)\[/{bb}\]", m => $"<{html}>{m.Groups[1].Value}</{html}>");
        text = R(text, @"\[spoiler\](.*?)\[/spoiler\]", m => $"<span class=\"spoiler\">{m.Groups[1].Value}</span>");
        text = R(text, @"\[table\]\n?", _ => "<table>");
        text = R(text, @"\[/table\]\n?", _ => "</table>");
        foreach (var t in new[] { "tr", "th", "td" })
        {
            text = R(text, $@"\[{t}\]\n?", _ => $"<{t}>");
            text = R(text, $@"\[/{t}\]\n?", _ => $"</{t}>");
        }
        // Line breaks between block elements are already part of the layout.
        text = Regex.Replace(text, @"\n?(</?(?:ul|ol|li|table|tr|blockquote|h[1-3])[^>]*>|<hr>|<pre>)\n?", "$1");
        text = text.Replace("\n", "<br>\n");
        return Regex.Replace(text, "\u0001(\\d+)\u0001", m => kept[int.Parse(m.Groups[1].Value)]);
    }

    /// <summary>A whole page that shows <paramref name="bbcode"/> roughly as Steam's Workshop page does.</summary>
    public static string Page(string title, string bbcode, string? subtitle = null) => $$"""
        <!doctype html>
        <html><head><meta charset="utf-8"><title>{{Enc(title)}}</title>
        <style>
        body { background: #1b2838; color: #acb2b8; font: 14px/1.5 Arial, Helvetica, sans-serif; margin: 0; padding: 24px; }
        .box { max-width: 640px; margin: 0 auto; background: rgba(0,0,0,.2); padding: 16px 20px; }
        .title { color: #fff; font-size: 26px; margin: 0 0 4px; }
        .sub { color: #8f98a0; font-size: 12px; margin-bottom: 14px; border-bottom: 1px solid #2a3f5a; padding-bottom: 10px; }
        h1 { color: #5aa9d6; font-size: 20px; font-weight: normal; margin: 12px 0 4px; }
        h2 { color: #5aa9d6; font-size: 16px; font-weight: normal; margin: 10px 0 4px; }
        h3 { color: #5aa9d6; font-size: 14px; font-weight: bold; margin: 8px 0 2px; }
        a { color: #ebebeb; } a:hover { color: #66c0f4; }
        ul, ol { margin: 4px 0 4px 8px; padding-left: 24px; }
        blockquote { border: 1px solid #56707f; background: rgba(0,0,0,.15); margin: 8px 0; padding: 8px 12px; color: #8f98a0; }
        blockquote .by { font-size: 12px; margin-bottom: 4px; }
        pre { background: rgba(0,0,0,.3); padding: 8px; overflow: auto; color: #c6d4df; }
        .spoiler { background: #000; color: #000; } .spoiler:hover { color: #fff; }
        hr { border: 0; border-top: 1px solid #4a5d70; }
        table { border-collapse: collapse; } td, th { border: 1px solid #4a5d70; padding: 4px 8px; }
        img { max-width: 100%; }
        </style></head>
        <body><div class="box"><div class="title">{{Enc(title)}}</div>{{(subtitle is { Length: > 0 } ? $"<div class=\"sub\">{Enc(subtitle)}</div>" : "")}}
        <div class="text">{{ToHtml(bbcode)}}</div></div></body></html>
        """;

    /// <summary>
    /// Writes <paramref name="bbcode"/> as a page into the data folder's previews folder and returns its path, to open
    /// in the browser. Older previews are removed.
    /// </summary>
    public static string WritePreview(string title, string bbcode, string? subtitle = null, string? dir = null)
    {
        dir ??= Path.Combine(AppSettings.Folder, "previews");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.html"))
            try { if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1)) File.Delete(old); } catch (IOException) { }
        var path = Path.Combine(dir, $"preview-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.html");
        File.WriteAllText(path, Page(title, bbcode, subtitle), new UTF8Encoding(false));
        return path;
    }

    static string Enc(string s) => WebUtility.HtmlEncode(s);

    /// <summary>An http(s) address as it may go into an attribute (already HTML-encoded), else null.</summary>
    static string? Safe(string url)
    {
        var u = WebUtility.HtmlDecode(url).Trim();
        return Uri.TryCreate(u, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? WebUtility.HtmlEncode(parsed.AbsoluteUri) : null;
    }

    static string Link(string href, string label) => Safe(href) is { } h ? $"<a href=\"{h}\" target=\"_blank\" rel=\"noreferrer\">{label}</a>" : label;
}
