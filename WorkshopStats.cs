using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>The counts the Stats window can chart.</summary>
public enum StatKind { Subscribers, Favorites, Views, LifetimeSubscribers }

/// <summary>
/// Workshop numbers over time, from the daily counts <see cref="SubscriberStats"/> keeps, and new comments on each
/// item, which only the Steam client can count (read on demand through <see cref="WorkshopExtras"/>).
/// </summary>
public static class WorkshopStats
{
    public static string Name(StatKind k) => k switch
    {
        StatKind.Favorites => "Favourites",
        StatKind.Views => "Unique visitors",
        StatKind.LifetimeSubscribers => "Subscribers ever",
        _ => "Current subscribers",
    };

    public static long Value(SubscriberPoint p, StatKind k) => k switch
    {
        StatKind.Favorites => p.Favorites,
        StatKind.Views => p.Views,
        StatKind.LifetimeSubscribers => p.LifetimeSubscribers,
        _ => p.Subscribers,
    };

    /// <summary>One item's counts by day, leaving out days recorded before that count was kept (zeros).</summary>
    public static List<(DateOnly Day, long Value)> Series(IEnumerable<SubscriberPoint> points, StatKind k) =>
        points.OrderBy(p => p.Day).Select(p => (p.Day, Value(p, k))).Where(x => k == StatKind.Subscribers || x.Item2 > 0).ToList();

    /// <summary>
    /// The sum over several items by day. A day an item has no count for uses its last count before it, so a total does
    /// not dip when one mod was missed on a day.
    /// </summary>
    public static List<(DateOnly Day, long Value)> Total(IEnumerable<IReadOnlyList<SubscriberPoint>> items, StatKind k)
    {
        var series = items.Select(i => Series(i, k)).Where(s => s.Count > 0).ToList();
        var days = series.SelectMany(s => s.Select(x => x.Day)).Distinct().OrderBy(d => d).ToList();
        return days.Select(day => (day, series.Sum(s => s.LastOrDefault(x => x.Day <= day).Value))).ToList();
    }

    /// <summary>"+56 in 7 days" style change over the last <paramref name="days"/> days, or null without data that old.</summary>
    public static long? Change(IReadOnlyList<(DateOnly Day, long Value)> series, int days)
    {
        if (series.Count == 0) return null;
        var last = series[^1];
        var from = series.LastOrDefault(x => x.Day <= last.Day.AddDays(-days));
        return from == default ? null : last.Value - from.Value;
    }

    // ------------------------------------------------------------ comments

    public static string DefaultCommentsFile => Path.Combine(AppSettings.Folder, "comments.json");

    /// <summary>Per item: how many comments Steam counted last time, and how many there were when the user last looked.</summary>
    public sealed record CommentCount(long Known, long Seen);

    static readonly object Gate = new();

    public static Dictionary<string, CommentCount> LoadComments(string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultCommentsFile;
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, CommentCount>>(File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }
    }

    static void Save(Dictionary<string, CommentCount> all, string? path)
    {
        try
        {
            var p = path ?? DefaultCommentsFile;
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(all));
        }
        catch { }
    }

    /// <summary>
    /// Takes new counts from Steam. An item seen for the first time starts with nothing new (its old comments are not
    /// news). Returns the items that have comments the user has not looked at, with how many.
    /// </summary>
    public static Dictionary<string, long> TakeCounts(IReadOnlyDictionary<string, long> counts, string? path = null)
    {
        lock (Gate)
        {
            var all = LoadComments(path);
            foreach (var (id, n) in counts)
                all[id] = all.TryGetValue(id, out var c) ? c with { Known = n, Seen = Math.Min(c.Seen, n) } : new CommentCount(n, n);
            Save(all, path);
            return New(all);
        }
    }

    /// <summary>The items with comments the user has not looked at, with how many.</summary>
    public static Dictionary<string, long> New(IReadOnlyDictionary<string, CommentCount> all) =>
        all.Where(kv => kv.Value.Known > kv.Value.Seen).ToDictionary(kv => kv.Key, kv => kv.Value.Known - kv.Value.Seen);

    /// <summary>The user opened the item's comments: nothing is new any more.</summary>
    public static void MarkSeen(string id, string? path = null)
    {
        lock (Gate)
        {
            var all = LoadComments(path);
            if (!all.TryGetValue(id, out var c) || c.Seen == c.Known) return;
            all[id] = c with { Seen = c.Known };
            Save(all, path);
        }
    }

    public static string CommentsUrl(string id) => $"https://steamcommunity.com/sharedfiles/filedetails/comments/{id}";
}
