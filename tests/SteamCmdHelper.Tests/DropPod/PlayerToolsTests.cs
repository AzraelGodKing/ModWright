using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>The 1.8.0 Drop Pod tools: history and undo, why a mod is where it is, bisecting, collections, play-together,
/// notes, cleanup, mod settings, save backups, launch options, bug reports, load times, update notes and freezing.</summary>
public sealed class PlayerToolsTests : IDisposable
{
    readonly FakeRimWorld _rw = new();
    readonly RimWorldModule _game = new();
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "droppod_tools_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _rw.Dispose();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    static ModManifest Mod(string id, string? needs = null, string? after = null) => RimWorldAbout.Parse(
        "<ModMetaData>" + $"<name>{id}</name><packageId>{id}</packageId>"
        + (after != null ? "<loadAfter>" + string.Concat(after.Split(',').Select(a => $"<li>{a}</li>")) + "</loadAfter>" : "")
        + (needs != null ? "<modDependencies>" + string.Concat(needs.Split(',').Select(a => $"<li><packageId>{a}</packageId></li>")) + "</modDependencies>" : "")
        + "</ModMetaData>", "/mods/" + id);

    // ------------------------------------------------------------ history and undo

    [Fact]
    public void History_keeps_a_snapshot_per_change_newest_first_and_prunes_old_ones()
    {
        var h = new OrderHistory(_tmp, keep: 3);
        var t = new DateTime(2026, 10, 21, 12, 0, 0);
        Assert.NotNull(h.Record(new[] { "a", "b" }, "Save to game", t));
        Assert.Null(h.Record(new[] { "a", "b" }, "Save to game", t.AddMinutes(1))); // same as the newest: nothing new
        h.Record(new[] { "b", "a" }, "Sorted", t.AddMinutes(2));
        h.Record(new[] { "b", "a", "c" }, "Switched", t.AddMinutes(3));
        h.Record(new[] { "c" }, "Switched", t.AddMinutes(4));
        var all = h.All();
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { "c" }, all[0].Active);
        Assert.Equal("Sorted", all[2].Label);
    }

    [Fact]
    public void Compare_counts_added_removed_and_the_fewest_moves()
    {
        Assert.True(OrderHistory.Compare(new[] { "a", "b" }, new[] { "a", "b" }).None);
        var c = OrderHistory.Compare(new[] { "a", "b", "c", "d" }, new[] { "b", "c", "d", "a", "e" });
        Assert.Equal((1, 0, 1), (c.Added, c.Removed, c.Moved));
        Assert.Equal("1 added, 1 moved", c.ToString());
        Assert.Equal(1, OrderHistory.Compare(new[] { "a", "x" }, new[] { "a" }).Removed);
    }

    [Fact]
    public void Undo_and_redo_step_through_edits_and_a_new_edit_drops_redo()
    {
        var u = new EditUndo();
        var list = new List<string> { "a" };
        u.Push(list); list = new() { "a", "b" };
        u.Push(list); list = new() { "b", "a" };
        list = u.Undo(list)!;
        Assert.Equal(new[] { "a", "b" }, list);
        list = u.Undo(list)!;
        Assert.Equal(new[] { "a" }, list);
        Assert.Null(u.Undo(list));
        list = u.Redo(list)!;
        Assert.Equal(new[] { "a", "b" }, list);
        u.Push(list);
        Assert.False(u.CanRedo);
    }

    // ------------------------------------------------------------ why is this mod here

    [Fact]
    public void Explain_names_the_rules_around_a_mod_and_marks_broken_ones()
    {
        var mods = new[] { Mod("ludeon.rimworld"), Mod("b.patch", needs: "a.core"), Mod("a.core"), Mod("free.mod") };
        var lines = LoadOrder.Explain(mods, 1, null, null, _game.SortTier);
        Assert.Contains(lines, l => l.StartsWith("After a.core (#3)") && l.Contains("b.patch needs a.core") && l.Contains("broken"));
        Assert.Contains(LoadOrder.Explain(mods, 0, null, null, _game.SortTier), l => l.Contains("ships with the game"));
        Assert.Contains("No rule places it", LoadOrder.Explain(mods, 3, null, null, _game.SortTier)[0]);
        var rules = SortRules.Parse("{\"rules\":{\"free.mod\":{\"loadLast\":true}}}");
        Assert.Contains("Your rules file puts it last.", LoadOrder.Explain(mods, 3, null, rules, _game.SortTier));
    }

    // ------------------------------------------------------------ bisect

    static Bisect Make(int n, params (string Mod, string Needs)[] needs)
    {
        var active = new[] { "ludeon.rimworld" }.Concat(Enumerable.Range(1, n).Select(i => "m" + i)).ToList();
        var deps = needs.GroupBy(x => x.Mod).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Needs).ToList());
        return new Bisect(active, new[] { "ludeon.rimworld" }, deps);
    }

    static int Run(Bisect b, Func<List<string>, bool> broken)
    {
        var rounds = 0;
        while (!b.Done && rounds < 50)
        {
            var trial = b.Trial();
            Assert.Contains("ludeon.rimworld", trial);
            b.Answer(broken(trial) ? BisectAnswer.Broken : BisectAnswer.Works);
            rounds++;
        }
        return rounds;
    }

    [Fact]
    public void Bisect_finds_one_bad_mod_in_about_log2_rounds()
    {
        var b = Make(100);
        var rounds = Run(b, t => t.Contains("m73"));
        Assert.Equal("m73", b.Culprit);
        Assert.Empty(b.AlsoNeeded);
        Assert.InRange(rounds, 7, 14);
    }

    [Fact]
    public void Bisect_keeps_dependencies_with_the_mods_that_need_them()
    {
        var b = Make(8, ("m2", "m7"));
        foreach (var _ in Enumerable.Range(0, 10))
        {
            if (b.Done) break;
            var t = b.Trial();
            if (t.Contains("m2")) Assert.Contains("m7", t);
            b.Answer(t.Contains("m5") ? BisectAnswer.Broken : BisectAnswer.Works);
        }
        Assert.Equal("m5", b.Culprit);
    }

    [Fact]
    public void Bisect_finds_a_clash_that_needs_mods_from_both_halves()
    {
        var b = Make(8);
        Run(b, t => t.Contains("m2") && t.Contains("m7"));
        Assert.Equal("m2", b.Culprit);
        Assert.Contains("m7", b.AlsoNeeded);
    }

    // ------------------------------------------------------------ Steam collections in

    const string CollectionJson = """
        {"response":{"result":1,"resultcount":2,"collectiondetails":[
          {"publishedfileid":"500","result":1,"children":[
            {"publishedfileid":"2","sortorder":2,"filetype":0},{"publishedfileid":"1","sortorder":1,"filetype":0},{"publishedfileid":"600","sortorder":3,"filetype":2}]},
          {"publishedfileid":"600","result":1,"children":[{"publishedfileid":"3","sortorder":1,"filetype":0},{"publishedfileid":"1","sortorder":2,"filetype":0}]},
          {"publishedfileid":"9","result":9}]}}
        """;

    [Fact]
    public async Task A_collection_lists_its_items_in_order_with_inner_collections_opened()
    {
        var parsed = CollectionImport.Parse(CollectionJson);
        Assert.Equal(new[] { "1", "2", "600" }, parsed["500"].Select(c => c.Id));
        Assert.False(parsed.ContainsKey("9"));
        var items = await CollectionImport.ItemsAsync("500", default, (_, _) => Task.FromResult(CollectionJson));
        Assert.Equal(new[] { "1", "2", "3" }, items);
        await Assert.ThrowsAsync<IOException>(() => CollectionImport.ItemsAsync("9", default, (_, _) => Task.FromResult(CollectionJson)));
    }

    [Fact]
    public void A_collection_modlist_finds_installed_items_by_workshop_id()
    {
        _rw.FromWorkshop("2009463077", FakeRimWorld.About("Harmony", "brrainz.harmony", "1.5"));
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var list = CollectionImport.ToModlist("Pack", "294100", new[] { "2009463077", "777" }, scan, new Dictionary<string, string> { ["777"] = "Seven" });
        Assert.Equal("brrainz.harmony", list.Mods[0].Id);
        Assert.Equal(("steam.777", "Seven"), (list.Mods[1].Id, list.Mods[1].Name));
        var (installed, missing) = list.Match(scan, _game);
        Assert.Equal(new[] { "brrainz.harmony" }, installed);
        Assert.Equal("777", Assert.Single(missing).WorkshopId);

        // Once the item is downloaded, the same entry is found by its Workshop id.
        _rw.Local("777", FakeRimWorld.About("Seven", "s.seven", "1.5"));
        File.WriteAllText(Path.Combine(_rw.Game, "Mods", "777", "About", "PublishedFileId.txt"), "777");
        Assert.Contains("s.seven", list.Match(ModLibrary.Scan(_game, _rw.Install()), _game).Installed);
    }

    // ------------------------------------------------------------ playing together

    [Fact]
    public void Share_codes_carry_mod_versions_and_old_codes_still_read()
    {
        var list = new Modlist { Name = "Mine", AppId = "294100" };
        list.Mods.Add(new ModlistEntry("a.mod", null, "11", "1.2"));
        list.Mods.Add(new ModlistEntry("b.mod"));
        var back = Modlist.FromShareCode(list.ToShareCode());
        Assert.Equal("1.2", back.Mods[0].Version);
        Assert.Null(back.Mods[1].Version);

        _rw.Local("a", "<ModMetaData><name>A</name><packageId>a.mod</packageId><modVersion>1.3</modVersion></ModMetaData>");
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var d = Assert.Single(PlayTogether.Versions(back, scan, _game));
        Assert.Equal(("1.3", "1.2"), (d.Mine, d.Theirs));
    }

    // ------------------------------------------------------------ notes, cleanup and settings

    [Fact]
    public void Notes_and_colours_are_kept_per_mod_and_empty_ones_removed()
    {
        var f = Path.Combine(_tmp, "notes.json");
        var n = new ModNotes(f);
        n.Set("a.mod", " crashes with X ", "green");
        Assert.Equal(("crashes with X", "Green"), (new ModNotes(f).Get("a.mod")!.Note, new ModNotes(f).Get("a.mod")!.Color));
        n.Set("a.mod", "", "not a colour");
        Assert.Null(new ModNotes(f).Get("a.mod"));
    }

    [Fact]
    public void Cleanup_lists_unused_mods_and_settings_left_by_removed_mods()
    {
        _rw.Local("Used", FakeRimWorld.About("Used", "u.used", "1.5"));
        _rw.Local("Spare", FakeRimWorld.About("Spare", "s.spare", "1.5"));
        _rw.FromWorkshop("123", FakeRimWorld.About("Big", "b.big", "1.5"));
        var cfg = Path.Combine(_rw.Data, "Config");
        File.WriteAllText(Path.Combine(cfg, "Mod_Used_UsedMod.xml"), "<x/>");
        File.WriteAllText(Path.Combine(cfg, "Mod_Gone_Folder_GoneMod.xml"), "<x/>");
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var unused = Hangar.Unused(scan, new[] { "u.used", "ludeon.rimworld" }, _game, f => f.EndsWith("123") ? 900 : 10);
        Assert.Equal(new[] { "b.big", "s.spare" }, unused.Select(u => u.Mod.Id));
        var orphan = Assert.Single(Hangar.Orphans(_rw.Data, scan));
        Assert.Equal("Gone_Folder", orphan.ModFolder);
        Assert.Equal("Used", ModSettings.FolderOf("Mod_Used_UsedMod.xml"));
        Assert.Null(ModSettings.FolderOf("Prefs.xml"));
    }

    [Fact]
    public void A_modlist_travels_with_its_mods_settings()
    {
        var used = _rw.Local("Used", FakeRimWorld.About("Used", "u.used", "1.5"));
        var cfg = Path.Combine(_rw.Data, "Config");
        File.WriteAllText(Path.Combine(cfg, "Mod_Used_UsedMod.xml"), "<settings>1</settings>");
        File.WriteAllText(Path.Combine(cfg, "Mod_Other_X.xml"), "<x/>");
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var files = ModSettings.For(_rw.Data, scan.Mods.Where(m => m.Id == "u.used"));
        Assert.Single(files);
        Directory.CreateDirectory(_tmp);
        var zip = Path.Combine(_tmp, "list.zip");
        ModSettings.Pack(new Modlist { Name = "With settings", AppId = "294100", Mods = { new ModlistEntry("u.used") } }, files, zip);
        var (list, settings) = ModSettings.Unpack(zip, "x");
        Assert.Equal("With settings", list.Name);
        var other = Path.Combine(_tmp, "OtherData");
        Assert.Equal(1, ModSettings.Restore(other, settings));
        Assert.Equal("<settings>1</settings>", File.ReadAllText(Path.Combine(other, "Config", "Mod_Used_UsedMod.xml")));
        Assert.Equal(1, ModSettings.Restore(other, settings)); // again: the old one is kept as a backup
        Assert.Single(Directory.GetFiles(Path.Combine(other, "Config"), "*.bak"));
    }

    // ------------------------------------------------------------ save backups, launch options, bug reports

    [Fact]
    public void A_save_is_at_risk_when_mods_it_used_are_off_and_backups_are_capped()
    {
        _rw.Local("a", FakeRimWorld.About("A Mod", "a.mod", "1.5"));
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var save = new SaveInfo("x.rws", "x", DateTime.Now, 1, "1.5", new Modlist { Mods = { new("ludeon.rimworld"), new("a.mod", "A Mod") } });
        Assert.Null(SaveBackups.Risk(save, new[] { "ludeon.rimworld", "a.mod" }, scan, _game));
        Assert.Contains("A Mod", SaveBackups.Risk(save, new[] { "ludeon.rimworld" }, scan, _game));
        Assert.Contains("new to it", SaveBackups.Risk(save, new[] { "ludeon.rimworld", "a.mod", "b.new" }, scan, _game));

        Directory.CreateDirectory(_tmp);
        var src = Path.Combine(_tmp, "Colony.rws");
        File.WriteAllText(src, "save");
        var dir = Path.Combine(_tmp, "backups");
        for (var i = 0; i < 4; i++) SaveBackups.Backup(src, dir, keep: 2, now: new DateTime(2026, 1, 1).AddHours(i));
        Assert.Equal(2, Directory.GetFiles(dir).Length);
    }

    [Fact]
    public void Launch_options_split_like_a_command_line_and_clashes_are_named()
    {
        Assert.Equal(new[] { "-quicktest", "-logFile", "C:\\a b\\x.log", "" }, LaunchArgs.Split(" -quicktest -logFile \"C:\\a b\\x.log\" \"\""));
        Assert.Equal(new[] { "-savedatafolder=x" }, LaunchArgs.Clashes(new[] { "-quicktest", "-savedatafolder=x" }));
    }

    [Fact]
    public void Bug_reports_go_to_github_issues_or_the_workshop_comments()
    {
        var gh = RimWorldAbout.Parse("<ModMetaData><name>A</name><packageId>a.a</packageId><url>https://github.com/someone/mymod</url></ModMetaData>", "/mods/a");
        Assert.StartsWith("https://github.com/someone/mymod/issues/new?title=Boom&body=", BugReport.Target(gh, "Boom", "text"));
        var ws = RimWorldAbout.Parse("<ModMetaData><name>B</name><packageId>b.b</packageId></ModMetaData>", "/mods/b");
        ws.WorkshopId = "42";
        Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/comments/42", BugReport.Target(ws, "t", "b"));
        ws.WorkshopId = null;
        Assert.Null(BugReport.Target(ws, "t", "b"));
        var issue = new LogIssue(LogLevel.Error, "NullReferenceException", "NullReferenceException\n at B.Thing()", 3, new[] { new ModBlame("b.b", Blame.Code) }, 10);
        Assert.Contains("3 times", BugReport.Text(issue, ws, "1.5.4104", 40));
    }

    // ------------------------------------------------------------ load times and update notes

    [Fact]
    public void Load_time_is_the_last_growth_before_the_log_goes_quiet()
    {
        var samples = new List<(double, long)> { (0, 0), (1, 100), (5, 900), (9, 2000), (12, 2000), (25, 2000), (31, 2000) };
        Assert.Equal(9, LoadTimes.Settled(samples, quiet: 20));
        Assert.Null(LoadTimes.Settled(samples.Take(5).ToList(), quiet: 20));

        var lt = new LoadTimes(Path.Combine(_tmp, "loadtimes.json"));
        lt.Add("Normal game", new[] { "a", "b" }, 30);
        var run = lt.Add("Normal game", new[] { "a", "b", "c" }, 45, new Dictionary<string, string> { ["c"] = "C Mod" });
        Assert.Equal(new[] { "C Mod" }, run.Added);
        Assert.Equal(2, lt.All().Count);
    }

    [Fact]
    public void Update_notes_are_read_from_the_changelog_page_and_risky_words_flagged()
    {
        const string html = """
            <div class="detailBox workshopAnnouncement noFooter changeLogCtn">
              <div class="changelog headline">  Update: 21 Oct @ 1:02pm  </div>
              <p id="1761051720" >Big rewrite.<br>This is a <b>breaking</b> change &amp; needs a new save.</p>
            </div>
            <div class="detailBox workshopAnnouncement noFooter changeLogCtn">
              <div class="changelog headline">Update: 1 Oct</div><p id="1759300000">Fixed typos</p>
            </div>
            """;
        var notes = UpdateNotes.Parse(html);
        Assert.Equal(2, notes.Count);
        Assert.Equal("Big rewrite.\nThis is a breaking change & needs a new save.", notes[0].Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1761051720), notes[0].When);
        Assert.Contains("breaking", notes[0].Warnings);
        Assert.Contains("new save", notes[0].Warnings);
        Assert.Empty(notes[1].Warnings);
    }

    // ------------------------------------------------------------ clean-colony test and players' logs

    [Fact]
    public void A_clean_colony_has_the_game_the_mods_needs_and_the_mod()
    {
        _rw.Local("Royalty", FakeRimWorld.About("Royalty", "ludeon.rimworld.royalty", "1.5"));
        _rw.FromWorkshop("2009463077", FakeRimWorld.About("Harmony", "brrainz.harmony", "1.5"));
        _rw.Local("Other", FakeRimWorld.About("Other", "o.other", "1.5"));
        var mine = _rw.Local("Mine", "<ModMetaData><name>Mine</name><packageId>me.mine</packageId><modDependencies>"
            + "<li><packageId>brrainz.harmony</packageId></li><li><packageId>not.here</packageId><steamWorkshopUrl>steam://url/CommunityFilePage/55</steamWorkshopUrl></li></modDependencies></ModMetaData>");
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var mod = scan.Mods.First(m => m.Id == "me.mine");
        var (active, missing) = CleanTest.Plan(mod, scan, _game);
        Assert.Equal("ludeon.rimworld", active[0]);
        Assert.Contains("brrainz.harmony", active);
        Assert.Equal("me.mine", active[^1]);
        Assert.DoesNotContain("o.other", active);
        Assert.Equal("not.here", Assert.Single(missing).Id);
        Assert.True(active.IndexOf("brrainz.harmony") < active.IndexOf("me.mine"));
        _ = mine;
    }

    [Fact]
    public void Clean_test_results_warn_about_errors_and_changes()
    {
        var issue = new LogIssue(LogLevel.Error, "Boom", "Boom", 2, new[] { new ModBlame("me.mine", Blame.Code) }, 1);
        var other = new LogIssue(LogLevel.Error, "Elsewhere", "x", 5, new[] { new ModBlame("o.other", Blame.Code) }, 2);
        var report = new LogReport("1.5.4104 rev435", new[] { issue, other }, new Dictionary<string, int>(), 100, false);
        var r = CleanTest.Result(report, "me.mine", "h1", null);
        Assert.Equal((2, 0), (r.Errors, r.Warnings));
        Assert.Contains("2 errors", CleanTest.Warning(r, "h1"));
        Assert.Contains("changed since", CleanTest.Warning(r, "h2"));
        Assert.Null(CleanTest.Warning(null, "h1"));
        Assert.Null(CleanTest.Warning(CleanTest.Result(new LogReport(null, new[] { other }, new Dictionary<string, int>(), 1, false), "me.mine", "h1", "1.5"), "h1"));

        var rec = new CleanTestRecord(Path.Combine(_tmp, "clean.json"));
        rec.Set("/mods/Mine", r);
        Assert.Equal(2, rec.Get("/mods/Mine")!.Errors);
        Assert.Equal(2, rec.Get("/mods/Mine", "1.5")!.Errors);
        Assert.Null(rec.Get("/mods/Mine", "1.4"));
    }

    [Fact]
    public void Paste_links_become_raw_links_and_mod_lists_are_found_in_logs()
    {
        Assert.Equal("https://pastebin.com/raw/abc123", PlayerLog.RawUrl("https://pastebin.com/abc123"));
        Assert.Equal("https://gist.githubusercontent.com/me/0123abcd/raw", PlayerLog.RawUrl("https://gist.github.com/me/0123abcd"));
        Assert.Null(PlayerLog.RawUrl("Exception in Verse.Root.Update\nat Foo"));
        var log = "RimWorld 1.5.4104 rev435\nLoaded mods:\nHarmony(brrainz.harmony)[2.3]\nMine(me.mine)[1.0]\n\nrest of log";
        Assert.Equal(new[] { "Harmony(brrainz.harmony)[2.3]", "Mine(me.mine)[1.0]" }, PlayerLog.ModList(log));
        var code = new Modlist { Name = "x", AppId = "294100", Mods = { new ModlistEntry("a.a") } }.ToShareCode();
        Assert.Equal(new[] { "a.a" }, PlayerLog.ModList("report\n" + code + "\n"));
    }

    // ------------------------------------------------------------ links and freezing

    [Fact]
    public void Dev_links_put_a_mod_folder_into_the_mods_folder_and_only_remove_their_own()
    {
        Directory.CreateDirectory(_tmp);
        var dev = Path.Combine(_tmp, "dev", "MyMod");
        Directory.CreateDirectory(Path.Combine(dev, "About"));
        var links = new DevLinks(Path.Combine(_tmp, "devlinks.json"));
        var mods = Path.Combine(_rw.Game, "Mods");
        var l = links.Add(mods, dev);
        Assert.True(FolderLinks.IsLink(l.Link));
        Assert.Equal(Path.Combine(mods, "MyMod"), l.Link);
        Assert.NotNull(links.For(dev));
        Assert.True(links.Remove(dev));
        Assert.False(Directory.Exists(l.Link));
        Assert.True(Directory.Exists(dev)); // the mod itself is never touched

        Directory.CreateDirectory(Path.Combine(mods, "MyMod"));
        Assert.Throws<IOException>(() => links.Add(mods, dev));
        Assert.False(FolderLinks.Remove(Path.Combine(mods, "MyMod")));
    }

    [Fact]
    public void Freezing_copies_workshop_mods_links_them_in_and_out_and_says_what_is_waiting()
    {
        _rw.FromWorkshop("111", FakeRimWorld.About("W", "w.mod", "1.5"));
        _rw.Local("Loc", FakeRimWorld.About("Loc", "l.loc", "1.5"));
        var inst = new InstanceStore(Path.Combine(_tmp, "instances")).Create(_game, _rw.Install(), "Long run", "", null, null);
        var scan = ModLibrary.Scan(_game, _rw.Install());
        var t = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var frozen = Freezer.Freeze(inst, scan.Mods, new Dictionary<string, DateTimeOffset> { ["111"] = t });
        Assert.Equal("111", Assert.Single(frozen).WorkshopId);
        Assert.True(Freezer.IsFrozen(inst));
        Assert.True(File.Exists(Path.Combine(Freezer.Folder(inst), "111", "About", "About.xml")));
        Assert.Empty(Freezer.Waiting(inst, new Dictionary<string, DateTimeOffset> { ["111"] = t }));
        Assert.Single(Freezer.Waiting(inst, new Dictionary<string, DateTimeOffset> { ["111"] = t.AddDays(1) }));

        var mods = Path.Combine(_rw.Game, "Mods");
        var made = Freezer.LinkIn(inst, mods);
        Assert.Equal(Path.Combine(mods, "111"), Assert.Single(made));
        Assert.Equal(Path.Combine(mods, "111"), Assert.Single(Freezer.LinkIn(inst, mods))); // again: replaced, not doubled
        Assert.Equal(1, Freezer.Unlink(mods));
        Assert.False(Directory.Exists(Path.Combine(mods, "111")));
        Assert.True(Directory.Exists(Path.Combine(mods, "Loc")));
        Freezer.Unfreeze(inst);
        Assert.False(Freezer.IsFrozen(inst));
    }
}
