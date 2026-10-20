using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>RimWorld-only checks: packageId, XML that parses, loadFolders.xml and version folders.</summary>
public sealed class RimWorldLintTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld(c => c.UseGitArchive = false);

    public void Dispose() => _repo.Dispose();

    void About(string folder, string inner) =>
        _repo.Write($"{folder}/About/About.xml", $"<?xml version=\"1.0\"?><ModMetaData><name>{folder}</name><modVersion>1.2.3</modVersion>{inner}</ModMetaData>");

    [Fact]
    public void A_clean_mod_has_nothing_to_say()
    {
        var dir = _repo.AddMod("Alpha", supportedVersions: new[] { "1.5", "1.6" });
        _repo.Write("Alpha/1.6/Defs/Things.xml", "<Defs><ThingDef><defName>A</defName></ThingDef></Defs>");
        var (problems, warnings) = RimWorldLint.Check(dir);
        Assert.Empty(problems);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("<packageId>Me.Mod</packageId>", false)]
    public void A_missing_package_id_blocks_the_upload(string packageId, bool blocked)
    {
        _repo.AddMod("Alpha");
        About("Alpha", packageId + "<supportedVersions><li>1.6</li></supportedVersions>");
        var m = ModScanner.Scan(_repo.Root, _cfg).Single();
        Assert.Equal(blocked, m.Problems.Any(p => p.Contains("no <packageId>")));
    }

    [Fact]
    public void Odd_package_ids_and_unchecked_dependencies_are_warned_about()
    {
        var dir = _repo.AddMod("Alpha");
        About("Alpha", "<packageId>my mod</packageId><supportedVersions><li>1.6</li></supportedVersions>"
            + "<modDependencies><li><displayName>Harmony</displayName><steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li></modDependencies>");
        var (_, warnings) = RimWorldLint.Check(dir);
        Assert.Contains(warnings, w => w.Contains("packageId \"my mod\" is not valid"));
        Assert.Contains(warnings, w => w.Contains("(Harmony) has no packageId"));
    }

    [Fact]
    public void Broken_xml_in_defs_and_patches_blocks_but_source_folders_are_skipped()
    {
        var dir = _repo.AddMod("Alpha");
        _repo.Write("Alpha/Defs/Good.xml", "<Defs/>");
        _repo.Write("Alpha/1.6/Patches/Bad.xml", "<Patch><Operation></Patch>");
        _repo.Write("Alpha/Source/obj/Junk.xml", "<not closed>");
        _repo.Write("Alpha/Textures/Readme.xml", "<also not closed>"); // RimWorld doesn't parse XML there

        var (problems, _) = RimWorldLint.Check(dir);

        var p = Assert.Single(problems);
        Assert.StartsWith("1.6/Patches/Bad.xml is not valid XML (line 1", p);

        _repo.Write("Alpha/1.6/Patches/Bad.xml", "<Patch><Operation/></Patch>"); // fixed: read again, not taken from the cache
        File.SetLastWriteTimeUtc(Path.Combine(dir, "1.6", "Patches", "Bad.xml"), DateTime.UtcNow.AddMinutes(1));
        Assert.Empty(RimWorldLint.Check(dir).Problems);
    }

    [Fact]
    public void Version_folders_must_match_supported_versions()
    {
        var dir = _repo.AddMod("Alpha", supportedVersions: new[] { "1.6" });
        Directory.CreateDirectory(Path.Combine(dir, "1.5"));
        Directory.CreateDirectory(Path.Combine(dir, "1.6"));
        var w = Assert.Single(RimWorldLint.Check(dir).Warnings);
        Assert.Contains("has a 1.5 folder, but About.xml does not list 1.5", w);
    }

    [Fact]
    public void Load_folders_must_exist_and_cover_every_supported_version()
    {
        var dir = _repo.AddMod("Alpha", supportedVersions: new[] { "1.5", "1.6" });
        Directory.CreateDirectory(Path.Combine(dir, "1.6"));
        _repo.Write("Alpha/loadFolders.xml", "<loadFolders><v1.6><li>/</li><li>1.6</li><li>Compat/Gone</li></v1.6></loadFolders>");

        var warnings = RimWorldLint.Check(dir).Warnings;

        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("lists \"Compat/Gone\" for v1.6, but there is no such folder"));
        Assert.Contains(warnings, w => w.Contains("About.xml lists 1.5, but loadFolders.xml has no v1.5 entry"));
    }

    [Fact]
    public void Two_mods_with_one_package_id_are_both_warned()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        About("Beta", "<packageId>Test.Alpha</packageId><supportedVersions><li>1.6</li></supportedVersions>");
        var mods = ModScanner.Scan(_repo.Root, _cfg);
        Assert.Contains(mods[0].Warnings, w => w.Contains("packageId Test.Alpha is also used by Beta"));
        Assert.Contains(mods[1].Warnings, w => w.Contains("also used by Alpha"));
    }

    [Fact]
    public void Only_rimworld_gets_these_checks()
    {
        Assert.True(RimWorldLint.Applies(_cfg));
        Assert.False(RimWorldLint.Applies(ProjectConfig.GetPreset("Generic")));
    }
}
