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

    /// <summary>Markdown made readable as plain text: bullets as dots, emphasis and code marks and link targets dropped.</summary>
    public static string Plain(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            var l = Regex.Replace(line, @"^(\s*)- ", "$1• ");
            l = Regex.Replace(l, @"\[([^\]]+)\]\([^)]+\)", "$1");
            l = l.Replace("**", "").Replace("`", "");
            return l;
        });
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>One text for the window: each version as a heading line over its notes.</summary>
    public static string Render(IEnumerable<(string Version, string Text)> sections) =>
        string.Join(Environment.NewLine + Environment.NewLine, sections.Select(s => $"Version {s.Version}{Environment.NewLine}{Plain(s.Text)}"));
}
