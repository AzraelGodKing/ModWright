using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Finding RimWorld through Steam's libraries and scanning every mod folder.</summary>
public sealed class ModLibraryTests : IDisposable
{
    readonly FakeRimWorld _rw = new();

    public void Dispose() => _rw.Dispose();

    [Fact]
    public void Library_folders_parse_in_new_and_old_formats()
    {
        Assert.Equal(new[] { "C:\\Steam", "D:\\Games" },
            SteamLibraries.ParseLibraryFolders("\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" } \"1\" { \"path\" \"D:\\\\Games\" } }"));
        Assert.Equal(new[] { "E:\\Lib" },
            SteamLibraries.ParseLibraryFolders("\"LibraryFolders\" { \"TimeNextStatsReport\" \"1\" \"1\" \"E:\\\\Lib\" }"));
        Assert.Empty(SteamLibraries.ParseLibraryFolders("garbage"));
    }

    [Fact]
    public void RimWorld_is_found_in_the_second_library_with_its_version_and_workshop_folder()
    {
        var install = _rw.Install();
        Assert.Equal(_rw.Game, install.GameFolder);
        Assert.Equal("1.5.4104 rev435", install.Version);
        Assert.Equal(_rw.Workshop, install.WorkshopFolder);
        Assert.Equal(_rw.Data, install.DataFolder);
    }

    [Fact]
    public void A_given_game_folder_wins_and_a_missing_one_finds_nothing()
    {
        var game = new RimWorldModule();
        Assert.Equal(_rw.Game, game.Detect(new SteamLibraries(null, Array.Empty<string>()), _rw.Game, _rw.Data)!.GameFolder);
        Assert.Null(game.Detect(new SteamLibraries(null, Array.Empty<string>()), null, null));
        Assert.Null(game.Detect(_rw.Libraries, Path.Combine(_rw.Root, "nope"), null));
    }

    [Fact]
    public void Scan_reads_official_local_and_workshop_mods_and_the_active_list()
    {
        _rw.Local("MyMod", FakeRimWorld.About("My Mod", "Me.MyMod", "1.5"));
        _rw.FromWorkshop("2009463077", FakeRimWorld.About("Harmony", "brrainz.harmony", "1.4", "1.5"));
        _rw.FromWorkshop("111111", FakeRimWorld.About("Copy of my mod", "me.mymod", "1.5"));
        _rw.FromWorkshop("222222", "<ModMetaData><name>Broken & bad</name>");
        Directory.CreateDirectory(Path.Combine(_rw.Workshop, "333333")); // an empty download: not a mod
        _rw.ModsConfig("ludeon.rimworld", "brrainz.harmony", "me.mymod");

        var scan = ModLibrary.Scan(new RimWorldModule(), _rw.Install());
        Assert.Equal(new[] { "Broken & bad", "Copy of my mod", "Core", "Harmony", "My Mod" }, scan.Mods.Select(m => m.DisplayName));
        Assert.Equal(ModSource.Official, scan.Mods.Single(m => m.Name == "Core").Source);
        Assert.Equal("2009463077", scan.Mods.Single(m => m.Name == "Harmony").WorkshopId);
        Assert.Contains(scan.Mods.Single(m => m.Name == "Broken & bad").Issues, i => i.Contains("not valid XML"));
        Assert.Equal(new[] { "ludeon.rimworld", "brrainz.harmony", "me.mymod" }, scan.Active);

        var dupe = Assert.Single(scan.Duplicates());
        Assert.Equal("me.mymod", dupe.Key);
        Assert.Equal(ModSource.Local, scan.ById()["me.mymod"].Source); // the Mods folder copy wins
        Assert.Empty(scan.Notes);
    }

    [Fact]
    public void A_broken_mods_config_is_a_note_not_a_crash()
    {
        File.WriteAllText(RimWorldModule.ModsConfigPath(_rw.Data), "<ModsConfigData><activeMods><li>a");
        var scan = ModLibrary.Scan(new RimWorldModule(), _rw.Install());
        Assert.Null(scan.Active);
        Assert.Contains(scan.Notes, n => n.Contains("active mod list"));
    }

    [Fact]
    public void No_mods_config_yet_means_no_active_list()
    {
        Assert.Null(ModLibrary.Scan(new RimWorldModule(), _rw.Install()).Active);
    }

    [Fact]
    public void Mods_config_keeps_order_and_expansions()
    {
        var c = ModsConfig.Parse("<ModsConfigData><version>1.5</version><activeMods><li>b.b</li><li>a.a</li><li> </li></activeMods><knownExpansions><li>ludeon.rimworld.royalty</li></knownExpansions></ModsConfigData>");
        Assert.Equal(new[] { "b.b", "a.a" }, c.Active);
        Assert.Equal("1.5", c.Version);
        Assert.Single(c.KnownExpansions);
    }

    [Fact]
    public void A_thousand_mods_scan_quickly()
    {
        for (var i = 0; i < 1000; i++) _rw.FromWorkshop((100000 + i).ToString(), FakeRimWorld.About("Mod " + i, "Bulk.Mod" + i, "1.5"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var scan = ModLibrary.Scan(new RimWorldModule(), _rw.Install());
        Assert.Equal(1001, scan.Mods.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), sw.Elapsed.ToString());
    }
}
