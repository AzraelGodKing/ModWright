using System.IO.Compression;
using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Instances: their own data folder, made, launched, moved between PCs and deleted without touching the normal game.</summary>
public sealed class InstanceTests : IDisposable
{
    readonly FakeRimWorld _rw = new();
    readonly RimWorldModule _game = new();
    readonly InstanceStore _store;

    public InstanceTests() => _store = new InstanceStore(Path.Combine(_rw.Root, "instances"));

    public void Dispose() => _rw.Dispose();

    GameInstall Usual()
    {
        Directory.CreateDirectory(Path.Combine(_rw.Data, "Config"));
        File.WriteAllText(Path.Combine(_rw.Data, "Config", "Prefs.xml"), "<PrefsData><volumeGame>0.4</volumeGame></PrefsData>");
        _rw.ModsConfig("ludeon.rimworld", "ludeon.rimworld.royalty", "normal.mod");
        return _rw.Install();
    }

    [Fact]
    public void A_new_instance_has_its_own_mod_list_and_a_copy_of_the_options()
    {
        var usual = Usual();
        var inst = _store.Create(_game, usual, "  Test run ", "", usual.DataFolder, new[] { "ludeon.rimworld", "a.test" });

        Assert.Equal("Test run", inst.Name);
        Assert.True(File.Exists(Path.Combine(inst.DataFolder, "Config", "Prefs.xml")));
        Assert.True(Directory.Exists(Path.Combine(inst.DataFolder, "Saves")));
        var config = ModsConfig.Parse(File.ReadAllText(RimWorldModule.ModsConfigPath(inst.DataFolder)));
        Assert.Equal(new[] { "ludeon.rimworld", "a.test" }, config.Active);
        Assert.Equal(new[] { "ludeon.rimworld.royalty" }, config.KnownExpansions);
        Assert.Contains("normal.mod", _game.ReadActiveMods(usual)!); // the normal game is untouched

        var reloaded = Assert.Single(_store.All(RimWorldModule.SteamAppId));
        Assert.Equal(inst.Folder, reloaded.Folder);
        Assert.Throws<IOException>(() => _store.Create(_game, usual, "test RUN", "", null, null));
        Assert.Empty(_store.All("12345"));
    }

    [Fact]
    public void An_instance_runs_the_usual_install_with_its_own_data_folder()
    {
        var usual = Usual();
        var inst = _store.Create(_game, usual, "Mine", "", null, null);
        var applied = inst.Apply(usual, _game, _rw.Libraries);
        Assert.Equal(usual.GameFolder, applied.GameFolder);
        Assert.Equal(inst.DataFolder, applied.DataFolder);

        inst.GameFolder = Path.Combine(_rw.Root, "gone");
        Assert.Equal(usual.GameFolder, inst.Apply(usual, _game, _rw.Libraries).GameFolder);
    }

    [Fact]
    public void Launching_points_the_game_at_the_instance_and_its_log()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;
        var usual = Usual();
        Assert.Null(_game.Launch(usual, false, null));
        File.WriteAllText(Path.Combine(usual.GameFolder, OperatingSystem.IsWindows() ? "RimWorldWin64.exe" : "RimWorldLinux"), "");
        var inst = _store.Create(_game, usual, "Mine", "", null, null);

        var normal = _game.Launch(usual, false, null)!;
        Assert.Empty(normal.Args);
        Assert.Equal(usual.GameFolder, normal.WorkingDirectory);

        var l = _game.Launch(usual with { DataFolder = inst.DataFolder }, true, inst.LogFile)!;
        Assert.Equal(new[] { "-savedatafolder=" + inst.DataFolder, "-logFile", inst.LogFile }, l.Args);
    }

    [Fact]
    public void Export_and_import_round_trip_with_or_without_saves()
    {
        var usual = Usual();
        var inst = _store.Create(_game, usual, "Shared", "", usual.DataFolder, new[] { "ludeon.rimworld" });
        File.WriteAllText(Path.Combine(inst.DataFolder, "Saves", "Colony.rws"), "<savegame/>");
        Assert.Equal(1, InstanceStore.SaveCount(inst));
        var zip = Path.Combine(_rw.Root, "shared.zip");

        InstanceStore.Export(inst, zip, includeSaves: false);
        var copy = _store.Import(zip, RimWorldModule.SteamAppId);
        Assert.Equal("Shared (2)", copy.Name);
        Assert.Equal(0, InstanceStore.SaveCount(copy));
        Assert.Equal(new[] { "ludeon.rimworld" }, _game.ReadActiveMods(usual with { DataFolder = copy.DataFolder }));

        InstanceStore.Export(inst, zip, includeSaves: true);
        Assert.Equal(1, InstanceStore.SaveCount(_store.Import(zip, RimWorldModule.SteamAppId)));
        Assert.Throws<FormatException>(() => _store.Import(zip, "999"));
    }

    [Fact]
    public void Import_skips_paths_that_leave_the_instance_and_rejects_other_zips()
    {
        var zip = Path.Combine(_rw.Root, "evil.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Add(string name, string text) { using var w = new StreamWriter(z.CreateEntry(name).Open()); w.Write(text); }
            Add("instance.json", "{\"format\":1,\"name\":\"Evil\",\"appId\":\"294100\",\"gameFolder\":\"Z:\\\\nowhere\"}");
            Add("Data/../../escaped.txt", "x");
            Add("Data/Config/Prefs.xml", "<PrefsData/>");
            Add("other.txt", "x");
        }
        var inst = _store.Import(zip, RimWorldModule.SteamAppId);
        Assert.Equal("", inst.GameFolder);
        Assert.True(File.Exists(Path.Combine(inst.DataFolder, "Config", "Prefs.xml")));
        Assert.False(File.Exists(Path.Combine(_store.Folder, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(inst.Folder, "other.txt")));

        var notOne = Path.Combine(_rw.Root, "plain.zip");
        using (var z = ZipFile.Open(notOne, ZipArchiveMode.Create)) z.CreateEntry("readme.txt");
        Assert.Throws<FormatException>(() => _store.Import(notOne, RimWorldModule.SteamAppId));
    }

    [Fact]
    public void Delete_removes_only_instance_folders()
    {
        var usual = Usual();
        var inst = _store.Create(_game, usual, "Gone soon", "", null, null);
        _store.Delete(inst);
        Assert.False(Directory.Exists(inst.Folder));
        Assert.Throws<IOException>(() => _store.Delete(new GameInstance { Name = "x", Folder = _rw.Data }));
        Assert.True(Directory.Exists(_rw.Data));
    }

    [Theory]
    [InlineData("My: list?", "My_ list_")]
    [InlineData("  ...  ", "fallback")]
    [InlineData("con", "_con")]
    [InlineData("LPT1.txt", "_LPT1.txt")]
    public void Names_become_safe_folder_names(string name, string safe) => Assert.Equal(safe, SafeFiles.SafeName(name, "fallback"));
}
