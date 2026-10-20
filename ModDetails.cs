using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// What the details panel beside the mod list says about the selected mod (or the whole list when none is selected).
/// No UI code, so it can be tested; <see cref="ModDetailsPanel"/> lays it out.
/// </summary>
public static class ModDetails
{
    /// <summary>The panel's narrowest width, and the narrowest the list may get beside it (96-DPI pixels).</summary>
    public const int MinPanelWidth = 280, MinStatusWidth = 160;

    /// <summary>
    /// True when the list still has room for its columns with the panel beside it: every column but Status at its
    /// width, Status at least <see cref="MinStatusWidth"/>, plus the panel at its narrowest. Otherwise the panel stays
    /// hidden and the list keeps the whole width, so a small window never gets a list it has to scroll sideways.
    /// </summary>
    public static bool Fits(int areaWidth, int fixedColumnsWidth, int minStatus, int minPanel) =>
        areaWidth >= fixedColumnsWidth + minStatus + minPanel;

    /// <summary>
    /// How wide the panel is: the width it was left at when there is one, else about a third of the area; never
    /// narrower than <paramref name="minPanel"/>, and never so wide that the list loses its columns.
    /// </summary>
    public static int Width(int saved, int areaWidth, int fixedColumnsWidth, int minStatus, int minPanel)
    {
        var want = saved > 0 ? saved : areaWidth * 36 / 100;
        var most = Math.Max(minPanel, areaWidth - fixedColumnsWidth - minStatus);
        return Math.Clamp(want, minPanel, most);
    }

    /// <summary>The label/value lines under the mod's name. Lines with nothing to say are left out.</summary>
    public static List<(string Label, string Value)> Facts(ModInfo m, string? onSteamVersion, WorkshopItem? live, UploadEntry? lastUpload,
        IReadOnlyList<string> tags, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var facts = new List<(string, string)>
        {
            ("To upload", m.Version.Length > 0 ? m.Version : "?"),
        };
        if (m.OnSteam)
        {
            facts.Add(("On Steam", Known(onSteamVersion) ? onSteamVersion! : "not known yet"));
            if (live is { Public: true })
            {
                if (live.Updated is { } u) facts.Add(("Updated", $"{u.ToLocalTime():d MMM yyyy} ({Dependencies.Ago(at - u)})"));
                facts.Add(("Subscribers", live.Subscribers.ToString("N0") + (live.LifetimeSubscribers > live.Subscribers ? $" ({live.LifetimeSubscribers:N0} all-time)" : "")));
                if (live.Favorites > 0) facts.Add(("Favourites", live.Favorites.ToString("N0")));
                if (live.Views > 0) facts.Add(("Views", live.Views.ToString("N0")));
                if (live.Size > 0) facts.Add(("Size", Size(live.Size)));
                facts.Add(("Visibility", Capital(live.VisibilityText) + (live.Banned ? ", banned" : "")));
            }
            else if (live != null) facts.Add(("Visibility", "not public (private, friends only, or removed)"));
            facts.Add(("Workshop ID", m.WorkshopId!));
        }
        else facts.Add(("On Steam", "not yet; uploading creates it"));
        if (lastUpload != null)
            facts.Add(("Last upload", $"{lastUpload.Version} on {lastUpload.Time.ToLocalTime():d MMM yyyy HH:mm} from this PC"));
        if (tags.Count > 0) facts.Add(("Tags", string.Join(", ", tags)));
        if (m.Dirty) facts.Add(("Git", "uncommitted changes (not uploaded with git packaging)"));
        return facts;
    }

    static bool Known(string? v) => v != null && Regex.IsMatch(v, @"\d");

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Size(long bytes) =>
        bytes >= 1048576 ? $"{bytes / 1048576.0:0.#} MB" : bytes >= 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes} bytes";

    static readonly Regex Tag = new(@"\[/?[a-z0-9*]+(?:=[^\]]*)?\]", RegexOptions.IgnoreCase);
    static readonly Regex Blank = new(@"\n{3,}");

    /// <summary>
    /// Steam BBCode as plain text for a short preview: tags dropped, list bullets kept, blank lines squeezed, cut at
    /// a word near <paramref name="max"/> characters with an ellipsis.
    /// </summary>
    public static string PlainText(string bbcode, int max)
    {
        var t = bbcode.Replace("\r\n", "\n").Replace('\r', '\n');
        t = Regex.Replace(t, @"\s*\[\*\]\s*", "\n• ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\[url=([^\]]*)\](.*?)\[/url\]", "$2", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        t = Regex.Replace(t, @"\[img\].*?\[/img\]", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        t = Tag.Replace(t, "");
        t = string.Join("\n", t.Split('\n').Select(l => l.TrimEnd()));
        t = Blank.Replace(t, "\n\n").Trim();
        if (t.Length <= max) return t;
        var cut = t.LastIndexOf(' ', Math.Max(0, max - 1));
        return t[..(cut > max / 2 ? cut : max)].TrimEnd() + "…";
    }

    /// <summary>With no mod selected: the whole list in one line ("11 mods: 9 up to date, 2 new").</summary>
    public static string Summary(IReadOnlyCollection<RowState> states)
    {
        if (states.Count == 0) return "No mods in this folder yet.";
        var parts = states.GroupBy(s => s).OrderBy(g => Order(g.Key))
            .Select(g => $"{g.Count()} {ModRows.Label(g.Key).ToLowerInvariant()}");
        return $"{states.Count} mod{(states.Count == 1 ? "" : "s")}: " + string.Join(", ", parts);
    }

    // The ones that need something first, the settled ones last.
    static int Order(RowState s) => s switch
    {
        RowState.Fix => 0, RowState.Failed => 1, RowState.Changed => 2, RowState.New => 3, RowState.Ready => 4, _ => 5,
    };
}
