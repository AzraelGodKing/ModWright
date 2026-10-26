using Xunit;

namespace SteamCmdHelper.Tests;

public class ModScannerTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void Folder_stamp_changes_only_when_the_mods_change()
    {
        var dir = _repo.AddMod("Alpha", workshopId: "1111111");
        var first = ModScanner.FolderStamp(_repo.Root);
        Assert.NotEqual("", first);
        Assert.Equal(first, ModScanner.FolderStamp(_repo.Root));

        var about = Path.Combine(dir, "About", "About.xml");
        File.SetLastWriteTimeUtc(about, File.GetLastWriteTimeUtc(about).AddMinutes(1)); // edited elsewhere
        var edited = ModScanner.FolderStamp(_repo.Root);
        Assert.NotEqual(first, edited);

        _repo.AddMod("Beta");
        Assert.NotEqual(edited, ModScanner.FolderStamp(_repo.Root));
        Assert.Equal("", ModScanner.FolderStamp(Path.Combine(_repo.Root, "missing")));
    }

    ModInfo ScanOne()
    {
        var mods = ModScanner.Scan(_repo.Root, _cfg);
        return Assert.Single(mods);
    }

    [Fact]
    public void Valid_mod_is_ready_with_name_version_and_id()
    {
        _repo.AddMod("Alpha");

        var m = ScanOne();

        Assert.Empty(m.Problems);
        Assert.True(m.Ready);
        Assert.True(m.OnSteam);
        Assert.Equal("Alpha", m.Folder);
        Assert.Equal("Alpha Display", m.Name);
        Assert.Equal("1.2.3", m.Version);
        Assert.Equal("123456789", m.WorkshopId);
        Assert.Equal("Ready", m.StatusText);
    }

    [Fact]
    public void Missing_repo_returns_empty_list()
    {
        Assert.Empty(ModScanner.Scan(Path.Combine(_repo.Root, "nope"), _cfg));
    }

    [Fact]
    public void Folders_without_marker_or_starting_with_dot_are_skipped()
    {
        _repo.Write("NotAMod/readme.txt", "hi");
        _repo.AddMod(".Hidden");
        _repo.AddMod("Beta");

        var m = ScanOne();
        Assert.Equal("Beta", m.Folder);
    }

    [Fact]
    public void Mods_are_sorted_by_folder_name_ignoring_case()
    {
        _repo.AddMod("charlie");
        _repo.AddMod("Alpha");
        _repo.AddMod("bravo");

        var folders = ModScanner.Scan(_repo.Root, _cfg).Select(m => m.Folder);
        Assert.Equal(new[] { "Alpha", "bravo", "charlie" }, folders);
    }

    // ------------------------------------------------------------ RimWorld: Add Changenote rules

    [Fact]
    public void Missing_changelog_is_a_problem()
    {
        _repo.AddMod("Alpha", changelog: null);

        var m = ScanOne();
        Assert.False(m.Ready);
        Assert.Contains("no About/changelog.txt", m.Problems);
    }

    [Fact]
    public void Changelog_without_an_entry_for_the_version_is_a_problem()
    {
        _repo.AddMod("Alpha", version: "1.2.3", changelog: "1.2.2 - Older fix.\n1.2.1 - Even older.");

        var m = ScanOne();
        Assert.Contains("About/changelog.txt has no entry for 1.2.3", m.Problems);
    }

    [Fact]
    public void Missing_version_is_a_problem()
    {
        _repo.AddMod("Alpha", version: null);

        var m = ScanOne();
        Assert.Contains("no version (<version> in About/Manifest.xml or version in About/About.xml)", m.Problems);
    }

    [Fact]
    public void Manifest_version_wins_over_about_xml()
    {
        _repo.AddMod("Alpha", version: "1.0.0", changelog: "1.1.0 - From the manifest.\n1.0.0 - From About.xml.");
        _repo.Write("Alpha/About/Manifest.xml", "<?xml version=\"1.0\"?><Manifest><identifier>Alpha</identifier><version>1.1.0</version></Manifest>");

        var m = ScanOne();
        Assert.Empty(m.Problems);
        Assert.Equal("1.1.0", m.Version);
    }

    [Fact]
    public void Manifest_without_version_falls_back_to_about_xml()
    {
        _repo.AddMod("Alpha");
        _repo.Write("Alpha/About/Manifest.xml", "<?xml version=\"1.0\"?><Manifest><identifier>Alpha</identifier></Manifest>");

        Assert.Equal("1.2.3", ScanOne().Version);
    }

    [Fact]
    public void Changenote_is_the_entry_for_the_current_version()
    {
        var lines = "# Changelog for MyMod\n# Author is me\n\n1.0.2 - Newest.\n1.0.1 Fixed a typo\nSecond line\n\n# a comment\nThird line\n1.0.0 First publish"
            .Split('\n');

        Assert.Equal("1.0.1 Fixed a typo\nSecond line\n\nThird line", ModScanner.ChangenoteFor(lines, "1.0.1"));
        Assert.Equal("1.0.0 First publish", ModScanner.ChangenoteFor(lines, "1.0.0"));
        Assert.Equal("", ModScanner.ChangenoteFor(lines, "2.0.0"));
        Assert.Equal("", ModScanner.ChangenoteFor(lines, ""));
    }

    [Fact]
    public void ReadChangelog_returns_the_entry_for_the_given_version()
    {
        var f = _repo.Write("c.txt", "1.4.0 - Newest\r\n- One\r\n1.3.0 - Older\r\n");

        Assert.Equal("1.4.0 - Newest\n- One", ModScanner.ReadChangelog(f, "1.4.0"));
    }

    // ------------------------------------------------------------ Workshop id and preview

    [Fact]
    public void Non_numeric_workshop_id_is_a_problem()
    {
        _repo.AddMod("Alpha", workshopId: "12ab");

        var m = ScanOne();
        Assert.Null(m.WorkshopId);
        Assert.Contains("About/PublishedFileId.txt is not a number", m.Problems);
    }

    [Fact]
    public void Workshop_id_is_trimmed()
    {
        _repo.AddMod("Alpha", workshopId: "  42\r\n");

        Assert.Equal("42", ScanOne().WorkshopId);
    }

    [Fact]
    public void New_mod_needs_a_preview()
    {
        _repo.AddMod("Alpha", workshopId: null, preview: false);

        var m = ScanOne();
        Assert.False(m.OnSteam);
        Assert.Contains("needs About/Preview.png to create on Steam", m.Problems);
    }

    [Fact]
    public void New_mod_preview_over_one_megabyte_is_a_problem()
    {
        _repo.AddMod("Alpha", workshopId: null);
        _repo.Write("Alpha/About/Preview.png", new string('x', (int)ModScanner.MaxPreviewBytes + 1));

        Assert.Contains("About/Preview.png is over 1 MB (Steam's limit)", ScanOne().Problems);
    }

    [Fact]
    public void New_mod_with_preview_is_ready_to_create()
    {
        _repo.AddMod("Alpha", workshopId: null);

        var m = ScanOne();
        Assert.True(m.Ready);
        Assert.Equal("Ready to create (private)", m.StatusText);
    }

    [Fact]
    public void Existing_mod_does_not_need_a_preview()
    {
        _repo.AddMod("Alpha", preview: false);

        Assert.True(ScanOne().Ready);
    }

    // ------------------------------------------------------------ other checks

    [Fact]
    public void Missing_build_output_is_only_a_warning()
    {
        _repo.AddMod("Alpha", built: false);

        var m = ScanOne();
        Assert.True(m.Ready);
        Assert.Contains("not built (nothing matches Assemblies/*.dll or */Assemblies/*.dll)", m.Warnings);
    }

    [Fact]
    public void Unreadable_marker_xml_is_a_problem()
    {
        _repo.AddMod("Alpha");
        _repo.Write("Alpha/About/About.xml", "<ModMetaData><name>broken");

        var m = ScanOne();
        Assert.Contains(m.Problems, p => p.StartsWith("About/About.xml unreadable ("));
    }

    [Fact]
    public void Status_text_lists_problems_then_warnings()
    {
        _repo.AddMod("Alpha", changelog: null, built: false);

        Assert.Equal("Fix: no About/changelog.txt", ScanOne().StatusText);

        _repo.AddMod("Alpha", built: false);
        Assert.Equal("Ready (not built (nothing matches Assemblies/*.dll or */Assemblies/*.dll))", ScanOne().StatusText);
    }

    // ------------------------------------------------------------ helpers

    [Fact]
    public void ReadMeta_xml_returns_null_for_missing_or_blank_values()
    {
        var f = _repo.Write("a.xml", "<Root><name>  Hello  </name><blank>   </blank></Root>");

        Assert.Equal("Hello", ModScanner.ReadMeta(f, "Root/name"));
        Assert.Equal("Hello", ModScanner.ReadMeta(f, "/Root/name"));
        Assert.Null(ModScanner.ReadMeta(f, "Root/blank"));
        Assert.Null(ModScanner.ReadMeta(f, "Root/missing"));
        Assert.Null(ModScanner.ReadMeta(f, ""));
    }

    [Fact]
    public void MatchGlob_matches_last_segment_wildcards()
    {
        var dir = _repo.AddMod("Alpha");
        _repo.Write("Alpha/Assemblies/Other.dll", "dll");
        _repo.Write("Alpha/Assemblies/notes.txt", "txt");

        var hits = ModScanner.MatchGlob(dir, "Assemblies/*.dll").Select(Path.GetFileName).OrderBy(n => n);

        Assert.Equal(new[] { "Alpha.dll", "Other.dll" }, hits);
        Assert.Empty(ModScanner.MatchGlob(dir, "Missing/*.dll"));
    }

    [Fact]
    public void MatchGlob_matches_wildcard_folders()
    {
        var dir = _repo.AddMod("Alpha", built: false);
        _repo.Write("Alpha/1.5/Assemblies/Alpha.dll", "dll");
        _repo.Write("Alpha/1.6/Assemblies/Alpha.dll", "dll");
        _repo.Write("Alpha/1.6/Defs/Things.xml", "xml");

        var hits = ModScanner.MatchGlob(dir, "*/Assemblies/*.dll").Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).OrderBy(n => n);

        Assert.Equal(new[] { "1.5/Assemblies/Alpha.dll", "1.6/Assemblies/Alpha.dll" }, hits);
        Assert.Empty(ModScanner.MatchGlob(dir, "*/Missing/*.dll"));
    }

    [Fact]
    public void Build_in_a_version_folder_counts_as_built()
    {
        _repo.AddMod("Alpha", built: false);
        _repo.Write("Alpha/Assemblies/.gitkeep", "");
        _repo.Write("Alpha/1.6/Assemblies/Alpha.dll", "dll");

        Assert.DoesNotContain(ScanOne().Warnings, w => w.StartsWith("not built"));
    }
}
