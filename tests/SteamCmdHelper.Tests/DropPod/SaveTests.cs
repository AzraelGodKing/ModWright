using System.IO.Compression;
using System.Text;
using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Reading RimWorld save headers (plain, gzip, huge, cut off, broken) and comparing them with the active mods.</summary>
public sealed class SaveTests : IDisposable
{
    readonly FakeRimWorld _rw = new();
    readonly RimWorldModule _game = new();

    public void Dispose() => _rw.Dispose();

    const string Meta = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<savegame>\n\t<meta>\n\t\t<gameVersion>1.5.4104 rev435</gameVersion>\n"
        + "\t\t<modIds>\n\t\t\t<li>brrainz.harmony</li>\n\t\t\t<li>ludeon.rimworld</li>\n\t\t\t<li>a.mod</li>\n\t\t\t<li>gone.mod</li>\n\t\t</modIds>\n"
        + "\t\t<modSteamIds><li>2009463077</li><li>0</li><li>111</li><li>222</li></modSteamIds>\n"
        + "\t\t<modNames><li>Harmony</li><li>Core</li><li>A Mod</li><li>Gone Mod</li></modNames>\n\t</meta>\n";

    string SavePath(string name) => Path.Combine(_rw.Data, "Saves", name + ".rws");

    string WriteSave(string name, string text, bool gzip = false)
    {
        var path = SavePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!gzip) File.WriteAllText(path, text);
        else using (var z = new GZipStream(File.Create(path), CompressionLevel.Fastest)) z.Write(Encoding.UTF8.GetBytes(text));
        return path;
    }

    [Fact]
    public void The_header_is_read_from_plain_and_gzip_saves()
    {
        foreach (var gz in new[] { false, true })
        {
            var s = _game.ReadSave(WriteSave("Colony" + gz, Meta + "<game><map/></game></savegame>", gz));
            Assert.Null(s.Error);
            Assert.Equal("1.5.4104 rev435", s.GameVersion);
            Assert.Equal(new[] { "brrainz.harmony", "ludeon.rimworld", "a.mod", "gone.mod" }, s.Mods.Mods.Select(m => m.Id));
            Assert.Equal(new string?[] { "2009463077", null, "111", "222" }, s.Mods.Mods.Select(m => m.WorkshopId));
            Assert.Equal("Gone Mod", s.Mods.Mods[3].Name);
        }
    }

    [Fact]
    public void Compact_xml_and_saves_cut_off_after_the_header_still_read()
    {
        var compact = "<savegame><meta><gameVersion>1.4.3901</gameVersion><modIds><li>ludeon.rimworld</li><li/><li>b.mod</li></modIds></meta><game><thin";
        var s = _game.ReadSave(WriteSave("Compact", compact));
        Assert.Null(s.Error);
        Assert.Equal("1.4.3901", s.GameVersion);
        Assert.Equal(new[] { "ludeon.rimworld", "b.mod" }, s.Mods.Mods.Select(m => m.Id));
    }

    [Fact]
    public void A_huge_save_is_read_without_reading_the_whole_file()
    {
        var path = WriteSave("Huge", Meta + "<game>");
        using (var f = new FileStream(path, FileMode.Append))
        {
            var junk = Encoding.UTF8.GetBytes("<thing><def>Steel</def><stackCount>75</stackCount></thing>\n");
            for (var i = 0; i < 200_000; i++) f.Write(junk); // about 12 MB of map, never read
        }
        var started = DateTime.UtcNow;
        var s = _game.ReadSave(path);
        Assert.Null(s.Error);
        Assert.Equal(4, s.Mods.Mods.Count);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<savegame><game><map/></game></savegame>")]
    [InlineData("<savegame><meta><modIds><li>a.b</li>")] // cut off inside the header: the list may be incomplete, so it isn't used
    public void Broken_saves_say_so_instead_of_throwing(string text)
    {
        var s = _game.ReadSave(WriteSave("Broken", text));
        Assert.NotNull(s.Error);
        Assert.Empty(s.Mods.Mods);
    }

    [Fact]
    public void Saves_are_listed_newest_first_and_compared_with_the_active_mods()
    {
        _rw.FromWorkshop("2009463077", FakeRimWorld.About("Harmony", "brrainz.harmony", "1.5"));
        _rw.Local("A", FakeRimWorld.About("A Mod", "a.mod", "1.5"));
        _rw.Local("Extra", FakeRimWorld.About("Extra", "c.extra", "1.5"));
        var old = WriteSave("Old", Meta);
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-2));
        WriteSave("New", Meta.Replace("<li>gone.mod</li>", "").Replace("<li>222</li>", ""));
        var scan = ModLibrary.Scan(_game, _rw.Install());

        var saves = Saves.List(_game, scan.Install);
        Assert.Equal(new[] { "New", "Old" }, saves.Select(s => s.Name));

        var d = Saves.Compare(saves[1], new[] { "ludeon.rimworld", "brrainz.harmony_steam", "c.extra" }, scan, _game);
        Assert.Equal(new[] { "gone.mod" }, d.Missing.Select(m => m.Id));
        Assert.Equal("222", d.Missing[0].WorkshopId);
        Assert.Equal(new[] { "a.mod" }, d.Inactive.Select(m => m.Id));
        Assert.Equal(new[] { "c.extra" }, d.Extra);
        Assert.True(d.OrderDiffers);
        Assert.False(d.Matches);

        Assert.True(Saves.Compare(saves[0], new[] { "brrainz.harmony", "ludeon.rimworld", "a.mod" }, scan, _game).Matches);
        Assert.Equal(new[] { "brrainz.harmony", "ludeon.rimworld", "a.mod" }, saves[1].Mods.Match(scan, _game).Installed);
    }
}
