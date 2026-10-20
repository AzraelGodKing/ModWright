using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The details panel beside the mod list: when it fits, how wide it is, and what it says.</summary>
public class ModDetailsTests
{
    static ModInfo Mod(string? id = "2000000001", string version = "1.4.0") =>
        new() { Name = "Better Storage", Folder = "BetterStorage", Version = version, WorkshopId = id };

    [Theory]
    [InlineData(1800, true)]
    [InlineData(1120, true)]
    [InlineData(1119, false)]
    [InlineData(950, false)]
    public void The_panel_shows_only_when_the_columns_keep_their_room(int area, bool fits)
    {
        Assert.Equal(fits, ModDetails.Fits(area, 680, ModDetails.MinStatusWidth, ModDetails.MinPanelWidth));
    }

    [Fact]
    public void The_width_starts_at_about_a_third_and_never_squeezes_the_columns()
    {
        Assert.Equal(648, ModDetails.Width(0, 1800, 680, 160, 280));
        Assert.Equal(500, ModDetails.Width(500, 1800, 680, 160, 280));
        Assert.Equal(960, ModDetails.Width(1500, 1800, 680, 160, 280)); // left wider than the list can give now
        Assert.Equal(280, ModDetails.Width(100, 1800, 680, 160, 280));
    }

    [Fact]
    public void A_mod_on_Steam_lists_its_versions_numbers_and_last_upload()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var live = new WorkshopItem("2000000001", true, "Better Storage", now.AddDays(-3), Visibility: 0, Size: 3_250_000, Subscribers: 1234, Favorites: 56, LifetimeSubscribers: 2000, Views: 9000);
        var last = new UploadEntry(now.AddDays(-3), "2000000001", "Better Storage", "1.3.9", "fixes");
        var facts = ModDetails.Facts(Mod(), "1.3.9", live, last, new[] { "Mod", "1.6" }, now).ToDictionary(f => f.Label, f => f.Value);
        Assert.Equal("1.4.0", facts["To upload"]);
        Assert.Equal("1.3.9", facts["On Steam"]);
        Assert.Contains("3 days ago", facts["Updated"]);
        Assert.Equal($"{1234:N0} ({2000:N0} all-time)", facts["Subscribers"]);
        Assert.Equal($"{3.1:0.#} MB", facts["Size"]);
        Assert.Equal("Public", facts["Visibility"]);
        Assert.Equal("2000000001", facts["Workshop ID"]);
        Assert.StartsWith("1.3.9 on ", facts["Last upload"]);
        Assert.Equal("Mod, 1.6", facts["Tags"]);
    }

    [Fact]
    public void A_new_mod_says_uploading_creates_it_and_leaves_out_Steam_numbers()
    {
        var facts = ModDetails.Facts(Mod(id: null), null, null, null, Array.Empty<string>()).ToDictionary(f => f.Label, f => f.Value);
        Assert.Equal("not yet; uploading creates it", facts["On Steam"]);
        Assert.False(facts.ContainsKey("Subscribers"));
        Assert.False(facts.ContainsKey("Workshop ID"));
        Assert.False(facts.ContainsKey("Tags"));
    }

    [Fact]
    public void A_version_still_loading_is_not_shown_as_a_version()
    {
        var facts = ModDetails.Facts(Mod(), "...", null, null, Array.Empty<string>()).ToDictionary(f => f.Label, f => f.Value);
        Assert.Equal("not known yet", facts["On Steam"]);
    }

    [Fact]
    public void The_description_preview_drops_BBCode_and_is_cut_at_a_word()
    {
        var text = ModDetails.PlainText("[h1]Better Storage[/h1]\r\n\r\n\r\n[b]Shelves[/b] hold more.\n[list][*]Twice the stack[*]No lag[/list]\n[url=https://x.y]Source[/url][img]https://x.y/a.png[/img]", 1000);
        Assert.Equal("Better Storage\n\nShelves hold more.\n\n• Twice the stack\n• No lag\nSource", text);
        var cut = ModDetails.PlainText("one two three four five six seven", 15);
        Assert.Equal("one two three…", cut);
    }

    [Fact]
    public void The_summary_counts_each_status_with_the_ones_that_need_something_first()
    {
        var s = ModDetails.Summary(new[] { RowState.UpToDate, RowState.UpToDate, RowState.New, RowState.Fix });
        Assert.Equal("4 mods: 1 fix, 1 new, 2 up to date", s);
        Assert.Equal("No mods in this folder yet.", ModDetails.Summary(Array.Empty<RowState>()));
    }
}
