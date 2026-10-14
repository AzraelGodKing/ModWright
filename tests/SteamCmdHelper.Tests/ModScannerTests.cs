namespace SteamCmdHelper.Tests;

public class ModScannerTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = new();

    public void Dispose() => _repo.Dispose();

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

    [Fact]
    public void Missing_changelog_is_a_problem()
    {
        _repo.AddMod("Alpha", changelog: null);

        var m = ScanOne();
        Assert.False(m.Ready);
        Assert.Contains("no About/changelog.txt", m.Problems);
    }

    [Fact]
    public void Changelog_version_must_match_marker_version()
    {
        _repo.AddMod("Alpha", version: "1.2.3", changelog: "1.2.4\n- A note that is long enough.");

        var m = ScanOne();
        Assert.Contains("changelog 1.2.4 vs 1.2.3 in About/About.xml", m.Problems);
    }

    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("1.2")]
    [InlineData("Version 1.2.3")]
    [InlineData("")]
    public void Changelog_first_line_must_be_a_version(string firstLine)
    {
        _repo.AddMod("Alpha", changelog: firstLine + "\n- A note that is long enough.");

        var m = ScanOne();
        Assert.Contains("changelog line 1 is not a version", m.Problems);
    }

    [Fact]
    public void Changelog_without_notes_is_a_problem()
    {
        _repo.AddMod("Alpha", changelog: "1.2.3\n- short");

        var m = ScanOne();
        Assert.Contains("changelog has no notes", m.Problems);
    }

    [Fact]
    public void Version_comes_from_changelog_when_marker_has_none()
    {
        _repo.AddMod("Alpha", version: null, changelog: "2.0.0\n- Rewrote everything from scratch.");

        var m = ScanOne();
        Assert.Empty(m.Problems);
        Assert.Equal("2.0.0", m.Version);
    }

    [Fact]
    public void Version_comes_from_changelog_when_version_xpath_is_empty()
    {
        _cfg.VersionXPath = "";
        _repo.AddMod("Alpha", version: "9.9.9", changelog: "2.0.0\n- Rewrote everything from scratch.");

        var m = ScanOne();
        Assert.Empty(m.Problems);
        Assert.Equal("2.0.0", m.Version);
    }

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

    [Fact]
    public void Missing_build_output_is_a_problem()
    {
        _repo.AddMod("Alpha", built: false);

        var m = ScanOne();
        Assert.Contains("not built (nothing matches Assemblies/*.dll)", m.Problems);
    }

    [Fact]
    public void Empty_built_files_skips_the_build_check()
    {
        _cfg.BuiltFiles = Array.Empty<string>();
        _repo.AddMod("Alpha", built: false);

        Assert.True(ScanOne().Ready);
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
    public void Status_text_lists_problems()
    {
        _repo.AddMod("Alpha", changelog: null, built: false);

        var m = ScanOne();
        Assert.Equal("Fix: no About/changelog.txt; not built (nothing matches Assemblies/*.dll)", m.StatusText);
    }

    [Fact]
    public void Custom_layout_is_respected()
    {
        _cfg.ModMarker = "mod.json";
        _cfg.Changelog = "CHANGES.txt";
        _cfg.WorkshopIdFile = "id.txt";
        _cfg.BuiltFiles = Array.Empty<string>();
        _repo.Write("Gamma/mod.json", "{}");
        _repo.Write("Gamma/CHANGES.txt", "0.1.0\n- First public release.");
        _repo.Write("Gamma/id.txt", "777");

        var m = ScanOne();
        Assert.Empty(m.Problems);
        Assert.Equal("Gamma", m.Name);
        Assert.Equal("0.1.0", m.Version);
        Assert.Equal("777", m.WorkshopId);
    }

    // ------------------------------------------------------------ ReadChangelog

    [Fact]
    public void ReadChangelog_splits_version_from_notes()
    {
        var f = _repo.Write("c.txt", "  1.4.0  \n\n- One\n- Two\n");

        var (version, notes) = ModScanner.ReadChangelog(f, _cfg);

        Assert.Equal("1.4.0", version);
        Assert.Equal("- One\n- Two", notes);
    }

    [Fact]
    public void ReadChangelog_handles_crlf()
    {
        var f = _repo.Write("c.txt", "1.4.0\r\n- One\r\n- Two\r\n");

        var (version, notes) = ModScanner.ReadChangelog(f, _cfg);

        Assert.Equal("1.4.0", version);
        Assert.Equal("- One\n- Two", notes);
    }

    [Fact]
    public void ReadChangelog_whole_file_is_notes_when_first_line_is_not_version()
    {
        _cfg.ChangelogFirstLineIsVersion = false;
        var f = _repo.Write("c.txt", "1.4.0\n- One\n");

        var (version, notes) = ModScanner.ReadChangelog(f, _cfg);

        Assert.Null(version);
        Assert.Equal("1.4.0\n- One", notes);
    }

    [Fact]
    public void ReadChangelog_empty_file_gives_empty_version()
    {
        var f = _repo.Write("c.txt", "");

        var (version, notes) = ModScanner.ReadChangelog(f, _cfg);

        Assert.Equal("", version);
        Assert.Equal("", notes);
    }

    // ------------------------------------------------------------ helpers

    [Fact]
    public void ReadXml_returns_null_for_missing_or_blank_values()
    {
        var f = _repo.Write("a.xml", "<Root><name>  Hello  </name><blank>   </blank></Root>");

        Assert.Equal("Hello", ModScanner.ReadXml(f, "Root/name"));
        Assert.Equal("Hello", ModScanner.ReadXml(f, "/Root/name"));
        Assert.Null(ModScanner.ReadXml(f, "Root/blank"));
        Assert.Null(ModScanner.ReadXml(f, "Root/missing"));
        Assert.Null(ModScanner.ReadXml(f, ""));
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
}
