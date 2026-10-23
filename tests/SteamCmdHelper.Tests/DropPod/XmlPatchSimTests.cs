using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

public sealed class XmlPatchSimTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "xmlsim_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    XmlMod Mod(string key, string name, bool official = false, params (string Path, string Xml)[] files)
    {
        var dir = Path.Combine(_root, key);
        foreach (var (rel, xml) in files)
        {
            var f = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, xml);
        }
        Directory.CreateDirectory(dir);
        return new XmlMod(key, name, key, dir, official);
    }

    const string Core = """
        <Defs>
          <ThingDef Name="BaseGun" Abstract="True"><category>Item</category></ThingDef>
          <ThingDef ParentName="BaseGun"><defName>Gun_Rifle</defName><statBases><MarketValue>100</MarketValue><Mass>3</Mass></statBases></ThingDef>
          <ThingDef><defName>Gun_Pistol</defName><statBases><MarketValue>50</MarketValue></statBases></ThingDef>
        </Defs>
        """;

    static string Patch(params string[] ops) => "<Patch>" + string.Concat(ops) + "</Patch>";

    static string Replace(string xpath, string value) =>
        $"<Operation Class=\"PatchOperationReplace\"><xpath>{xpath}</xpath><value>{value}</value></Operation>";

    XmlMod CoreMod() => Mod("ludeon.rimworld", "Core", true, ("Defs/Guns.xml", Core));

    [Fact]
    public void Two_mods_replacing_the_same_value_is_a_clash_the_later_one_wins()
    {
        var a = Mod("a", "Mod A", false, ("Patches/P.xml", Patch(Replace("/Defs/ThingDef[defName=\"Gun_Rifle\"]/statBases/MarketValue", "<MarketValue>200</MarketValue>"))));
        var b = Mod("b", "Mod B", false, ("Patches/P.xml", Patch(Replace("Defs/ThingDef[defName=\"Gun_Rifle\"]/statBases/MarketValue", "<MarketValue>300</MarketValue>"))));
        var found = XmlPatchSim.Run(new[] { CoreMod(), a, b }, "1.5");
        var clash = Assert.Single(found, f => f.Kind == XmlFindingKind.PatchOverwrite);
        Assert.Equal(new[] { "a", "b" }, clash.Mods);
        Assert.Equal("ThingDef Gun_Rifle > statBases > MarketValue", Assert.Single(clash.Targets));
        Assert.Equal("replaces", clash.Detail);
    }

    [Fact]
    public void Patching_the_games_defs_alone_is_not_reported()
    {
        var a = Mod("a", "Mod A", false, ("Patches/P.xml", Patch(Replace("/Defs/ThingDef[defName=\"Gun_Rifle\"]/statBases/Mass", "<Mass>4</Mass>"))));
        Assert.Empty(XmlPatchSim.Run(new[] { CoreMod(), a }, "1.5"));
    }

    [Fact]
    public void A_patch_that_finds_nothing_fails_unless_it_may_fail()
    {
        var a = Mod("a", "Mod A", false, ("Patches/P.xml", Patch(
            Replace("/Defs/ThingDef[defName=\"Gun_Missing\"]/label", "<label>x</label>"),
            "<Operation Class=\"PatchOperationRemove\" MayRequire=\"some.other.mod\"><xpath>/Defs/ThingDef[defName=\"Nope\"]</xpath></Operation>",
            "<Operation Class=\"PatchOperationRemove\"><success>Always</success><xpath>/Defs/ThingDef[defName=\"Nope2\"]</xpath></Operation>",
            "<Operation Class=\"PatchOperationConditional\"><xpath>/Defs/ThingDef[defName=\"Nope3\"]</xpath><match Class=\"PatchOperationRemove\"><xpath>/Defs/ThingDef[defName=\"Nope3\"]</xpath></match></Operation>",
            "<Operation Class=\"PatchOperationFindMod\"><mods><li>Not Installed</li></mods><match Class=\"PatchOperationRemove\"><xpath>/Defs/X</xpath></match></Operation>")));
        var fails = Assert.Single(XmlPatchSim.Run(new[] { CoreMod(), a }, "1.5"), f => f.Kind == XmlFindingKind.PatchFails);
        Assert.Equal("/Defs/ThingDef[defName=\"Gun_Missing\"]/label", Assert.Single(fails.Targets));
        Assert.Equal("P.xml", fails.Detail);
    }

    [Fact]
    public void A_sequence_fails_at_its_first_miss()
    {
        var a = Mod("a", "Mod A", false, ("Patches/P.xml", Patch(
            "<Operation Class=\"PatchOperationSequence\"><operations>" +
            "<li Class=\"PatchOperationAdd\"><xpath>/Defs/ThingDef[defName=\"Gun_Pistol\"]</xpath><value><label>pistol</label></value></li>" +
            "<li Class=\"PatchOperationReplace\"><xpath>/Defs/ThingDef[defName=\"Gun_Pistol\"]/nothing</xpath><value><x/></value></li>" +
            "</operations></Operation>")));
        var fails = Assert.Single(XmlPatchSim.Run(new[] { CoreMod(), a }, "1.5"), f => f.Kind == XmlFindingKind.PatchFails);
        Assert.Contains("/nothing", Assert.Single(fails.Targets));
    }

    [Fact]
    public void A_mod_defining_a_def_another_mod_has_overrides_it()
    {
        var a = Mod("a", "Mod A", false, ("Defs/D.xml", "<Defs><ThingDef><defName>Shared_Thing</defName></ThingDef></Defs>"));
        var b = Mod("b", "Mod B", false, ("Defs/D.xml", "<Defs><ThingDef><defName>Shared_Thing</defName></ThingDef><ThingDef><defName>Gun_Pistol</defName></ThingDef></Defs>"));
        var found = XmlPatchSim.Run(new[] { CoreMod(), a, b }, "1.5").Where(f => f.Kind == XmlFindingKind.DefOverride).ToList();
        Assert.Contains(found, f => f.Mods.SequenceEqual(new[] { "a", "b" }) && f.Targets.SequenceEqual(new[] { "ThingDef Shared_Thing" }));
        Assert.Contains(found, f => f.Mods.SequenceEqual(new[] { "ludeon.rimworld", "b" }) && f.Targets.SequenceEqual(new[] { "ThingDef Gun_Pistol" }));
    }

    [Fact]
    public void Removing_what_another_mod_added_is_a_clash()
    {
        var a = Mod("a", "Mod A", false, ("Patches/P.xml", Patch("<Operation Class=\"PatchOperationAdd\"><xpath>/Defs/ThingDef[defName=\"Gun_Pistol\"]/statBases</xpath><value><Beauty>2</Beauty></value></Operation>")));
        var b = Mod("b", "Mod B", false, ("Patches/P.xml", Patch("<Operation Class=\"PatchOperationRemove\"><xpath>/Defs/ThingDef[defName=\"Gun_Pistol\"]/statBases</xpath></Operation>")));
        var clash = Assert.Single(XmlPatchSim.Run(new[] { CoreMod(), a, b }, "1.5"), f => f.Kind == XmlFindingKind.PatchOverwrite);
        Assert.Equal("removes", clash.Detail);
        Assert.Equal("ThingDef Gun_Pistol > statBases", clash.Targets[0]);
    }

    [Fact]
    public void Custom_operations_and_broken_files_are_noted()
    {
        var a = Mod("a", "Mod A", false,
            ("Patches/P.xml", Patch("<Operation Class=\"XmlExtensions.PatchOperationSafeAdd\"><xpath>/Defs</xpath></Operation>")),
            ("Defs/Broken.xml", "<Defs><ThingDef>"));
        var found = XmlPatchSim.Run(new[] { CoreMod(), a }, "1.5");
        Assert.Contains(found, f => f.Kind == XmlFindingKind.CustomPatches && f.Targets[0] == "XmlExtensions.PatchOperationSafeAdd");
        Assert.Contains(found, f => f.Kind == XmlFindingKind.BrokenFile && f.Targets[0] == "Broken.xml");
    }

    [Fact]
    public void Only_the_load_folders_for_the_game_version_are_read()
    {
        var a = Mod("a", "Mod A", false,
            ("loadFolders.xml", "<loadFolders><v1.5><li>/</li><li>1.5</li><li IfModActive=\"b\">Compat/B</li></v1.5></loadFolders>"),
            ("1.4/Patches/Old.xml", Patch(Replace("/Defs/OldThing", "<x/>"))),
            ("1.5/Patches/New.xml", Patch(Replace("/Defs/NewThing", "<x/>"))),
            ("Compat/B/Patches/B.xml", Patch(Replace("/Defs/BThing", "<x/>"))));
        var fails = XmlPatchSim.Run(new[] { CoreMod(), a }, "1.5").Where(f => f.Kind == XmlFindingKind.PatchFails).ToList();
        Assert.Equal(new[] { "New.xml" }, fails.Select(f => f.Detail));
    }

    [Fact]
    public void Without_loadFolders_the_newest_older_version_folder_is_read()
    {
        var dir = Path.Combine(_root, "v");
        foreach (var v in new[] { "1.3", "1.4", "1.6" }) Directory.CreateDirectory(Path.Combine(dir, v));
        Directory.CreateDirectory(Path.Combine(dir, "Common"));
        var folders = LoadFolders.For(dir, "1.5.4104 rev435");
        Assert.Equal(new[] { Path.Combine(dir, "1.4"), Path.Combine(dir, "Common"), dir }, folders);
    }

    [Fact]
    public void Insight_words_the_findings()
    {
        var a = Mod("a", "Mod A", false, ("Patches/P.xml", Patch(Replace("/Defs/ThingDef[defName=\"Gun_Rifle\"]/statBases/MarketValue", "<MarketValue>200</MarketValue>"))));
        var b = Mod("b", "Mod B", false, ("Patches/P.xml", Patch(Replace("/Defs/ThingDef[defName=\"Gun_Rifle\"]/statBases/MarketValue", "<MarketValue>300</MarketValue>"))));
        var items = Insight.Xml(XmlPatchSim.Run(new[] { CoreMod(), a, b }, "1.5"), k => k.ToUpperInvariant(), new[] { "ludeon.rimworld" });
        var item = Assert.Single(items);
        Assert.Equal(InsightKind.XmlClash, item.Kind);
        Assert.Contains("B replaces what A changed in 1 place", item.Message);
    }
}
