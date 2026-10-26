using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The change note template, and the version checks around change notes.</summary>
public sealed class ChangeNoteShapeTests : IDisposable
{
    readonly TempRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void Template_fills_its_placeholders_once()
    {
        var today = new DateTime(2026, 10, 17);
        Assert.Equal("1.2.0 (2026-10-17)\n- Fixed {name}\n\nThanks for playing My Mod!",
            ChangeNotes.Apply("{version} ({date})\n{note}\n\nThanks for playing {name}!", "- Fixed {name}", "My Mod", "1.2.0", "RimWorld", today));
        Assert.Equal("Update for RimWorld\n\nthe note", ChangeNotes.Apply("Update for {game}", "the note", "M", "1", "RimWorld", today));
        Assert.Equal("the note", ChangeNotes.Apply("  ", "the note", "M", "1", "G", today));
    }

    [Fact]
    public void Settings_keep_a_template_per_folder()
    {
        var s = new AppSettings();
        s.SetTemplate(@"C:\mods\", "{version}\r\n{note}");
        Assert.Equal("{version}\n{note}", s.TemplateFor(@"c:\mods"));
        s.SetTemplate(@"C:\mods", "");
        Assert.Equal("", s.TemplateFor(@"C:\mods"));
    }

    [Fact]
    public void Scan_warns_when_the_changelog_is_ahead_of_the_version()
    {
        _repo.AddMod("Ahead", version: "1.2.3", changelog: "1.3.0\n- next\n1.2.3\n- now");
        _repo.AddMod("Even", version: "1.2", changelog: "1.2.0\n- same\n1.2\n- now");
        var mods = ModScanner.Scan(_repo.Root, TestConfig.RimWorld());
        Assert.Contains(mods.Single(m => m.Folder == "Ahead").Warnings, w => w.Contains("entry for 1.3.0"));
        Assert.DoesNotContain(mods.Single(m => m.Folder == "Even").Warnings, w => w.Contains("newer than"));
    }

    [Fact]
    public void Scan_warns_when_manifest_and_about_disagree()
    {
        _repo.AddMod("Split", version: "1.0.0", changelog: "1.1.0\n- x");
        _repo.Write("Split/About/Manifest.xml", "<Manifest><version>1.1.0</version></Manifest>");
        var m = ModScanner.Scan(_repo.Root, TestConfig.RimWorld()).Single();
        Assert.Equal("1.1.0", m.Version);
        Assert.Contains(m.Warnings, w => w.Contains("About/Manifest.xml says 1.1.0") && w.Contains("says 1.0.0"));
        Assert.True(m.Ready);
    }

    [Fact]
    public void Newest_changelog_version_ignores_comments_and_short_versions_compare_equal()
    {
        Assert.Equal(new Version(1, 3, 0), ModScanner.NewestChangelogVersion(new[] { "# 9.9.9 header", "1.3.0", "- x", "1.2.0" }));
        Assert.Null(ModScanner.NewestChangelogVersion(new[] { "no versions here" }));
    }
}
