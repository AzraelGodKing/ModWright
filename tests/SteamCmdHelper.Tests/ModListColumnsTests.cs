using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The data behind the mod list's Uploaded version, Version to upload, Preview and Status columns.</summary>
public class ModListColumnsTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld();

    public void Dispose() => _repo.Dispose();

    ModInfo ScanOne() => Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

    // ------------------------------------------------------------ uploaded version from Steam's changelog page

    const string Page = """
        <div class="changelog headline">
            Update: Jul 26, 2025 @ 5:58am </div>
        <div class="changelog author">by <a href="x">Me</a></div>
        <p id="1753534725">1.6.1 - Can now look for the ModVersion attribute<br>second line</p>
        <div class="changelog headline">
            Update: Jun 21, 2025 @ 1:46pm </div>
        <p id="1750538765">1.6.0 - Older</p>
        """;

    [Fact]
    public void Uploaded_version_is_read_from_the_newest_change_note()
    {
        Assert.Equal("1.6.1", UploadedVersions.FromChangelogPage(Page));
    }

    [Theory]
    [InlineData("<div class=\"changelog headline\">x</div><p id=\"1\">[Auto-generated text]: Update on 10/11/2026</p>")]
    [InlineData("<html>This item is private</html>")]
    public void Uploaded_version_is_null_when_the_note_has_no_version(string html)
    {
        Assert.Null(UploadedVersions.FromChangelogPage(html));
    }

    [Fact]
    public void Uploaded_version_accepts_a_leading_v()
    {
        Assert.Equal("2.0", UploadedVersions.FromChangelogPage("<div class=\"changelog headline\"></div><p id=\"9\">v2.0 big rewrite</p>"));
    }

    [Fact]
    public void Local_record_remembers_the_last_upload()
    {
        var file = Path.Combine(_repo.Root, "uploaded.json");
        UploadedVersions.Remember("123", "1.0.0", file);
        UploadedVersions.Remember("123", "1.1.0", file);
        UploadedVersions.Remember("456", "2.0.0", file);

        var all = UploadedVersions.LoadRecord(file);
        Assert.Equal("1.1.0", all["123"]);
        Assert.Equal("2.0.0", all["456"]);
    }

    // ------------------------------------------------------------ editing the version to upload

    [Fact]
    public void Editing_the_version_updates_about_xml()
    {
        _repo.AddMod("Alpha");

        Assert.Null(ModScanner.SetVersion(ScanOne(), _cfg, "1.3.0"));
        Assert.Equal("1.3.0", ModScanner.ReadMeta(Path.Combine(_repo.Root, "Alpha", "About", "About.xml"), "ModMetaData/modVersion"));
    }

    [Fact]
    public void Editing_the_version_updates_the_manifest_and_about_xml()
    {
        _repo.AddMod("Alpha");
        var manifest = _repo.Write("Alpha/About/Manifest.xml", "<?xml version=\"1.0\"?>\n<Manifest>\n  <version>1.2.3</version>\n</Manifest>");

        Assert.Null(ModScanner.SetVersion(ScanOne(), _cfg, "2.0.0"));
        Assert.Equal("2.0.0", ModScanner.ReadMeta(manifest, "Manifest/version"));
        Assert.Equal("2.0.0", ModScanner.ReadMeta(Path.Combine(_repo.Root, "Alpha", "About", "About.xml"), "ModMetaData/modVersion"));
    }

    [Fact]
    public void Editing_adds_mod_version_when_about_xml_has_none()
    {
        _repo.AddMod("Alpha", version: null);

        Assert.Null(ModScanner.SetVersion(ScanOne(), _cfg, "0.1"));
        Assert.Equal("0.1", ModScanner.ReadMeta(Path.Combine(_repo.Root, "Alpha", "About", "About.xml"), "ModMetaData/modVersion"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.2")]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    public void Editing_refuses_a_value_that_is_not_a_version(string value)
    {
        _repo.AddMod("Alpha");

        Assert.NotNull(ModScanner.SetVersion(ScanOne(), _cfg, value));
        Assert.Equal("1.2.3", ScanOne().Version);
    }

    [Fact]
    public void Editing_is_refused_when_the_layout_has_no_xml_version()
    {
        var generic = new ProjectConfig();
        _repo.Write("Gamma/readme.txt", "hi");
        var m = Assert.Single(ModScanner.Scan(_repo.Root, generic));

        Assert.NotNull(ModScanner.SetVersion(m, generic, "1.0.0"));
    }

    // ------------------------------------------------------------ preview and status columns

    [Fact]
    public void Preview_is_ok_when_present_and_small()
    {
        _repo.AddMod("Alpha");

        Assert.True(ScanOne().PreviewOk);
    }

    [Fact]
    public void Preview_is_not_ok_when_missing_or_too_big_even_for_existing_items()
    {
        _repo.AddMod("Alpha", preview: false);
        var missing = ScanOne();
        Assert.False(missing.PreviewOk);
        Assert.True(missing.Ready); // existing items keep their preview on Steam

        _repo.Write("Alpha/About/Preview.png", new string('x', (int)ModScanner.MaxPreviewBytes + 1));
        Assert.False(ScanOne().PreviewOk);
    }

    [Fact]
    public void Preview_is_not_applicable_when_the_layout_has_none()
    {
        _cfg.Preview = "";
        _repo.AddMod("Alpha");

        Assert.Null(ScanOne().PreviewOk);
    }

    [Fact]
    public void Status_label_is_ready_fix_or_ready_to_create()
    {
        _repo.AddMod("Alpha", built: false);
        Assert.Equal("Ready", ScanOne().StatusLabel); // a warning does not change the label

        File.Delete(Path.Combine(_repo.Root, "Alpha", "About", "changelog.txt"));
        Assert.Equal("Fix", ScanOne().StatusLabel);
    }

    [Fact]
    public void Status_label_for_a_new_item_is_ready_to_create()
    {
        _repo.AddMod("Alpha", workshopId: null);

        Assert.Equal("Ready to create (private)", ScanOne().StatusLabel);
    }
}
