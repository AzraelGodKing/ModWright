using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>The About.xml check: what it warns about, what blocks, and how the upload flow and the CLI use it.</summary>
public sealed class AboutLintTests : IDisposable
{
    readonly TempRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    static List<LintFinding> Lint(string inner) =>
        AboutLint.Check(RimWorldAbout.Parse($"<ModMetaData>{inner}</ModMetaData>"));

    const string Good = "<name>Good</name><author>Me</author><packageId>Me.Good</packageId><description>Fine.</description><supportedVersions><li>1.5</li><li>1.6</li></supportedVersions>";

    [Fact]
    public void A_complete_about_has_no_findings() => Assert.Empty(Lint(Good
        + "<modDependencies><li><packageId>brrainz.harmony</packageId><displayName>Harmony</displayName><steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li></modDependencies>"
        + "<loadAfter><li>brrainz.harmony</li></loadAfter><loadAfterByVersion><v1.6><li>Other.Mod</li></v1.6></loadAfterByVersion>"));

    [Fact]
    public void Missing_fields_are_warnings_and_a_missing_package_id_blocks()
    {
        var f = Lint("<supportedVersions><li>1.6</li></supportedVersions>");
        Assert.Equal(LintLevel.Block, f.Single(x => x.Rule == "about-no-packageid").Level);
        Assert.Contains(f, x => x.Rule == "about-no-name" && x.Level == LintLevel.Warn);
        Assert.Contains(f, x => x.Rule == "about-no-author");
        Assert.Contains(f, x => x.Rule == "about-no-description");
        Assert.Contains(Lint("<name>A</name><author>Me</author><packageId>Me.A</packageId>"), x => x.Rule == "about-no-versions");
    }

    [Fact]
    public void A_1_0_only_mod_without_package_id_only_gets_a_warning()
    {
        var f = Lint("<name>Old</name><author>Me</author><description>x</description><targetVersion>1.0.2059</targetVersion>");
        Assert.Equal(LintLevel.Warn, f.Single(x => x.Rule == "about-no-packageid").Level);
    }

    [Fact]
    public void Odd_versions_and_ids_are_named()
    {
        var f = Lint("<name>A</name><author>Me</author><packageId>my mod</packageId><description>x</description><supportedVersions><li>1.5.4104</li></supportedVersions>");
        Assert.Contains(f, x => x.Rule == "about-bad-packageid" && x.Message.Contains("\"my mod\""));
        Assert.Contains(f, x => x.Rule == "about-version-form" && x.Message.Contains("such as 1.5"));
    }

    [Fact]
    public void Dependencies_without_an_id_a_link_or_a_valid_id_are_warned_about()
    {
        var f = Lint(Good + "<modDependencies>"
            + "<li><displayName>Harmony</displayName><steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li>"
            + "<li><packageId>Some.Lib</packageId></li>"
            + "<li><packageId>Not An Id</packageId><downloadUrl>https://example.com</downloadUrl></li>"
            + "</modDependencies>");
        Assert.Contains(f, x => x.Rule == "dep-no-packageid" && x.Message.Contains("(Harmony)"));
        Assert.Contains(f, x => x.Rule == "dep-no-link" && x.Message.Contains("Some.Lib"));
        Assert.Contains(f, x => x.Rule == "dep-bad-packageid" && x.Message.Contains("Not An Id"));
    }

    [Fact]
    public void Load_order_rules_that_contradict_themselves_are_warned_about()
    {
        var f = Lint(Good
            + "<loadAfter><li>me.good</li><li>Harmony</li><li>A.B</li></loadAfter>"
            + "<loadBefore><li>a.b</li></loadBefore>"
            + "<incompatibleWith><li>Lib.X</li></incompatibleWith>"
            + "<modDependencies><li><packageId>lib.x</packageId><steamWorkshopUrl>steam://url/CommunityFilePage/1234567</steamWorkshopUrl></li></modDependencies>"
            + "<loadAfterByVersion><v1.3><li>C.D</li></v1.3></loadAfterByVersion>");
        Assert.Contains(f, x => x.Rule == "rule-self" && x.Message.StartsWith("loadAfter"));
        Assert.Contains(f, x => x.Rule == "rule-not-packageid" && x.Message.Contains("\"Harmony\""));
        Assert.Contains(f, x => x.Rule == "rule-before-and-after" && x.Message.StartsWith("a.b"));
        Assert.Contains(f, x => x.Rule == "rule-needs-incompatible" && x.Message.StartsWith("lib.x"));
        Assert.Contains(f, x => x.Rule == "byversion-unsupported" && x.Message.Contains("1.3"));
    }

    [Fact]
    public void The_upload_scan_warns_and_strict_mode_blocks()
    {
        _repo.AddMod("Alpha");
        _repo.Write("Alpha/About/About.xml", "<ModMetaData><name>Alpha</name><modVersion>1.2.3</modVersion><packageId>Test.Alpha</packageId><supportedVersions><li>1.6</li></supportedVersions><description>x</description></ModMetaData>");
        var cfg = TestConfig.RimWorld();
        var relaxed = ModScanner.Scan(_repo.Root, cfg).Single();
        Assert.True(relaxed.Ready);
        Assert.Contains(relaxed.Warnings, w => w.Contains("no <author>"));
        var strict = ModScanner.Scan(_repo.Root, cfg, strictAbout: true).Single();
        Assert.False(strict.Ready);
        Assert.Contains(strict.Problems, p => p.Contains("no <author>"));
    }

    [Fact]
    public async Task The_lint_command_lists_findings_and_fails_when_a_mod_is_blocked()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        _repo.Write("Beta/About/About.xml", "<ModMetaData><name>Beta</name><author>Me</author><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>");
        async Task<(int, string)> Run(params string[] args)
        {
            var sw = new StringWriter();
            var code = await Cli.RunAsync(Cli.Parse(args), _repo.Root, "steamcmd", "modder", sw, CancellationToken.None);
            return (code, sw.ToString());
        }
        var (code, text) = await Run("lint");
        Assert.Equal(Cli.ExitFailed, code);
        Assert.Contains("fix:  About.xml has no <packageId>", text);
        Assert.Contains("1 of 2 can't be uploaded", text);
        (code, text) = await Run("lint", "Alpha");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("All 1 ready.", text);
        (code, text) = await Run("lint", "Nope");
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Equal("--strict only goes with \"lint\" or \"upload\".", Cli.Parse(new[] { "list", "--strict" }).Error);
    }
}
