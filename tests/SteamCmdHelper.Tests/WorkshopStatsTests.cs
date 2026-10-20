using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Stats over time and new comments.</summary>
public sealed class WorkshopStatsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_stats_" + Guid.NewGuid().ToString("N"));
    public WorkshopStatsTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    static readonly DateOnly D = new(2026, 10, 1);

    [Fact]
    public void Public_details_carry_favourites_views_and_lifetime_subscribers()
    {
        var items = WorkshopStatus.Parse("""
            {"response":{"publishedfiledetails":[{"publishedfileid":"1","result":1,"title":"A","subscriptions":10,"favorited":"4","lifetime_subscriptions":25,"views":300}]}}
            """);
        var a = items["1"];
        Assert.Equal((10, 4, 25, 300), (a.Subscribers, a.Favorites, a.LifetimeSubscribers, a.Views));
        Assert.Contains("4 favourites", a.Summary);

        var file = Path.Combine(_root, "stats.json");
        var kept = SubscriberStats.Record(new[] { a }, D, file)["1"].Single();
        Assert.Equal(new SubscriberPoint(D, 10, 4, 300, 25), kept);
    }

    [Fact]
    public void Old_days_without_favourites_are_left_out_of_that_chart()
    {
        var points = new[] { new SubscriberPoint(D, 5), new SubscriberPoint(D.AddDays(1), 6, Favorites: 2) };
        Assert.Equal(2, WorkshopStats.Series(points, StatKind.Subscribers).Count);
        Assert.Equal(new[] { (D.AddDays(1), 2L) }, WorkshopStats.Series(points, StatKind.Favorites));
    }

    [Fact]
    public void Totals_carry_a_missed_day_forward()
    {
        var a = new[] { new SubscriberPoint(D, 10), new SubscriberPoint(D.AddDays(1), 12), new SubscriberPoint(D.AddDays(2), 15) };
        var b = new[] { new SubscriberPoint(D, 100), new SubscriberPoint(D.AddDays(2), 110) }; // not read on day 2
        var total = WorkshopStats.Total(new IReadOnlyList<SubscriberPoint>[] { a, b }, StatKind.Subscribers);
        Assert.Equal(new long[] { 110, 112, 125 }, total.Select(t => t.Value));
        Assert.Equal(15, WorkshopStats.Change(total, 2));
        Assert.Null(WorkshopStats.Change(total, 30));
    }

    [Fact]
    public void Only_comments_after_the_first_count_are_new_until_seen()
    {
        var file = Path.Combine(_root, "comments.json");
        Assert.Empty(WorkshopStats.TakeCounts(new Dictionary<string, long> { ["1"] = 40 }, file)); // old comments are not news

        var fresh = WorkshopStats.TakeCounts(new Dictionary<string, long> { ["1"] = 43, ["2"] = 0 }, file);
        Assert.Equal(3, fresh["1"]);

        WorkshopStats.MarkSeen("1", file);
        Assert.Empty(WorkshopStats.New(WorkshopStats.LoadComments(file)));

        // Comments deleted on Steam don't leave a negative count behind.
        Assert.Empty(WorkshopStats.TakeCounts(new Dictionary<string, long> { ["1"] = 30 }, file));
        Assert.Equal(2, WorkshopStats.TakeCounts(new Dictionary<string, long> { ["1"] = 32 }, file)["1"]);
    }
}
