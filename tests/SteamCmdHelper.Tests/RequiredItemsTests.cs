using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Setting a Workshop page's "Required items" from the mod's dependency links.</summary>
public sealed class RequiredItemsTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _work = Path.Combine(Path.GetTempPath(), "steamcmd_helper_required_" + Guid.NewGuid().ToString("N"));

    public RequiredItemsTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_work, true); } catch { }
    }

    [Fact]
    public void Missing_items_are_added_and_only_ones_this_app_set_are_taken_off()
    {
        var (add, remove) = WorkshopExtras.RequiredPlan(current: new[] { "1", "2", "9" }, wanted: new[] { "1", "3" }, managed: new[] { "1", "2" });
        Assert.Equal(new[] { "3" }, add);
        Assert.Equal(new[] { "2" }, remove); // 9 was added by hand on Steam, so it stays
    }

    [Fact]
    public void Nothing_to_do_without_links_or_a_record()
    {
        var record = Path.Combine(_work, "required.json");
        Assert.Null(WorkshopExtras.RequiredRequest("294100", "100", Array.Empty<string>(), record));

        var req = WorkshopExtras.RequiredRequest("294100", "100", new[] { "5", "100", "5" }, record)!;
        Assert.Equal(new[] { "5" }, req.Required); // its own id and repeats left out

        WorkshopExtras.Remember("100", new[] { "5" }, record);
        var later = WorkshopExtras.RequiredRequest("294100", "100", Array.Empty<string>(), record)!;
        Assert.Empty(later.Required!); // the link was removed: take it off Steam too
        Assert.Equal(new[] { "5" }, later.Managed);

        WorkshopExtras.Remember("100", Array.Empty<string>(), record);
        Assert.Empty(WorkshopExtras.LoadRecord(record));
    }

    [Fact]
    public void The_child_answer_is_read_back()
    {
        var line = WorkshopExtras.ChildLine(new ExtrasResult { Ok = true, Done = { "added required item 5" }, RequiredNow = new[] { "5" } });
        var r = WorkshopExtras.ParseChild("Steam noise\r\n" + line + "\r\n");
        Assert.True(r.Ok);
        Assert.Equal(new[] { "5" }, r.RequiredNow);
        Assert.Contains("stopped without saying why: crashed", WorkshopExtras.ParseChild("crashed").Error);
    }

    string FakeSteamCmd()
    {
        var path = Path.Combine(_work, "steamcmd.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + TestConfig.SkipSteamFlags + "echo \"Logging in user '$2' to Steam Public...OK\"\necho 'Waiting for user info...OK'\n"
            + "[ \"$3\" = '+workshop_build_item' ] || exit 0\necho 'Uploading content...'\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    [Fact]
    public async Task An_upload_sets_required_items_from_the_package_and_remembers_them()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = _repo.AddMod("Alpha", workshopId: "1111111");
        File.WriteAllText(Path.Combine(dir, "About", "About.xml"), """
            <ModMetaData><name>Alpha</name><modVersion>1.2.3</modVersion><supportedVersions><li>1.6</li></supportedVersions><description>A test mod.</description>
            <modDependencies><li><packageId>brrainz.harmony</packageId><steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li></modDependencies></ModMetaData>
            """);
        var calls = new List<ExtrasRequest>();
        var sw = new StringWriter();
        var temp = Path.Combine(_work, "temp");
        Directory.CreateDirectory(temp);

        var code = await Cli.RunAsync(Cli.Parse(new[] { "upload", "Alpha" }), _repo.Root, FakeSteamCmd(), "modder", sw, CancellationToken.None,
            Path.Combine(_work, "uploaded.json"), temp, pageExtras: (r, _) =>
            {
                calls.Add(r);
                return Task.FromResult(new ExtrasResult { Ok = true, Done = { "added required item " + r.Required![0] }, RequiredNow = r.Required });
            });

        Assert.Equal(Cli.ExitOk, code);
        var call = Assert.Single(calls);
        Assert.Equal(("294100", "1111111"), (call.AppId, call.ItemId));
        Assert.Equal(new[] { "2009463077" }, call.Required);
        Assert.Contains("Workshop page: added required item 2009463077.", sw.ToString());
        Assert.Equal(new[] { "2009463077" }, WorkshopExtras.LoadRecord(Path.Combine(_work, "required-items.json"))["1111111"]);
    }

    [Fact]
    public async Task A_failed_required_items_update_does_not_fail_the_upload()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = _repo.AddMod("Alpha", workshopId: "1111111");
        File.AppendAllText(Path.Combine(dir, "About", "About.xml"), "<!-- steam://url/CommunityFilePage/2009463077 -->");
        var sw = new StringWriter();
        var temp = Path.Combine(_work, "temp");
        Directory.CreateDirectory(temp);

        var code = await Cli.RunAsync(Cli.Parse(new[] { "upload", "Alpha" }), _repo.Root, FakeSteamCmd(), "modder", sw, CancellationToken.None,
            Path.Combine(_work, "uploaded.json"), temp, pageExtras: (_, _) => Task.FromResult(ExtrasResult.Fail("Steam is not running.")));

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("the required items were not all set: Steam is not running.", sw.ToString());
    }
}
