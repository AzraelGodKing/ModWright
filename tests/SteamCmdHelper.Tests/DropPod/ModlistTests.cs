using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Modlists: saving, files, RimWorld's .rml, share codes and writing ModsConfig.xml.</summary>
public sealed class ModlistTests : IDisposable
{
    readonly FakeRimWorld _rw = new();

    public void Dispose() => _rw.Dispose();

    static Modlist Sample() => new()
    {
        Name = "Medieval run", AppId = "294100", GameVersion = "1.5.4104 rev435",
        Mods = { new("ludeon.rimworld"), new("brrainz.harmony", "Harmony", "2009463077"), new("Kat.Tweaks", "Kat's tweaks") },
    };

    [Fact]
    public void Json_round_trips()
    {
        var back = Modlist.Parse(Sample().ToJson());
        Assert.Equal("Medieval run", back.Name);
        Assert.Equal(Sample().Mods, back.Mods);
    }

    [Fact]
    public void Share_codes_round_trip_and_survive_chat_line_breaks()
    {
        var code = Sample().ToShareCode();
        Assert.StartsWith("DROPPOD1:", code);
        var back = Modlist.FromShareCode(code.Insert(20, "\n ").Insert(40, " "));
        Assert.Equal("Medieval run", back.Name);
        Assert.Equal(new[] { "ludeon.rimworld", "brrainz.harmony", "Kat.Tweaks" }, back.Mods.Select(m => m.Id));
        Assert.Equal("2009463077", back.Mods[1].WorkshopId);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("DROPPOD1:!!!notbase64")]
    [InlineData("DROPPOD1:AAAA")]
    public void Bad_share_codes_say_so(string code) => Assert.Throws<FormatException>(() => Modlist.FromShareCode(code));

    [Fact]
    public void RimWorlds_own_saved_modlist_imports()
    {
        const string rml = """
            <?xml version="1.0" encoding="utf-8"?>
            <savedModList>
              <meta><gameVersion>1.5.4104 rev435</gameVersion><modIds><li>ludeon.rimworld</li><li>brrainz.harmony</li></modIds>
                <modSteamIds><li>0</li><li>2009463077</li></modSteamIds><modNames><li>Core</li><li>Harmony</li></modNames></meta>
            </savedModList>
            """;
        var m = Modlist.Parse(rml, "From game");
        Assert.Equal(("From game", "1.5.4104 rev435"), (m.Name, m.GameVersion));
        Assert.Equal(new ModlistEntry("ludeon.rimworld", "Core", null), m.Mods[0]);
        Assert.Equal(new ModlistEntry("brrainz.harmony", "Harmony", "2009463077"), m.Mods[1]);
        Assert.Throws<FormatException>(() => Modlist.Parse("<savedModList/>"));
        Assert.Throws<FormatException>(() => Modlist.Parse("not a list"));
        Assert.Throws<FormatException>(() => Modlist.Parse("{ \"format\": 99, \"name\": \"x\" }"));
    }

    [Fact]
    public void The_store_saves_lists_per_game_with_safe_file_names()
    {
        var store = new ModlistStore(Path.Combine(_rw.Root, "lists"));
        store.Save(Sample());
        store.Save(new Modlist { Name = "../../evil:name", AppId = "294100" });
        store.Save(new Modlist { Name = "Other game", AppId = "1" });
        Assert.Equal(new[] { "../../evil:name", "Medieval run" }, store.All("294100").Select(m => m.Name));
        Assert.All(Directory.GetFiles(store.Folder), f => Assert.Equal(store.Folder, Path.GetDirectoryName(f)));
        store.Delete("Medieval run");
        Assert.Single(store.All("294100"));
    }

    [Fact]
    public void Matching_a_list_finds_installed_and_missing_mods()
    {
        _rw.FromWorkshop("2009463077", FakeRimWorld.About("Harmony", "brrainz.harmony", "1.5"));
        var scan = ModLibrary.Scan(new RimWorldModule(), _rw.Install());
        var (installed, missing) = Sample().Match(scan, new RimWorldModule());
        Assert.Equal(new[] { "ludeon.rimworld", "brrainz.harmony" }, installed);
        Assert.Equal("Kat.Tweaks", Assert.Single(missing).Id);
    }

    [Fact]
    public void Writing_the_active_list_keeps_the_rest_of_mods_config_and_a_backup()
    {
        _rw.ModsConfig("ludeon.rimworld");
        var game = new RimWorldModule();
        game.WriteActiveMods(_rw.Install(), new[] { "Ludeon.RimWorld", "brrainz.harmony", "brrainz.harmony" });
        var c = ModsConfig.Parse(File.ReadAllText(RimWorldModule.ModsConfigPath(_rw.Data)));
        Assert.Equal(new[] { "ludeon.rimworld", "brrainz.harmony" }, c.Active);
        Assert.Equal("1.5.4104 rev435", c.Version);
        Assert.Equal(new[] { "ludeon.rimworld.royalty" }, c.KnownExpansions);
        Assert.Single(Directory.GetFiles(Path.Combine(_rw.Data, "Config"), "ModsConfig.xml.*.bak"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_rw.Data, "Config"), "*.tmp"));
    }

    [Fact]
    public void Old_backups_are_pruned()
    {
        var file = Path.Combine(_rw.Root, "f.xml");
        for (var i = 0; i < 8; i++) SafeFiles.WriteWithBackup(file, "v" + i, keep: 3, now: new DateTime(2026, 1, 1, 0, 0, i));
        Assert.Equal(3, Directory.GetFiles(_rw.Root, "f.xml.*.bak").Length);
        Assert.Equal("v7", File.ReadAllText(file));
    }

    [Fact]
    public void A_list_from_the_active_mods_fills_names_and_workshop_ids()
    {
        _rw.FromWorkshop("2009463077", FakeRimWorld.About("Harmony", "brrainz.harmony", "1.5"));
        var scan = ModLibrary.Scan(new RimWorldModule(), _rw.Install());
        var game = new RimWorldModule();
        var m = Modlist.From("Now", "294100", "1.5", new[] { "ludeon.rimworld", "brrainz.harmony_steam", "gone.mod" }, scan.ById(), game.ActiveKey);
        Assert.Equal(new ModlistEntry("brrainz.harmony", "Harmony", "2009463077"), m.Mods[1]);
        Assert.Equal(new ModlistEntry("gone.mod"), m.Mods[2]);
    }
}
