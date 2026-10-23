using Xunit;

namespace SteamCmdHelper.Tests;

public sealed class RimWorldXmlLintTests : IDisposable
{
    readonly string _mod = Path.Combine(Path.GetTempPath(), "xmllint_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_mod, true); } catch { }
    }

    void Write(string rel, string text)
    {
        var f = Path.Combine(_mod, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
    }

    [Fact]
    public void A_def_defined_twice_is_reported()
    {
        Write("Defs/A.xml", "<Defs><ThingDef><defName>My_Thing</defName></ThingDef></Defs>");
        Write("Defs/B.xml", "<Defs><ThingDef><defName>My_Thing</defName></ThingDef><ThingDef><defName>Other</defName></ThingDef></Defs>");
        var w = RimWorldXmlLint.Check(_mod, new[] { "1.5" });
        Assert.Contains(w, x => x.StartsWith("defines ThingDef My_Thing 2 times (Defs/A.xml, Defs/B.xml)"));
        Assert.DoesNotContain(w, x => x.Contains("Other"));
    }

    [Fact]
    public void A_missing_texture_in_the_mods_own_folder_is_reported_but_game_paths_are_not()
    {
        Write("Textures/Things/MyMod/Gun.png", "png");
        Write("Textures/Things/MyMod/Pawn_north.png", "png");
        Write("Textures/Things/MyMod/Random/a.png", "png");
        Write("Defs/A.xml", """
            <Defs>
              <ThingDef><defName>A</defName><graphicData><texPath>Things/MyMod/Gun</texPath></graphicData></ThingDef>
              <ThingDef><defName>B</defName><graphicData><texPath>Things/MyMod/Pawn</texPath></graphicData></ThingDef>
              <ThingDef><defName>C</defName><graphicData><texPath>Things/MyMod/Random</texPath></graphicData></ThingDef>
              <ThingDef><defName>D</defName><graphicData><texPath>Things/MyMod/Gnu</texPath></graphicData></ThingDef>
              <ThingDef><defName>E</defName><graphicData><texPath>Things/Item/Resource/Steel</texPath></graphicData></ThingDef>
            </Defs>
            """);
        var line = Assert.Single(RimWorldXmlLint.Check(_mod, new[] { "1.5" }));
        Assert.Contains("1 texture path points", line);
        Assert.Contains("Things/MyMod/Gnu (ThingDef D)", line);
    }

    [Fact]
    public void Keyed_texts_no_code_or_xml_mentions_are_reported()
    {
        var never = "Nev";
        var unused = "MyLintMod_" + never + "erUsed";
        Write("Languages/English/Keyed/Keys.xml", $"<LanguageData><MyLintMod_UsedInCode>x</MyLintMod_UsedInCode><MyLintMod_InXml>y</MyLintMod_InXml><{unused}>z</{unused}></LanguageData>");
        Write("Defs/A.xml", "<Defs><ThingDef><defName>A</defName><description>MyLintMod_InXml</description></ThingDef></Defs>");
        Directory.CreateDirectory(Path.Combine(_mod, "Assemblies"));
        File.Copy(typeof(RimWorldXmlLintTests).Assembly.Location, Path.Combine(_mod, "Assemblies", "Mod.dll"));
        Assert.Contains("MyLintMod_UsedInCode", RimWorldXmlLint.UserStrings(Path.Combine(_mod, "Assemblies", "Mod.dll")));
        var line = Assert.Single(RimWorldXmlLint.Check(_mod, new[] { "1.5" }));
        Assert.Contains("1 English Keyed text", line);
        Assert.Contains(unused, line);
        Assert.DoesNotContain("UsedInCode", line);
    }

    [Fact]
    public void Keyed_texts_without_code_are_not_checked()
    {
        Write("Languages/English/Keyed/Keys.xml", "<LanguageData><SomeVanillaKey>x</SomeVanillaKey></LanguageData>");
        Assert.Empty(RimWorldXmlLint.Check(_mod, new[] { "1.5" }));
    }
}
