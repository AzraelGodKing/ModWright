using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>What goes in a Steam collection of the active mods, and how an existing one is made to match.</summary>
public sealed class SteamCollectionsTests : IDisposable
{
    readonly FakeRimWorld _rw = new();
    readonly RimWorldModule _game = new();

    public void Dispose() => _rw.Dispose();

    [Fact]
    public void Only_active_workshop_mods_go_in_in_load_order()
    {
        _rw.FromWorkshop("2009463077", FakeRimWorld.About("Harmony", "brrainz.harmony", "1.5"));
        _rw.FromWorkshop("111", FakeRimWorld.About("Inactive", "a.inactive", "1.5"));
        _rw.FromWorkshop("222", FakeRimWorld.About("Big Mod", "b.big", "1.5"));
        var uploaded = _rw.Local("Uploaded", FakeRimWorld.About("Uploaded", "c.uploaded", "1.5"));
        File.WriteAllText(Path.Combine(uploaded, "About", "PublishedFileId.txt"), "333");
        _rw.Local("Homemade", FakeRimWorld.About("Homemade", "d.home", "1.5"));
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var active = new[] { "ludeon.rimworld", "brrainz.harmony_steam", "b.big", "c.uploaded", "d.home", "brrainz.harmony", "not.here" };

        var (items, left) = SteamCollections.Contents(scan, active, _game);

        Assert.Equal(new[] { "2009463077", "222", "333" }, items);
        Assert.Equal(new[] { "Homemade", "not.here" }, left);
    }

    [Fact]
    public void Updating_adds_the_new_and_takes_out_the_inactive()
    {
        var (add, remove) = SteamCollections.Plan(new[] { "1", "2", "3" }, new[] { "3", "4", "1", "4" });
        Assert.Equal(new[] { "4" }, add);
        Assert.Equal(new[] { "2" }, remove);

        var (none, gone) = SteamCollections.Plan(new[] { "1", "2" }, new[] { "2", "1" });
        Assert.Empty(none);
        Assert.Empty(gone);
    }

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData("  https://steamcommunity.com/sharedfiles/filedetails/?id=2934567890&searchtext= ", "2934567890")]
    [InlineData("https://steamcommunity.com/workshop/filedetails/?l=german&id=42", "42")]
    [InlineData("", null)]
    [InlineData("000", null)]
    [InlineData("my collection", null)]
    [InlineData("https://steamcommunity.com/id/someone", null)]
    public void Collection_ids_are_read_from_links_or_numbers(string text, string? id) =>
        Assert.Equal(id, SteamCollections.ParseId(text));

    [Fact]
    public void The_child_answer_round_trips_and_garbage_fails()
    {
        var r = SteamCollections.ParseChild("Steam noise\n" + SteamCollections.ChildLine(new CollectionResult { Ok = true, CollectionId = "99", Created = true, Added = 3 }) + "\n");
        Assert.True(r.Ok);
        Assert.Equal("99", r.CollectionId);
        Assert.Contains("Created the collection (added 3, took out 0)", SteamCollections.Describe(r));

        var half = SteamCollections.ParseChild(SteamCollections.ChildLine(new CollectionResult { Ok = false, CollectionId = "99", Error = "Steam refused" }));
        Assert.False(half.Ok);
        Assert.Equal("Collection 99: Steam refused", SteamCollections.Describe(half));

        Assert.False(SteamCollections.ParseChild("crashed").Ok);
        Assert.False(SteamCollections.ParseChild("RESULT {not json").Ok);
    }
}
