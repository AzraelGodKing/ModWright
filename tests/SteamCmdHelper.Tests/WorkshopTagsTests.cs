using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Workshop tags: which ones a mod gets, how they merge with Steam's, and that uploads set them.</summary>
public sealed class WorkshopTagsTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _work = Path.Combine(Path.GetTempPath(), "steamcmd_helper_tags_" + Guid.NewGuid().ToString("N"));

    public WorkshopTagsTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_work, true); } catch { }
    }

    [Fact]
    public void RimWorld_mods_get_Mod_and_each_supported_version()
    {
        var dir = _repo.AddMod("Alpha", supportedVersions: new[] { "1.5", "1.6.4104", " 1.7 ", "1.6", "not a version" });
        Assert.Equal(new[] { "Mod", "1.5", "1.6", "1.7" }, WorkshopTags.For(dir, TestConfig.RimWorld()));
    }

    [Fact]
    public void A_mod_without_supported_versions_still_gets_Mod_and_a_warning()
    {
        _repo.AddMod("Alpha", supportedVersions: Array.Empty<string>());
        Assert.Equal(new[] { "Mod" }, WorkshopTags.For(Path.Combine(_repo.Root, "Alpha"), TestConfig.RimWorld()));
        var mod = ModScanner.Scan(_repo.Root, TestConfig.RimWorld()).Single();
        Assert.Contains(mod.Warnings, w => w.Contains("no supported game versions"));
    }

    [Fact]
    public void Layouts_without_tags_leave_them_alone()
    {
        var generic = ProjectConfig.GetPreset("Generic");
        Assert.False(WorkshopTags.LayoutHasTags(generic));
        Assert.Empty(WorkshopTags.For(_repo.AddMod("Alpha"), generic));
        Assert.True(WorkshopTags.LayoutHasTags(TestConfig.RimWorld()));
    }

    [Fact]
    public void Json_markers_list_versions_as_an_array()
    {
        var file = Path.Combine(_work, "mod.json");
        File.WriteAllText(file, """{ "info": { "versions": ["2.0.1", "2.1"] } }""");
        Assert.Equal(new[] { "2.0", "2.1" }, WorkshopTags.SupportedVersions(file, "info.versions"));
    }

    [Fact]
    public void Merge_replaces_old_version_tags_and_keeps_the_rest()
    {
        var merged = WorkshopTags.Merge(new[] { "mod", "1.4", "1.5", "scenario" }, new[] { "Mod", "1.5", "1.6" });
        Assert.Equal(new[] { "Mod", "1.5", "1.6", "scenario" }, merged);
        Assert.True(WorkshopTags.Same(new[] { "mod", "1.6" }, new[] { "1.6", "Mod" }));
        Assert.False(WorkshopTags.Same(new[] { "Mod", "1.5" }, new[] { "Mod", "1.5", "1.6" }));
    }

    [Theory]
    [InlineData("OK Mod\t1.6\n", true, true, new[] { "Mod", "1.6" })]
    [InlineData("noise\r\nSAME Mod\t1.6\r\n", true, false, new[] { "Mod", "1.6" })]
    [InlineData("ERROR Steam is not running\n", false, false, new[] { "Mod" })]
    [InlineData("", false, false, new[] { "Mod" })]
    public void Child_output_is_read_back(string output, bool ok, bool changed, string[] tags)
    {
        var r = WorkshopTags.ParseChild(output, new[] { "Mod" });
        Assert.Equal(ok, r.Ok);
        Assert.Equal(changed, r.Changed);
        Assert.Equal(tags, r.Tags);
    }

    [Fact]
    public void Child_lines_round_trip()
    {
        var sent = new TagResult(true, true, new[] { "Mod", "1.6", "Two words" }, "");
        var back = WorkshopTags.ParseChild(WorkshopTags.ChildLine(sent), Array.Empty<string>());
        Assert.Equal(sent.Tags, back.Tags);
        Assert.True(back.Changed);
        Assert.Equal("Steam is not running", WorkshopTags.ParseChild(WorkshopTags.ChildLine(new TagResult(false, false, sent.Tags, "Steam is not running")), sent.Tags).Message);
    }

    // ------------------------------------------------------------ uploads (fake SteamCMD is a shell script)

    string FakeSteamCmd()
    {
        var path = Path.Combine(_work, "steamcmd.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + TestConfig.SkipSteamFlags + "echo \"Logging in user '$2' to Steam Public...OK\"\necho 'Waiting for user info...OK'\n"
            + "[ \"$3\" = '+workshop_build_item' ] || exit 0\necho 'Uploading content...'\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    async Task<(int code, string text)> Upload(Func<string, string, string[], CancellationToken, Task<TagResult>> tagger, params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var temp = Path.Combine(_work, "temp");
        Directory.CreateDirectory(temp);
        var code = await Cli.RunAsync(o, _repo.Root, FakeSteamCmd(), "modder", sw, CancellationToken.None,
            Path.Combine(_work, "uploaded.json"), temp, tagUpdater: tagger);
        return (code, sw.ToString());
    }

    [Fact]
    public async Task An_upload_sets_the_tags_from_the_package()
    {
        if (OperatingSystem.IsWindows()) return;
        _repo.AddMod("Alpha", workshopId: "1111111", supportedVersions: new[] { "1.6", "1.7" });
        var calls = new List<(string app, string id, string[] tags)>();
        var (code, text) = await Upload((app, id, tags, _) =>
        {
            calls.Add((app, id, tags));
            return Task.FromResult(new TagResult(true, true, tags, "Workshop tags set to " + WorkshopTags.Describe(tags) + "."));
        }, "upload", "Alpha");
        Assert.Equal(Cli.ExitOk, code);
        var call = Assert.Single(calls);
        Assert.Equal(("294100", "1111111"), (call.app, call.id));
        Assert.Equal(new[] { "Mod", "1.6", "1.7" }, call.tags);
        Assert.Contains("Workshop tags set to Mod, 1.6, 1.7.", text);
    }

    [Fact]
    public async Task A_failed_tag_update_does_not_fail_the_upload()
    {
        if (OperatingSystem.IsWindows()) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var (code, text) = await Upload((_, _, tags, _) => Task.FromResult(new TagResult(false, false, tags, "Steam is not running.")), "upload", "Alpha");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Warning: the upload went through, but the Workshop tags were not set: Steam is not running.", text);
    }

    [Fact]
    public async Task A_dry_run_only_says_which_tags_it_would_set()
    {
        _repo.AddMod("Alpha", workshopId: "1111111");
        var called = false;
        var (code, text) = await Upload((_, _, tags, _) => { called = true; return Task.FromResult(new TagResult(true, true, tags, "")); }, "upload", "Alpha", "--dry-run");
        Assert.Equal(Cli.ExitOk, code);
        Assert.False(called);
        Assert.Contains("Workshop tags to set after the upload: Mod, 1.6.", text);
    }
}
