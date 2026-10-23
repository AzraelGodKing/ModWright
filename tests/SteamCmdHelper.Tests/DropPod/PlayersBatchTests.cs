using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

public sealed class LoadOrderEditTests
{
    static readonly string[] Active = { "core", "a", "b", "c", "d" };

    [Theory]
    [InlineData(new[] { "d" }, "a", false, "core,d,a,b,c")]
    [InlineData(new[] { "d" }, "a", true, "core,a,d,b,c")]
    [InlineData(new[] { "b", "a" }, "d", true, "core,c,d,a,b")]
    [InlineData(new[] { "a" }, null, true, "core,b,c,d,a")]
    [InlineData(new[] { "new" }, "b", false, "core,a,new,b,c,d")]
    [InlineData(new[] { "a", "b" }, "b", false, "core,a,b,c,d")]
    public void Moves_mods_before_or_after_the_target(string[] moving, string? target, bool after, string expected) =>
        Assert.Equal(expected, string.Join(",", LoadOrderEdit.Move(Active, moving, target, after)));
}

public sealed class SubscriptionsTests
{
    [Fact]
    public void Waits_longer_for_more_items_up_to_ten_minutes()
    {
        Assert.Equal(45, Subscriptions.WaitSeconds(1));
        Assert.Equal(600, Subscriptions.WaitSeconds(500));
    }

    [Fact]
    public void Describes_what_is_still_downloading()
    {
        Assert.Contains("All of them are downloaded", Subscriptions.Describe(2, 2, 2));
        Assert.Contains("1 is downloaded and ticked; Steam is still downloading the rest", Subscriptions.Describe(3, 3, 1));
    }

    [Fact]
    public void Downloaded_copies_are_mods_folder_mods_with_a_workshop_id_and_no_subscription()
    {
        using var rw = new FakeRimWorld();
        var copy = rw.Local("111", FakeRimWorld.About("Copied", "a.copied", "1.5"));
        File.WriteAllText(Path.Combine(copy, "About", "PublishedFileId.txt"), "111");
        var both = rw.Local("Both", FakeRimWorld.About("Both", "a.both", "1.5"));
        File.WriteAllText(Path.Combine(both, "About", "PublishedFileId.txt"), "222");
        rw.FromWorkshop("222", FakeRimWorld.About("Both", "a.both", "1.5"));
        var own = rw.Local("Own", FakeRimWorld.About("Own", "a.own", "1.5"));
        File.WriteAllText(Path.Combine(own, "About", "PublishedFileId.txt"), "333");
        Directory.CreateDirectory(Path.Combine(own, ".git"));
        rw.Local("Plain", FakeRimWorld.About("Plain", "a.plain", "1.5"));
        var scan = ModLibrary.Scan(new RimWorldModule(), rw.Install());
        var copies = Subscriptions.DownloadedCopies(scan);
        Assert.Equal(new[] { "Copied" }, copies.Select(c => c.DisplayName));
        Assert.Single(Subscriptions.Replaced(copies, new[] { "111", "999" }));
        Assert.Empty(Subscriptions.Replaced(copies, new[] { "999" }));
    }
}

public sealed class ModLibraryCacheTests
{
    [Fact]
    public void A_rescan_reuses_unchanged_mods_and_rereads_changed_ones()
    {
        using var rw = new FakeRimWorld();
        var a = rw.Local("A", FakeRimWorld.About("A", "x.a", "1.5"));
        rw.Local("B", FakeRimWorld.About("B", "x.b", "1.5"));
        var game = new RimWorldModule();
        var first = ModLibrary.Scan(game, rw.Install());
        File.WriteAllText(Path.Combine(a, "About", "About.xml"), FakeRimWorld.About("A renamed", "x.a", "1.5"));
        File.SetLastWriteTimeUtc(Path.Combine(a, "About", "About.xml"), DateTime.UtcNow.AddMinutes(1));
        var second = ModLibrary.Scan(game, rw.Install());
        Assert.Same(first.ById()["x.b"], second.ById()["x.b"]);
        Assert.Equal("A renamed", second.ById()["x.a"].DisplayName);
    }

    [Fact]
    public void Scanning_a_big_library_twice_is_quick()
    {
        using var rw = new FakeRimWorld();
        for (var i = 0; i < 1500; i++)
            rw.FromWorkshop((100000 + i).ToString(), FakeRimWorld.About("Mod " + i, "big.mod" + i, "1.5"));
        var game = new RimWorldModule();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var first = ModLibrary.Scan(game, rw.Install());
        var cold = sw.Elapsed;
        sw.Restart();
        var second = ModLibrary.Scan(game, rw.Install());
        var warm = sw.Elapsed;
        Assert.Equal(1501, first.Mods.Count);
        Assert.Equal(1501, second.Mods.Count);
        Console.WriteLine($"1,500 mods: first scan {cold.TotalMilliseconds:N0} ms, rescan {warm.TotalMilliseconds:N0} ms");
        Assert.True(warm < TimeSpan.FromSeconds(10));
    }
}

public sealed class GitHubModUpdatesTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ghmod_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData("https://github.com/Someone/CoolMod", "Someone/CoolMod")]
    [InlineData("https://github.com/Someone/Cool.Mod.git", "Someone/Cool.Mod")]
    [InlineData("http://www.github.com/a/b/releases", "a/b")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=1", null)]
    [InlineData("", null)]
    public void Reads_the_repository_from_the_link(string url, string? repo) => Assert.Equal(repo, GitHubModUpdates.RepoOf(url));

    [Fact]
    public void Parses_a_release_preferring_a_zip_asset()
    {
        var r = GitHubModUpdates.Parse("""
            {"tag_name":"v1.4.0","html_url":"https://github.com/a/b/releases/tag/v1.4.0","zipball_url":"https://api.github.com/zip",
             "published_at":"2026-10-01T00:00:00Z","assets":[{"name":"notes.txt","browser_download_url":"https://x/notes.txt"},{"name":"CoolMod.zip","browser_download_url":"https://x/CoolMod.zip"}]}
            """)!;
        Assert.Equal("1.4.0", r.Version);
        Assert.Equal("https://x/CoolMod.zip", r.Download);
        Assert.Null(GitHubModUpdates.Parse("""{"tag_name":"v2","prerelease":true,"zipball_url":"z"}"""));
        Assert.Equal("z", GitHubModUpdates.Parse("""{"tag_name":"v2","zipball_url":"z"}""")!.Download);
    }

    [Fact]
    public void Newer_by_version_or_by_date()
    {
        var mod = new ModManifest { Folder = _root, ModVersion = "1.2.0" };
        Assert.True(GitHubModUpdates.IsNewer(mod, new GitHubModRelease("1.3.0", "", "d", null)));
        Assert.False(GitHubModUpdates.IsNewer(mod, new GitHubModRelease("1.2.0", "", "d", null)));
        Directory.CreateDirectory(Path.Combine(_root, "About"));
        File.WriteAllText(Path.Combine(_root, "About", "About.xml"), "<ModMetaData/>");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "About", "About.xml"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var noVersion = new ModManifest { Folder = _root };
        Assert.True(GitHubModUpdates.IsNewer(noVersion, new GitHubModRelease("x", "", "d", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero))));
        Assert.False(GitHubModUpdates.IsNewer(noVersion, new GitHubModRelease("x", "", "d", new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero))));
    }

    static void ModAt(string dir, string id, string extra = "")
    {
        Directory.CreateDirectory(Path.Combine(dir, "About"));
        File.WriteAllText(Path.Combine(dir, "About", "About.xml"), FakeRimWorld.About(id, id, "1.5"));
        if (extra.Length > 0) File.WriteAllText(Path.Combine(dir, extra), extra);
    }

    [Fact]
    public void Finds_the_mod_in_a_release_and_replaces_it_with_a_backup()
    {
        var installed = Path.Combine(_root, "Mods", "CoolMod");
        ModAt(installed, "cool.mod", "old.txt");
        var unpacked = Path.Combine(_root, "unpacked");
        ModAt(Path.Combine(unpacked, "repo-main", "CoolMod"), "cool.mod", "new.txt");
        ModAt(Path.Combine(unpacked, "repo-main", "Other"), "other.mod");
        var mod = RimWorldAbout.ReadFolder(installed, ModSource.Local)!;
        var found = GitHubModUpdates.FindMod(unpacked, mod, d => RimWorldAbout.ReadFolder(d, ModSource.Local));
        Assert.Equal(Path.Combine(unpacked, "repo-main", "CoolMod"), found);

        var backup = GitHubModUpdates.Replace(mod, found!, Path.Combine(_root, "backups"), new DateTime(2026, 10, 23, 12, 0, 0));
        Assert.True(File.Exists(Path.Combine(installed, "new.txt")));
        Assert.False(File.Exists(Path.Combine(installed, "old.txt")));
        Assert.EndsWith("CoolMod 20261023-120000.zip", backup);
        using var zip = System.IO.Compression.ZipFile.OpenRead(backup);
        Assert.Contains(zip.Entries, e => e.FullName == "old.txt");
    }

    [Fact]
    public void Working_copies_are_not_updated()
    {
        var installed = Path.Combine(_root, "Mods", "Dev");
        ModAt(installed, "dev.mod");
        Directory.CreateDirectory(Path.Combine(installed, ".git"));
        var mod = RimWorldAbout.ReadFolder(installed, ModSource.Local)!;
        mod.Url = "https://github.com/a/dev";
        Assert.Empty(GitHubModUpdates.Candidates(new[] { mod }));
        Assert.Throws<IOException>(() => GitHubModUpdates.Replace(mod, installed, Path.Combine(_root, "b")));
    }
}

public sealed class DropPodCliTests : IDisposable
{
    readonly FakeRimWorld _rw = new();
    readonly string _data = Path.Combine(Path.GetTempPath(), "dpcli_" + Guid.NewGuid().ToString("N"));
    readonly AppSettings _settings = new();
    readonly List<string> _started = new();
    bool _running;
    int _saves;

    public void Dispose()
    {
        _rw.Dispose();
        try { Directory.Delete(_data, true); } catch { }
    }

    DropPodCli Cli()
    {
        var game = new RimWorldModule();
        return new DropPodCli(game, () => _rw.Install(), (usual, inst) => inst.Apply(usual, game, _rw.Libraries),
            new ModlistStore(Path.Combine(_data, "modlists")), new InstanceStore(Path.Combine(_data, "instances")), Path.Combine(_data, "history"),
            _settings, () => _saves++, () => _running, (psi, _, inst) => { _started.Add(inst?.Name ?? "normal"); return 0; });
    }

    (int Code, string Text) Run(params string[] args)
    {
        var w = new StringWriter();
        var code = Cli().Run(args, w);
        return (code, w.ToString());
    }

    void SaveList(string name, params string[] ids)
    {
        var scan = ModLibrary.Scan(new RimWorldModule(), _rw.Install());
        new ModlistStore(Path.Combine(_data, "modlists")).Save(Modlist.From(name, RimWorldModule.SteamAppId, "1.5", ids, scan.ById(), ModManifest.KeyOf));
    }

    [Fact]
    public void Switches_to_a_modlist_and_lists_it_as_current()
    {
        _rw.Local("A", FakeRimWorld.About("Mod A", "x.a", "1.5"));
        _rw.Local("B", FakeRimWorld.About("Mod B", "x.b", "1.5"));
        _rw.ModsConfig("ludeon.rimworld");
        SaveList("Heavy", "ludeon.rimworld", "x.b", "x.a");
        var (code, text) = Run("switch", "heavy");
        Assert.Equal(0, code);
        Assert.Contains("Switched the game to Heavy: 3 mods active.", text);
        Assert.Equal(new[] { "ludeon.rimworld", "x.b", "x.a" }, new RimWorldModule().ReadActiveMods(_rw.Install()));
        Assert.Equal(1, _saves);
        Assert.Contains("* Heavy  (3 mods)", Run("lists").Text);
        var active = Run("active").Text;
        Assert.Contains("2. Mod B (x.b)", active);
    }

    [Fact]
    public void Refuses_to_switch_while_the_game_runs_and_names_unknown_lists()
    {
        SaveList("Light", "ludeon.rimworld");
        Assert.Equal(2, Run("switch", "nope").Code);
        _running = true;
        Assert.Equal(1, Run("switch", "Light").Code);
        Assert.Equal(1, Run("play").Code);
        Assert.Empty(_started);
    }

    [Fact]
    public void Problems_exit_with_1_when_there_are_errors()
    {
        _rw.Local("A", "<ModMetaData><name>Mod A</name><packageId>x.a</packageId><supportedVersions><li>1.5</li></supportedVersions>" +
                       "<modDependencies><li><packageId>x.missing</packageId><displayName>Missing</displayName></li></modDependencies></ModMetaData>");
        _rw.ModsConfig("ludeon.rimworld", "x.a");
        var (code, text) = Run("problems");
        Assert.Equal(1, code);
        Assert.Contains("Mod A needs Missing, which is not installed.", text);
        var json = Run("problems", "--json").Text;
        Assert.Contains("\"kind\":\"MissingDependency\"", json);
    }

    [Fact]
    public void Plays_and_rejects_bad_arguments()
    {
        foreach (var exe in new[] { "RimWorldWin64.exe", "RimWorldLinux" }) File.WriteAllText(Path.Combine(_rw.Game, exe), "");
        Assert.Equal(0, Run("play").Code);
        Assert.Equal(new[] { "normal" }, _started);
        Assert.Equal(2, Run("play", "--instance", "Nope").Code);
        Assert.Equal(2, Run("frobnicate").Code);
        Assert.Equal(2, Run("lists", "--bogus").Code);
        Assert.Contains("droppod lists", Run().Text);
    }
}
