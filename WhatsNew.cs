using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// After the app updates itself, the CHANGELOG sections it skipped over are shown once, so people find the features
/// they just got. The changelog is built into the program.
/// </summary>
public static class WhatsNew
{
    static readonly Regex Heading = new(@"^##\s+(\d+(?:\.\d+){1,3})\s*$", RegexOptions.Multiline);

    /// <summary>The changelog built into this program, or "" when it is missing.</summary>
    public static string BuiltIn()
    {
        using var s = typeof(WhatsNew).Assembly.GetManifestResourceStream("SteamCmdHelper.CHANGELOG.md");
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>The sections for versions after <paramref name="from"/> up to and including <paramref name="to"/>, newest first.</summary>
    public static List<(string Version, string Text)> Between(string changelog, string from, string to)
    {
        var text = changelog.Replace("\r\n", "\n");
        var heads = Heading.Matches(text);
        var list = new List<(string, string)>();
        for (var i = 0; i < heads.Count; i++)
        {
            var v = heads[i].Groups[1].Value;
            if (!UpdateCheck.IsNewer(v, from) || UpdateCheck.IsNewer(v, to)) continue;
            var start = heads[i].Index + heads[i].Length;
            var end = i + 1 < heads.Count ? heads[i + 1].Index : text.Length;
            list.Add((v, text[start..end].Trim()));
        }
        return list;
    }

    /// <summary>
    /// Whether to show what's new now: only when the program is newer than the version that last ran. The first run
    /// ever (nothing recorded) shows nothing, since everything is new then.
    /// </summary>
    public static bool ShouldShow(string lastSeen, string current) => lastSeen.Length > 0 && UpdateCheck.IsNewer(current, lastSeen);

    /// <summary>
    /// The sections as blocks to lay out: a heading per version, then its bullets and paragraphs. The changelog wraps
    /// long items over several indented lines; those are joined back into one, so the window wraps them to its own
    /// width instead of breaking every line where the file did.
    /// </summary>
    public static List<NoteBlock> Blocks(IEnumerable<(string Version, string Text)> sections)
    {
        var blocks = new List<NoteBlock>();
        foreach (var (version, text) in sections)
        {
            blocks.Add(new NoteBlock(NoteKind.Version, new[] { new NoteSpan("Version " + version) }));
            NoteKind? kind = null;
            var words = new List<string>();
            void Flush()
            {
                if (kind is { } k && words.Count > 0) blocks.Add(new NoteBlock(k, Spans(string.Join(" ", words))));
                kind = null;
                words.Clear();
            }
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                var item = Regex.Match(line, @"^\s*[-*+]\s+(.*)$");
                var head = Regex.Match(line, @"^#{1,6}\s+(.*)$");
                if (line.Trim().Length == 0) Flush();
                else if (item.Success) { Flush(); kind = NoteKind.Bullet; words.Add(item.Groups[1].Value.Trim()); }
                else if (head.Success) { Flush(); kind = NoteKind.Heading; words.Add(head.Groups[1].Value.Trim()); Flush(); }
                else { kind ??= NoteKind.Text; words.Add(line.Trim()); }
            }
            Flush();
        }
        return blocks;
    }

    /// <summary>One line of Markdown as plain, bold and code runs; link targets are dropped and their text kept.</summary>
    public static List<NoteSpan> Spans(string markdown)
    {
        var text = Regex.Replace(markdown, @"\[([^\]]+)\]\([^)]+\)", "$1");
        var spans = new List<NoteSpan>();
        var at = 0;
        foreach (Match m in Regex.Matches(text, @"\*\*(?<b>.+?)\*\*|`(?<c>[^`]+)`"))
        {
            if (m.Index > at) spans.Add(new NoteSpan(text[at..m.Index]));
            spans.Add(m.Groups["b"].Success ? new NoteSpan(m.Groups["b"].Value.Replace("`", ""), Bold: true) : new NoteSpan(m.Groups["c"].Value, Code: true));
            at = m.Index + m.Length;
        }
        if (at < text.Length) spans.Add(new NoteSpan(text[at..]));
        return spans;
    }
}

public enum NoteKind { Version, Heading, Bullet, Text }

/// <summary>A run of changelog text; <see cref="Code"/> is a command or file name.</summary>
public sealed record NoteSpan(string Text, bool Bold = false, bool Code = false);

/// <summary>A version heading, a sub-heading, a bullet or a paragraph of the changelog.</summary>
public sealed record NoteBlock(NoteKind Kind, IReadOnlyList<NoteSpan> Spans)
{
    public string Text => string.Concat(Spans.Select(s => s.Text));
}
