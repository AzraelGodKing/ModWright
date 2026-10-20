using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Translation gaps, Harmony checks and adding a new RimWorld version to a mod.</summary>
public sealed class RimWorldExtrasTests : IDisposable
{
    readonly TempRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    static string Keyed(params string[] keys) =>
        "<?xml version=\"1.0\"?><LanguageData>" + string.Concat(keys.Select(k => $"<{k}>text</{k}>")) + "</LanguageData>";

    [Fact]
    public void Missing_and_leftover_keyed_texts_are_named_per_language()
    {
        var dir = _repo.AddMod("Mod");
        _repo.Write("Mod/Languages/English/Keyed/Main.xml", Keyed("A", "B", "C"));
        _repo.Write("Mod/Languages/German/Keyed/Main.xml", Keyed("A", "Old"));
        _repo.Write("Mod/Languages/French/DefInjected/ThingDef/x.xml", Keyed("X")); // no Keyed folder: nothing to compare
        _repo.Write("Mod/1.6/Languages/English/Keyed/V.xml", Keyed("V1"));
        _repo.Write("Mod/1.6/Languages/Russian/Keyed/V.xml", Keyed("V1"));
        var w = RimWorldLint.TranslationGaps(dir);
        Assert.Equal(2, w.Count);
        Assert.Contains("Languages/German is missing 2 of 3 English Keyed texts", w[0]);
        Assert.Contains("B, C", w[0]);
        Assert.Contains("Languages/German has 1 Keyed text English no longer has", w[1]);
        Assert.Contains(RimWorldLint.Check(dir).Warnings, x => x.Contains("German is missing"));
    }

    [Fact]
    public void Shipping_harmony_or_using_it_without_the_dependency_is_warned_about()
    {
        var dir = _repo.AddMod("Mod", built: false);
        _repo.Write("Mod/1.6/Assemblies/Mod.dll", "MZ ... 0Harmony ... HarmonyLib");
        var meta = System.Xml.Linq.XDocument.Load(Path.Combine(dir, "About", "About.xml")).Root!;
        var w = RimWorldLint.HarmonyWarnings(dir, meta);
        Assert.Contains("doesn't list brrainz.harmony", Assert.Single(w));

        _repo.Write("Mod/1.6/Assemblies/0Harmony.dll", "harmony");
        w = RimWorldLint.HarmonyWarnings(dir, meta);
        Assert.Equal(2, w.Count);
        Assert.Contains("ships its own 1.6/Assemblies/0Harmony.dll", w[0]);

        meta.Add(new System.Xml.Linq.XElement("modDependencies", new System.Xml.Linq.XElement("li", new System.Xml.Linq.XElement("packageId", "brrainz.harmony"))));
        Assert.Single(RimWorldLint.HarmonyWarnings(dir, meta));
        File.Delete(Path.Combine(dir, "1.6", "Assemblies", "0Harmony.dll"));
        Assert.Empty(RimWorldLint.HarmonyWarnings(dir, meta));
    }

    [Fact]
    public void A_mod_without_harmony_is_left_alone()
    {
        var dir = _repo.AddMod("Mod");
        Assert.DoesNotContain(RimWorldLint.Check(dir).Warnings, x => x.Contains("Harmony"));
    }

    [Fact]
    public void Adding_a_version_edits_supported_versions_as_text()
    {
        var dir = _repo.AddMod("Mod");
        var about = Path.Combine(dir, "About", "About.xml");
        File.WriteAllText(about, "<?xml version=\"1.0\"?>\r\n<ModMetaData>\r\n  <!-- keep me -->\r\n  <supportedVersions>\r\n    <li>1.5</li>\r\n    <li>1.6</li>\r\n  </supportedVersions>\r\n</ModMetaData>\r\n");
        var r = GameVersions.Add(dir, "1.7", copyFolder: true);
        Assert.Null(r.Error);
        Assert.Equal(new[] { "added 1.7 to supportedVersions" }, r.Done);
        Assert.Equal("<?xml version=\"1.0\"?>\r\n<ModMetaData>\r\n  <!-- keep me -->\r\n  <supportedVersions>\r\n    <li>1.5</li>\r\n    <li>1.6</li>\r\n    <li>1.7</li>\r\n  </supportedVersions>\r\n</ModMetaData>\r\n", File.ReadAllText(about));
        Assert.Empty(GameVersions.Add(dir, "1.7", true).Done); // already there
        Assert.NotNull(GameVersions.Add(dir, "seven", true).Error);
    }

    [Fact]
    public void Version_folders_and_load_folders_follow_the_newest_one()
    {
        var dir = _repo.AddMod("Mod", supportedVersions: new[] { "1.5", "1.6" });
        _repo.Write("Mod/1.5/Defs/a.xml", "<Defs/>");
        _repo.Write("Mod/1.6/Defs/a.xml", "<Defs>16</Defs>");
        _repo.Write("Mod/1.6/Assemblies/Mod.dll", "dll");
        _repo.Write("Mod/1.6/Patches/p.xml", "<Patch/>");
        _repo.Write("Mod/Common/Defs/c.xml", "<Defs/>");
        _repo.Write("Mod/loadFolders.xml", "<loadFolders>\n  <v1.5>\n    <li>/</li>\n    <li>1.5</li>\n  </v1.5>\n  <v1.6>\n    <li>/</li>\n    <li>Common</li>\n    <li>1.6</li>\n    <li>1.6/Patches</li>\n  </v1.6>\n</loadFolders>\n");
        var r = GameVersions.Add(dir, "1.7", copyFolder: true);
        Assert.Equal(3, r.Done.Count);
        Assert.Equal("<Defs>16</Defs>", File.ReadAllText(Path.Combine(dir, "1.7", "Defs", "a.xml")));
        Assert.True(File.Exists(Path.Combine(dir, "1.7", "Assemblies", "Mod.dll")));
        var lf = File.ReadAllText(Path.Combine(dir, "loadFolders.xml"));
        Assert.Contains("  </v1.6>\n  <v1.7>\n    <li>/</li>\n    <li>Common</li>\n    <li>1.7</li>\n    <li>1.7/Patches</li>\n  </v1.7>\n</loadFolders>", lf);
        Assert.DoesNotContain(RimWorldLint.Check(dir).Warnings, w => w.Contains("1.7"));
    }

    [Fact]
    public void Without_copying_only_about_changes()
    {
        var dir = _repo.AddMod("Mod");
        _repo.Write("Mod/1.6/Defs/a.xml", "<Defs/>");
        Assert.Single(GameVersions.Add(dir, "1.7", copyFolder: false).Done);
        Assert.False(Directory.Exists(Path.Combine(dir, "1.7")));
        Assert.Equal("1.7", GameVersions.Next("1.6"));
        Assert.Equal("", GameVersions.Next("x"));
    }

    async Task<(int code, string text)> Run(params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, _repo.Root, "steamcmd", "modder", sw, CancellationToken.None, Path.Combine(_repo.Root, "uploaded.json"));
        return (code, sw.ToString());
    }

    [Fact]
    public async Task Game_version_command_adds_to_named_or_all_mods()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        var (code, text) = await Run("game-version", "1.7", "Alpha", "--no-copy");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Alpha: added 1.7 to supportedVersions.", text);
        Assert.DoesNotContain("<li>1.7</li>", File.ReadAllText(Path.Combine(_repo.Root, "Beta", "About", "About.xml")));
        (code, text) = await Run("game-version", "1.7", "--all");
        Assert.Contains("Alpha: already supports 1.7.", text);
        Assert.Contains("Beta: added 1.7", text);
        Assert.Equal(Cli.ExitUsage, (await Run("game-version", "1.7")).code);
        Assert.Equal(Cli.ExitUsage, (await Run("list", "--no-copy")).code);
    }

    [Fact]
    public void Ticked_mods_get_their_own_next_version_and_one_changelog_line()
    {
        _repo.AddMod("Alpha", version: "1.2.3");
        _repo.AddMod("Beta", version: "2.0.0", changelog: "2.0.0\n- Old.");
        var cfg = ProjectConfig.Load(_repo.Root);
        var mods = ModScanner.Scan(_repo.Root, cfg);
        var log = new List<string>();
        Assert.True(VersionBatch.Apply(mods, cfg, "minor", "- Works with the new update.", log.Add));
        Assert.Equal(new[] { "1.3.0", "2.1.0" }, mods.Select(m => m.Version).ToArray());
        var again = ModScanner.Scan(_repo.Root, cfg);
        Assert.All(again, m => Assert.True(m.Ready, m.StatusText));
        Assert.Contains("1.3.0", File.ReadAllText(Path.Combine(_repo.Root, "Alpha", "About", "changelog.txt")));
        Assert.Equal("1.4.0", VersionBatch.NewVersion(new ModInfo { Version = "9" }, "v1.4.0"));
        Assert.Null(VersionBatch.NewVersion(new ModInfo { Version = "abc" }, "patch"));
    }
}
