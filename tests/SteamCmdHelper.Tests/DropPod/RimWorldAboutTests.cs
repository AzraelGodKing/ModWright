using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Reading About.xml, including the messy files real mods ship.</summary>
public sealed class RimWorldAboutTests
{
    const string Full = """
        <?xml version="1.0" encoding="utf-8"?>
        <ModMetaData>
          <name>Better Storage</name>
          <author>Kat, Azrael</author>
          <authors><li>Azrael</li><li>Helper</li></authors>
          <packageId>Kat.BetterStorage</packageId>
          <modVersion>2.1.0</modVersion>
          <url>https://example.com</url>
          <description>Shelves that hold more &amp; better.</description>
          <supportedVersions><li>1.4</li><li>1.5</li></supportedVersions>
          <modDependencies>
            <li>
              <packageId>brrainz.harmony</packageId>
              <displayName>Harmony</displayName>
              <steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl>
            </li>
            <li><displayName>No id or link: kept, so the lint can name it</displayName></li>
          </modDependencies>
          <modDependenciesByVersion>
            <v1.4>
              <li><packageId>Old.Lib</packageId><downloadUrl>https://steamcommunity.com/sharedfiles/filedetails/?id=1234567</downloadUrl></li>
            </v1.4>
          </modDependenciesByVersion>
          <loadAfter><li>Ludeon.RimWorld</li><li>ludeon.rimworld</li><li>Other.Mod</li></loadAfter>
          <loadAfterByVersion><v1.5><li>New.Mod</li></v1.5></loadAfterByVersion>
          <loadBefore><li>Late.Mod</li></loadBefore>
          <forceLoadAfter><li>Must.Before</li></forceLoadAfter>
          <incompatibleWith><li>Bad.Mod</li></incompatibleWith>
        </ModMetaData>
        """;

    [Fact]
    public void Reads_every_field()
    {
        var m = RimWorldAbout.Parse(Full, "/mods/BetterStorage");
        Assert.Equal("Better Storage", m.Name);
        Assert.Equal("Kat.BetterStorage", m.Id);
        Assert.Equal("kat.betterstorage", m.Key);
        Assert.Equal(new[] { "Azrael", "Helper", "Kat" }, m.Authors.OrderBy(a => a));
        Assert.Equal("Shelves that hold more & better.", m.Description);
        Assert.Equal("2.1.0", m.ModVersion);
        Assert.Equal(new[] { "1.4", "1.5" }, m.SupportedVersions);
        Assert.Equal(2, m.Dependencies.All.Count);
        Assert.Equal("", m.Dependencies.All[1].Id);
        var dep = m.Dependencies.All[0];
        Assert.Equal(("brrainz.harmony", "Harmony", "2009463077"), (dep.Key, dep.DisplayName, dep.WorkshopId));
        Assert.Equal("1234567", Assert.Single(m.Dependencies.For("1.4.3901 rev123")).WorkshopId);
        Assert.Equal("brrainz.harmony", m.Dependencies.For("1.5")[0].Key);
        Assert.Equal(new[] { "Ludeon.RimWorld", "Other.Mod" }, m.LoadAfter.All); // duplicates differing only in case are one entry
        Assert.Equal(new[] { "New.Mod" }, m.LoadAfter.For("1.5"));
        Assert.Equal(new[] { "Late.Mod" }, m.LoadBefore.All);
        Assert.Equal(new[] { "Must.Before" }, m.ForceLoadAfter.All);
        Assert.Equal(new[] { "Bad.Mod" }, m.Incompatible.All);
        Assert.Empty(m.Issues);
        Assert.True(m.Supports("1.5.4104 rev435"));
        Assert.False(m.Supports("1.6"));
    }

    [Fact]
    public void Element_names_in_any_case_and_old_target_version_are_read()
    {
        var m = RimWorldAbout.Parse("<ModMetaData><Name>Old</Name><PACKAGEID>a.b</PACKAGEID><targetVersion>1.0.2408</targetVersion></ModMetaData>");
        Assert.Equal(("Old", "a.b"), (m.Name, m.Id));
        Assert.Equal(new[] { "1.0" }, m.SupportedVersions);
    }

    [Fact]
    public void A_mod_without_packageId_gets_a_folder_key_and_a_note()
    {
        var m = RimWorldAbout.Parse(FakeRimWorld.About("No Id", null, "1.5"), "/mods/NoIdMod");
        Assert.Equal("folder:noidmod", m.Key);
        Assert.Contains(m.Issues, i => i.Contains("no packageId"));
    }

    [Fact]
    public void An_invalid_packageId_is_noted()
    {
        var m = RimWorldAbout.Parse(FakeRimWorld.About("Bad", "no dots here", "1.5"));
        Assert.Contains(m.Issues, i => i.Contains("not valid"));
    }

    [Fact]
    public void Broken_xml_is_read_as_far_as_possible()
    {
        const string broken = """
            <ModMetaData>
              <name>Fish & Chips</name>
              <packageId>Cook.FishChips</packageId>
              <supportedVersions><li>1.5</li></supportedVersions>
              <modDependencies><li><packageId>brrainz.harmony</packageId><steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li></modDependencies>
              <loadAfter><li>A.B</li></loadAfter>
            """;
        var m = RimWorldAbout.Parse(broken);
        Assert.Equal("Fish & Chips", m.Name);
        Assert.Equal("Cook.FishChips", m.Id);
        Assert.Equal(new[] { "1.5" }, m.SupportedVersions);
        Assert.Equal("2009463077", Assert.Single(m.Dependencies.All).WorkshopId);
        Assert.Equal(new[] { "A.B" }, m.LoadAfter.All);
        Assert.Contains(m.Issues, i => i.Contains("not valid XML"));
    }

    [Fact]
    public void Empty_or_wrong_root_is_noted_not_thrown()
    {
        Assert.Contains(RimWorldAbout.Parse("").Issues, i => i.Contains("not valid XML"));
        Assert.Contains(RimWorldAbout.Parse("<Something><name>x</name></Something>").Issues, i => i.Contains("<Something>"));
    }

    [Fact]
    public void Dtds_are_never_followed()
    {
        const string xxe = """<?xml version="1.0"?><!DOCTYPE m [<!ENTITY x SYSTEM "file:///etc/passwd">]><ModMetaData><name>&x;</name><packageId>a.b</packageId></ModMetaData>""";
        var m = RimWorldAbout.Parse(xxe);
        Assert.DoesNotContain("root", m.Name);
    }

    [Fact]
    public void Folder_reading_finds_files_in_any_case_and_the_workshop_id()
    {
        var dir = Path.Combine(Path.GetTempPath(), "droppod_about_" + Guid.NewGuid().ToString("N"), "2009463077");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "about"));
            File.WriteAllText(Path.Combine(dir, "about", "about.xml"), FakeRimWorld.About("Harmony", "brrainz.harmony", "1.5"));
            File.WriteAllBytes(Path.Combine(dir, "about", "preview.png"), new byte[] { 1 });
            var m = RimWorldAbout.ReadFolder(dir, ModSource.Workshop)!;
            Assert.Equal("Harmony", m.Name);
            Assert.Equal("2009463077", m.WorkshopId);
            Assert.NotNull(m.PreviewPath);
            Assert.Null(RimWorldAbout.ReadFolder(Path.GetDirectoryName(dir)!, ModSource.Local)); // no About.xml: not a mod
            File.WriteAllText(Path.Combine(dir, "about", "PublishedFileId.txt"), " 555666 \n");
            Assert.Equal("555666", RimWorldAbout.WorkshopIdOf(dir, ModSource.Local));
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [Theory]
    [InlineData("1.5.4104 rev435", "1.5")]
    [InlineData("v1.4", "1.4")]
    [InlineData("1.05", "1.5")]
    [InlineData("beta", "beta")]
    public void Game_versions_shorten_to_major_minor(string full, string expected) => Assert.Equal(expected, GameVersion.Short(full));

    [Fact]
    public void Steam_suffix_and_official_ids()
    {
        Assert.Equal("a.b", RimWorldIds.WithoutSteamSuffix("a.b_steam"));
        Assert.True(RimWorldIds.IsOfficial("ludeon.rimworld.biotech"));
        Assert.False(RimWorldIds.IsOfficial("ludeon.rimworldish"));
    }
}
