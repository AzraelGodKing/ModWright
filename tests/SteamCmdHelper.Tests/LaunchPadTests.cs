using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The 1.8.0 Launch Pad helpers: bump suggestions, subscriber alerts, checklists, README to BBCode, family
/// releases, the About.xml editor and the clean-colony test record.</summary>
public sealed class LaunchPadTests
{
    [Fact]
    public void Bump_suggestion_follows_the_commits()
    {
        Assert.Null(BumpSuggestion.Suggest(Array.Empty<string>()));
        Assert.Equal(ModScanner.Bump.Patch, BumpSuggestion.Suggest(new[] { "- Fix the crash on load" })!.Value.Part);
        Assert.Equal(ModScanner.Bump.Minor, BumpSuggestion.Suggest(new[] { "Fix typo", "Add a gizmo to sort shelves" })!.Value.Part);
        Assert.Equal(ModScanner.Bump.Minor, BumpSuggestion.Suggest(new[] { "feat(ui): bigger buttons" })!.Value.Part);
        var major = BumpSuggestion.Suggest(new[] { "Add a gizmo", "refactor!: new save format" })!.Value;
        Assert.Equal(ModScanner.Bump.Major, major.Part);
        Assert.Equal("refactor!: new save format", major.Reason);
        Assert.Equal(ModScanner.Bump.Major, BumpSuggestion.Suggest(new[] { "Rework storage; BREAKING CHANGE: old shelves are gone" })!.Value.Part);
    }

    [Fact]
    public void AtLeast_compares_numbers_not_text()
    {
        Assert.True(BumpSuggestion.AtLeast("1.10.0", "1.9.0"));
        Assert.True(BumpSuggestion.AtLeast("1.2", "1.2.0"));
        Assert.False(BumpSuggestion.AtLeast("1.2.3", "1.3.0"));
    }

    static readonly DateOnly Today = new(2026, 10, 21);

    [Fact]
    public void Subscriber_drop_needs_a_real_fall()
    {
        var points = new List<SubscriberPoint> { new(Today.AddDays(-8), 1000), new(Today, 930) };
        Assert.Equal("lost 70 subscribers this week (-7%)", SubscriberAlerts.Drop(points, Today));
        Assert.Null(SubscriberAlerts.Drop(new List<SubscriberPoint> { new(Today.AddDays(-8), 1000), new(Today, 985) }, Today));
        Assert.Null(SubscriberAlerts.Drop(new List<SubscriberPoint> { new(Today.AddDays(-8), 40), new(Today, 35) }, Today)); // fewer than 10 people
        Assert.Null(SubscriberAlerts.Drop(new List<SubscriberPoint> { new(Today.AddDays(-2), 1000), new(Today, 500) }, Today)); // no week yet
    }

    [Fact]
    public void Milestone_is_the_biggest_round_number_passed()
    {
        Assert.Equal(1000, SubscriberAlerts.Milestone(new List<SubscriberPoint> { new(Today.AddDays(-1), 240), new(Today, 1010) }));
        Assert.Null(SubscriberAlerts.Milestone(new List<SubscriberPoint> { new(Today.AddDays(-1), 1001), new(Today, 1100) }));
        Assert.Null(SubscriberAlerts.Milestone(new List<SubscriberPoint> { new(Today, 5000) }));
    }

    [Fact]
    public void Checklist_reads_one_step_per_line()
    {
        Assert.Equal(new[] { "Tested on 1.5", "Updated screenshots" },
            ReleaseChecklist.Items("# my steps\r\n- [ ] Tested on 1.5\n\n* Updated screenshots\ntested on 1.5\n"));
        Assert.Empty(ReleaseChecklist.Items(null));
    }

    [Fact]
    public void Markdown_becomes_bbcode()
    {
        var md = "# Better Shelves\n\nShelves that **sort** themselves, *quietly*. See [the wiki](https://example.com/wiki).\n\n" +
                 "- one\n- two with `code_here`\n\n1. first\n2. second\n\n> a quote\n\n---\n\n```\nraw *text*\n```\n![shot](https://example.com/a.png)";
        var bb = MarkdownBBCode.Convert(md);
        Assert.Contains("[h1]Better Shelves[/h1]", bb);
        Assert.Contains("[b]sort[/b]", bb);
        Assert.Contains("[i]quietly[/i]", bb);
        Assert.Contains("[url=https://example.com/wiki]the wiki[/url]", bb);
        Assert.Contains("[list]\n[*]one\n[*]two with [noparse]code_here[/noparse]\n[/list]", bb);
        Assert.Contains("[olist]\n[*]first\n[*]second\n[/olist]", bb);
        Assert.Contains("[quote]a quote[/quote]", bb);
        Assert.Contains("[hr][/hr]", bb);
        Assert.Contains("[code]\nraw *text*\n[/code]", bb);
        Assert.Contains("[img]https://example.com/a.png[/img]", bb);
        Assert.Empty(SteamText.Problems(bb));
    }

    sealed record Fam(string Key, params string[] Needs);

    [Fact]
    public void Family_release_puts_what_others_need_first_and_skips_dependents_of_failures()
    {
        var items = new List<Fam> { new("addon", "core"), new("core", "lib"), new("lib"), new("solo"), new("addon2", "addon") };
        var order = FamilyRelease.Order(items, f => f.Key, f => f.Needs).Select(f => f.Key).ToList();
        Assert.Equal(new[] { "lib", "core", "addon", "solo", "addon2" }, order);
        Assert.Equal(new HashSet<string> { "addon", "addon2" }, FamilyRelease.Dependents(new[] { "core" }, items, f => f.Key, f => f.Needs));

        var loop = new List<Fam> { new("a", "b"), new("b", "a") };
        Assert.Equal(2, FamilyRelease.Order(loop, f => f.Key, f => f.Needs).Count);
    }

    const string About = """
        <?xml version="1.0" encoding="utf-8"?>
        <ModMetaData>
          <!-- keep me -->
          <name>Shelves</name>
          <author>Kat</author>
          <packageId>Kat.Shelves</packageId>
          <supportedVersions><li>1.5</li></supportedVersions>
          <description>Sorts.</description>
          <loadAfterByVersion><v1.5><li>brrainz.harmony</li></v1.5></loadAfterByVersion>
        </ModMetaData>
        """;

    [Fact]
    public void About_editor_round_trips_and_keeps_what_it_does_not_show()
    {
        var f = AboutEditor.Read(About);
        Assert.Equal("Kat.Shelves", f.PackageId);
        Assert.Equal(new[] { "1.5" }, f.SupportedVersions);
        f.SupportedVersions.Add("1.6");
        f.Author = "Kat, Wolf";
        f.Dependencies.Add(new ModDependency("brrainz.harmony", "Harmony", "2009463077", "https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077"));
        f.LoadAfter.Add("brrainz.harmony");
        var xml = AboutEditor.Write(About, f);
        Assert.Contains("keep me", xml);
        Assert.Contains("<loadAfterByVersion>", xml);
        Assert.Contains("<steamWorkshopUrl>", xml);
        Assert.DoesNotContain("<author>", xml);
        var back = RimWorldAbout.Parse(xml);
        Assert.Equal(new[] { "Kat", "Wolf" }, back.Authors);
        Assert.Equal(new[] { "1.5", "1.6" }, back.SupportedVersions);
        Assert.Equal("2009463077", back.Dependencies.All.Single().WorkshopId);
        Assert.Equal(new[] { "brrainz.harmony" }, back.LoadAfter.All);
    }

    [Fact]
    public void About_editor_lints_what_would_be_written()
    {
        var f = AboutEditor.Read(About);
        Assert.DoesNotContain(AboutEditor.Lint(f, About), x => x.Level == LintLevel.Block);
        f.PackageId = "";
        Assert.Contains(AboutEditor.Lint(f, About), x => x.Rule == "about-no-packageid" && x.Level == LintLevel.Block);
        f.PackageId = "Kat.Shelves";
        f.Description = "";
        Assert.Contains(AboutEditor.Lint(f, About), x => x.Rule == "about-no-description");
        Assert.Contains("<ModMetaData>", AboutEditor.Write(null, new AboutFields { Name = "New" }));
    }

    [Fact]
    public void Clean_test_record_keeps_results_per_game_version()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lp_" + Guid.NewGuid().ToString("N"));
        try
        {
            var rec = new CleanTestRecord(Path.Combine(dir, "tests.json"));
            var mod = Path.Combine(dir, "Shelves");
            rec.Set(mod, new CleanTestResult { GameVersion = "1.5.4104 rev435", Hash = "a", Errors = 0 });
            rec.Set(mod, new CleanTestResult { GameVersion = "1.6.4518", Hash = "b", Errors = 2, Titles = { "boom" } });
            Assert.Equal("b", rec.Get(mod)!.Hash);
            Assert.Equal("a", rec.Get(mod, "1.5")!.Hash);
            Assert.Equal(2, rec.Get(mod, "1.6.1")!.Errors);
            Assert.Null(CleanTest.Warning(rec.Get(mod, "1.5"), "a"));
            Assert.Contains("changed since", CleanTest.Warning(rec.Get(mod, "1.5"), "z"));
            Assert.Contains("2 errors: boom", CleanTest.Warning(rec.Get(mod), "b"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Folder_hash_changes_with_the_files_and_ignores_source()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lp_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "About"));
            Directory.CreateDirectory(Path.Combine(dir, "Source"));
            File.WriteAllText(Path.Combine(dir, "About", "About.xml"), "<ModMetaData/>");
            var first = CleanTest.FolderHash(dir);
            File.WriteAllText(Path.Combine(dir, "Source", "Code.cs"), "class A {}");
            Assert.Equal(first, CleanTest.FolderHash(dir));
            File.WriteAllText(Path.Combine(dir, "About", "About.xml"), "<ModMetaData><name>x</name></ModMetaData>");
            Assert.NotEqual(first, CleanTest.FolderHash(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
