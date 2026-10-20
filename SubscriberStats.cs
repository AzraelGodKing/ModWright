using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>One day's counts for an item (favourites, views and lifetime subscribers are 0 in days recorded by older versions).</summary>
public sealed record SubscriberPoint(DateOnly Day, long Subscribers, long Favorites = 0, long Views = 0, long LifetimeSubscribers = 0);

/// <summary>
/// Remembers each item's subscriber count once a day (from the public details the list already reads), so the row can
/// show how a mod has grown: this week and since its last upload. Kept in %LocalAppData%\SteamCmdHelper\stats.json.
/// </summary>
public static class SubscriberStats
{
    public const int KeepDays = 400;
    static readonly object Gate = new();

    public static string DefaultFile => Path.Combine(AppSettings.Folder, "stats.json");

    public static Dictionary<string, List<SubscriberPoint>> Load(string? file = null)
    {
        lock (Gate)
        {
            try
            {
                var p = file ?? DefaultFile;
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, List<SubscriberPoint>>>(File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }
    }

    /// <summary>Adds today's count for each public item (replacing an earlier count from today) and saves.</summary>
    public static Dictionary<string, List<SubscriberPoint>> Record(IEnumerable<WorkshopItem> items, DateOnly today, string? file = null)
    {
        lock (Gate)
        {
            var all = Load(file);
            foreach (var item in items.Where(i => i.Public))
            {
                if (!all.TryGetValue(item.Id, out var points)) all[item.Id] = points = new();
                points.RemoveAll(p => p.Day == today || p.Day < today.AddDays(-KeepDays));
                points.Add(new SubscriberPoint(today, item.Subscribers, item.Favorites, item.Views, item.LifetimeSubscribers));
                points.Sort((a, b) => a.Day.CompareTo(b.Day));
            }
            try
            {
                var p = file ?? DefaultFile;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(all));
                File.Move(p + ".tmp", p, true);
            }
            catch { /* a convenience */ }
            return all;
        }
    }

    /// <summary>The count on or just before <paramref name="day"/>, or null when there is none that old.</summary>
    public static long? At(IReadOnlyList<SubscriberPoint> points, DateOnly day) =>
        points.LastOrDefault(p => p.Day <= day)?.Subscribers;

    /// <summary>"Subscribers: 1,234 (+56 this week, +120 since the last upload on 3 Oct)", or null with no history.</summary>
    public static string? Trend(IReadOnlyList<SubscriberPoint>? points, DateOnly today, DateOnly? lastUpload)
    {
        if (points == null || points.Count == 0) return null;
        var now = points[^1].Subscribers;
        static string Delta(long d) => (d >= 0 ? "+" : "") + d.ToString("N0");
        var parts = new List<string>();
        if (At(points, today.AddDays(-7)) is long week) parts.Add(Delta(now - week) + " this week");
        if (lastUpload is { } u && u < today && At(points, u) is long then) parts.Add($"{Delta(now - then)} since the last upload on {u:d MMM}");
        return $"Subscribers: {now:N0}" + (parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : " (growth shows from tomorrow)");
    }
}
