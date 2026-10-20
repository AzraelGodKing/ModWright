namespace SteamCmdHelper;

/// <summary>One action the command palette can run: its name, where it lives, its shortcut, and what it does.</summary>
public sealed record PaletteEntry(string Name, string Where, string Shortcut, Action Run);

/// <summary>
/// The command palette's search (Ctrl+K): finds actions by what people type, so "tick ch" finds "Tick changed" and
/// "dl steam" finds "Download from Steam...". Words that start a word in the name count most, then text anywhere in
/// it, then letters in order.
/// </summary>
public static class PaletteSearch
{
    /// <summary>A button or menu text as people read it: no "&amp;" mnemonics, dots or arrows.</summary>
    public static string Clean(string text) =>
        text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Replace("▾", "").Replace("...", "").Replace("…", "").Trim();

    /// <summary>How well <paramref name="query"/> matches <paramref name="text"/>: higher is better, null for no match.</summary>
    public static int? Score(string query, string text)
    {
        var q = query.Trim().ToLowerInvariant();
        var t = text.ToLowerInvariant();
        if (q.Length == 0) return 0;
        var words = t.Split(new[] { ' ', '-', '>', '›', ':', '(', ')', ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var parts = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Every typed word starts a word of the name, in any order.
        if (parts.All(p => words.Any(w => w.StartsWith(p))))
            return 300 - t.Length + (t.StartsWith(parts[0]) ? 50 : 0);
        if (t.Contains(q)) return 200 - t.Length;
        if (parts.All(t.Contains)) return 150 - t.Length;
        // The letters in order, as in "dlst" for "download from steam".
        var i = 0;
        foreach (var c in t)
            if (i < q.Length && c == q[i]) i++;
        return i == q.Length && q.Replace(" ", "").Length >= 2 ? 100 - t.Length : null;
    }

    /// <summary>The entries that match <paramref name="query"/>, best first (all of them, in order, for an empty query).</summary>
    public static List<PaletteEntry> Rank(string query, IEnumerable<PaletteEntry> entries) =>
        entries.Select((e, i) => (e, i, s: Score(query, e.Name + " " + e.Where)))
            .Where(x => x.s != null)
            .OrderByDescending(x => x.s).ThenBy(x => x.i)
            .Select(x => x.e).ToList();
}
