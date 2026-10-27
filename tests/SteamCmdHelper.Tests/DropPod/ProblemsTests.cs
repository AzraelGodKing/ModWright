using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Problem detection over a scanned library, and Workshop updates and downloads.</summary>
public sealed class ProblemsTests : IDisposable
{
    readonly FakeRimWorld _rw = new();
    readonly RimWorldModule _game = new();

    public void Dispose() => _rw.Dispose();

    static string About(string name, string id, string extra = "", string versions = "<li>1.5</li>") =>
        $"<ModMetaData><name>{name}</name><packageId>{id}</packageId><supportedVersions>{versions}</supportedVersions>{extra}</ModMetaData>";

    LibraryScan Scan() => ModLibrary.Scan(_game, _rw.Install());

    [Fact]
    public void Every_kind_of_problem_is_found()
    {
        _rw.FromWorkshop("2009463077", About("Harmony", "brrainz.harmony"));
        _rw.Local("Needy", About("Needy", "a.needy", "<modDependencies>"
            + "<li><packageId>brrainz.harmony</packageId></li>"
            + "<li><packageId>gone.lib</packageId><displayName>Gone Lib</displayName><steamWorkshopUrl>steam://url/CommunityFilePage/7654321</steamWorkshopUrl></li>"
            + "<li><packageId>b.inactive</packageId></li></modDependencies><incompatibleWith><li>c.enemy</li></incompatibleWith>"));
        _rw.Local("Inactive", About("Inactive", "b.inactive"));
        _rw.Local("Enemy", About("Enemy", "c.enemy", "<loadBefore><li>Ludeon.RimWorld</li></loadBefore>"));
        _rw.Local("Old", About("Old", "d.old", versions: "<li>1.3</li>"));
        _rw.FromWorkshop("555", About("Old copy", "d.old", versions: "<li>1.3</li>"));
        var active = new[] { "ludeon.rimworld", "a.needy", "brrainz.harmony_steam", "c.enemy", "d.old", "not.here" };

        var p = Problems.Find(Scan(), active, _game);

        Assert.Contains(p, x => x.Kind == ProblemKind.MissingDependency && x.Other == "gone.lib" && x.WorkshopId == "7654321" && x.Message.Contains("Gone Lib"));
        Assert.Contains(p, x => x.Kind == ProblemKind.MissingDependency && x.Other == "b.inactive" && x.Message.Contains("not active"));
        Assert.Contains(p, x => x.Kind == ProblemKind.Order && x.Mod == "a.needy" && x.Other == "brrainz.harmony");
        Assert.Contains(p, x => x.Kind == ProblemKind.Order && x.Mod == "c.enemy" && x.Message.Contains("before Core"));
        Assert.Single(p, x => x.Kind == ProblemKind.Incompatible);
        Assert.Contains(p, x => x.Kind == ProblemKind.WrongVersion && x.Mod == "d.old");
        Assert.Contains(p, x => x.Kind == ProblemKind.Duplicate && x.Mod == "d.old");
        Assert.Contains(p, x => x.Kind == ProblemKind.NotInstalled && x.Mod == "not.here");
        Assert.Equal(ProblemLevel.Error, p[0].Level); // errors first
        Assert.Equal(new[] { "7654321" }, Problems.DownloadableFixes(p));
    }

    [Fact]
    public void Installed_twice_can_be_ignored_for_one_mod()
    {
        // A mod author's own mod: the working copy in Mods and their Workshop subscription.
        _rw.Local("Mine", About("Mine", "me.mine"));
        _rw.FromWorkshop("111", About("Mine", "me.mine"));
        _rw.Local("Other", About("Other", "x.other"));
        _rw.FromWorkshop("222", About("Other", "x.other"));
        var scan = Scan();
        var active = new[] { "ludeon.rimworld", "me.mine", "x.other" };

        var p = Problems.Find(scan, active, _game, ignoreDuplicates: new[] { "me.mine" });

        Assert.DoesNotContain(p, x => x.Kind == ProblemKind.Duplicate && x.Mod == "me.mine");
        Assert.Contains(p, x => x.Kind == ProblemKind.Duplicate && x.Mod == "x.other");
        Assert.Equal("x.other", Assert.Single(scan.Duplicates(new[] { "me.mine" })).Key);
        Assert.Equal(2, scan.Duplicates().Count);
    }

    [Fact]
    public void A_clean_list_has_no_problems_and_rules_notes_show()
    {
        _rw.FromWorkshop("2009463077", About("Harmony", "brrainz.harmony", "<loadBefore><li>Ludeon.RimWorld</li></loadBefore>"));
        var rules = SortRules.Parse("""{ "rules": { "brrainz.harmony": { "note": "Keep it first." } } }""");
        var p = Problems.Find(Scan(), new[] { "brrainz.harmony", "ludeon.rimworld" }, _game, rules);
        Assert.Equal(ProblemKind.RulesNote, Assert.Single(p).Kind);
    }

    [Fact]
    public void Inactive_dependencies_are_pulled_in_before_the_mod_that_needs_them()
    {
        _rw.Local("A", About("A", "a.a", "<modDependencies><li><packageId>b.b</packageId></li></modDependencies>"));
        _rw.Local("B", About("B", "b.b", "<modDependencies><li><packageId>c.c</packageId></li><li><packageId>nowhere.x</packageId></li></modDependencies>"));
        _rw.Local("C", About("C", "c.c"));
        Assert.Equal(new[] { "ludeon.rimworld", "c.c", "b.b", "a.a" }, Problems.WithInactiveDependencies(Scan(), new[] { "ludeon.rimworld", "a.a" }, _game));
    }

    [Fact]
    public void Steam_client_install_times_and_outdated_mods()
    {
        File.WriteAllText(Path.Combine(_rw.Library, "steamapps", "workshop", "appworkshop_294100.acf"),
            "\"AppWorkshop\" { \"appid\" \"294100\" \"WorkshopItemsInstalled\" { \"2009463077\" { \"size\" \"1\" \"timeupdated\" \"1700000000\" } \"111\" { \"timeupdated\" \"1700000000\" } } }");
        _rw.FromWorkshop("2009463077", About("Harmony", "brrainz.harmony"));
        _rw.FromWorkshop("111", About("Fresh", "f.fresh"));
        var local = WorkshopUpdates.SteamClientTimes(_rw.Install().WorkshopFolder, "294100");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), local["2009463077"]);
        var remote = new Dictionary<string, WorkshopItem>
        {
            ["2009463077"] = new("2009463077", true, Updated: DateTimeOffset.FromUnixTimeSeconds(1700090000)),
            ["111"] = new("111", true, Updated: DateTimeOffset.FromUnixTimeSeconds(1700000030)),
        };
        var u = Assert.Single(WorkshopUpdates.Outdated(Scan().Mods, remote, local));
        Assert.Equal("Harmony", u.Mod.Name);
        Assert.Empty(WorkshopUpdates.SteamClientTimes(null, "294100"));
    }

    [Fact]
    public async Task Installing_from_steamcmd_replaces_the_old_copy_only_when_the_new_one_is_complete()
    {
        var steamDir = Path.Combine(_rw.Root, "steamcmd");
        var downloaded = Path.Combine(steamDir, "steamapps", "workshop", "content", "294100", "777");
        Directory.CreateDirectory(Path.Combine(downloaded, "About"));
        File.WriteAllText(Path.Combine(downloaded, "About", "About.xml"), About("New", "n.new"));
        var mods = Path.Combine(_rw.Game, "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "777"));
        File.WriteAllText(Path.Combine(mods, "777", "old.txt"), "old");
        var record = new DownloadRecord(Path.Combine(_rw.Root, "downloads.json"));
        Task<DownloadResult> Ok(string a, string b, string c, string d, Action<string>? e, CancellationToken f) => Task.FromResult(new DownloadResult(true, "Downloaded", downloaded));

        var dest = await WorkshopUpdates.InstallAsync(Path.Combine(steamDir, "steamcmd.exe"), "me", "294100", "777", mods, record, null, CancellationToken.None, Ok);

        Assert.False(File.Exists(Path.Combine(dest, "old.txt")));
        Assert.Equal("777", File.ReadAllText(Path.Combine(dest, "About", "PublishedFileId.txt")));
        Assert.False(Directory.Exists(downloaded)); // SteamCMD's copy is cleaned up
        Assert.True(new DownloadRecord(Path.Combine(_rw.Root, "downloads.json")).Times.ContainsKey("777"));

        Task<DownloadResult> Fail(string a, string b, string c, string d, Action<string>? e, CancellationToken f) => Task.FromResult(new DownloadResult(false, "Steam refused the login", null));
        var ex = await Assert.ThrowsAsync<IOException>(() => WorkshopUpdates.InstallAsync("x", "me", "294100", "777", mods, record, null, CancellationToken.None, Fail));
        Assert.Contains("refused", ex.Message);
        Assert.True(Directory.Exists(dest)); // a failed download leaves the installed copy alone
    }
}
