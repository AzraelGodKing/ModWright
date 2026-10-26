using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Writing a changelog entry for a new version, so a RimWorld upload is not refused for lack of one.</summary>
public class ChangelogEntryTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld();

    public void Dispose() => _repo.Dispose();

    ModInfo ScanOne() => Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

    string Changelog => File.ReadAllText(Path.Combine(_repo.Root, "Alpha", "About", "changelog.txt"));

    [Fact]
    public void New_entry_goes_above_the_newest_and_below_the_comments()
    {
        var text = ModScanner.InsertChangelogEntry("# My mod\n\n1.0.1 - Fix\n1.0.0 - First\n", "1.1.0", "- Added a thing");

        Assert.Equal("# My mod\n\n1.1.0\n- Added a thing\n\n1.0.1 - Fix\n1.0.0 - First\n", text);
        Assert.Equal("1.1.0\n- Added a thing", ModScanner.ChangenoteFor(text.Split('\n'), "1.1.0"));
        Assert.Equal("1.0.1 - Fix", ModScanner.ChangenoteFor(text.Split('\n'), "1.0.1"));
    }

    [Fact]
    public void New_entry_goes_at_the_end_when_the_oldest_version_is_first()
    {
        var text = ModScanner.InsertChangelogEntry("1.0.0 First\n1.0.9 Fix\n\n", "1.0.10", "Better");

        Assert.Equal("1.0.0 First\n1.0.9 Fix\n\n1.0.10\nBetter\n", text);
    }

    [Fact]
    public void Windows_line_endings_are_kept()
    {
        var text = ModScanner.InsertChangelogEntry("1.0.0\r\n- First\r\n", "1.0.1", "- One\n- Two");

        Assert.Equal("1.0.1\r\n- One\r\n- Two\r\n\r\n1.0.0\r\n- First\r\n", text);
    }

    [Fact]
    public void Note_lines_that_would_break_the_entry_are_prefixed()
    {
        var text = ModScanner.InsertChangelogEntry("", "2.0.0", "# headline\n1.5 is no longer supported");

        Assert.Equal("2.0.0\n- # headline\n- 1.5 is no longer supported\n", text);
        Assert.Equal("2.0.0\n- # headline\n- 1.5 is no longer supported", ModScanner.ChangenoteFor(text.Split('\n'), "2.0.0"));
    }

    [Fact]
    public void Adding_the_entry_clears_the_missing_entry_problem()
    {
        _repo.AddMod("Alpha", changelog: "1.2.3\n- Old");
        Assert.Null(ModScanner.SetVersion(ScanOne(), _cfg, "1.3.0"));
        var m = ScanOne();
        Assert.True(ModScanner.NeedsChangelogEntry(m, _cfg));
        Assert.Contains(m.Problems, p => p.Contains("no entry for 1.3.0"));

        Assert.Null(ModScanner.AddChangelogEntry(m, _cfg, "1.3.0", "- New stuff"));

        m = ScanOne();
        Assert.Empty(m.Problems);
        Assert.False(ModScanner.NeedsChangelogEntry(m, _cfg));
        Assert.StartsWith("1.3.0\n- New stuff\n\n1.2.3", Changelog);
    }

    [Fact]
    public void A_missing_changelog_is_created()
    {
        _repo.AddMod("Alpha", changelog: null);
        var m = ScanOne();
        Assert.True(ModScanner.NeedsChangelogEntry(m, _cfg));

        Assert.Null(ModScanner.AddChangelogEntry(m, _cfg, m.Version, "First release"));

        Assert.Equal("1.2.3\nFirst release\n", Changelog);
        Assert.Empty(ScanOne().Problems);
    }

    [Fact]
    public void Empty_notes_and_existing_entries_are_refused()
    {
        _repo.AddMod("Alpha");
        var m = ScanOne();

        Assert.NotNull(ModScanner.AddChangelogEntry(m, _cfg, "1.3.0", "  \n "));
        Assert.NotNull(ModScanner.AddChangelogEntry(m, _cfg, "1.2.3", "again"));
        Assert.Equal("1.2.3\n- Fixed the thing that was broken.", Changelog);
    }
}
