using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Workshop page changes: keeping them until the next upload, reading Steam's page, and the build file.</summary>
public sealed class WorkshopPageTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld(c => c.UseGitArchive = false);
    string Store => Path.Combine(_repo.Root, "_store", "pages.json");

    public void Dispose() => _repo.Dispose();

    // ------------------------------------------------------------ pending changes

    [Fact]
    public void Changes_are_kept_per_mod_until_cleared()
    {
        var a = _repo.AddMod("Alpha");
        var b = _repo.AddMod("Beta");

        WorkshopPage.Set(a, new WorkshopPageEdit { Title = "New title", Visibility = 0 }, Store);

        var got = WorkshopPage.Get(a + Path.DirectorySeparatorChar, Store);
        Assert.NotNull(got);
        Assert.Equal("New title", got!.Title);
        Assert.Null(got.Description);
        Assert.Equal(0, got.Visibility);
        Assert.Equal("title, visibility (Public)", got.Summary());
        Assert.Null(WorkshopPage.Get(b, Store));

        WorkshopPage.Clear(a, Store);
        Assert.Null(WorkshopPage.Get(a, Store));
    }

    [Fact]
    public void Saving_an_empty_edit_clears_it()
    {
        var a = _repo.AddMod("Alpha");
        WorkshopPage.Set(a, new WorkshopPageEdit { Description = "x" }, Store);

        WorkshopPage.Set(a, new WorkshopPageEdit(), Store);

        Assert.Null(WorkshopPage.Get(a, Store));
    }

    [Fact]
    public void A_broken_store_reads_as_empty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Store)!);
        File.WriteAllText(Store, "{ not json");

        Assert.Null(WorkshopPage.Get(_repo.Root, Store));
    }

    [Theory]
    [InlineData("   ", null, null, false)]
    [InlineData("ok", null, null, true)]
    [InlineData(null, null, 3, true)]
    [InlineData(null, null, 4, false)]
    [InlineData(null, null, -1, false)]
    public void Edits_are_checked_against_Steam_limits(string? title, string? description, int? visibility, bool ok)
    {
        var e = new WorkshopPageEdit { Title = title, Description = description, Visibility = visibility };
        Assert.Equal(ok, WorkshopPage.Validate(e) == null);
    }

    [Fact]
    public void Too_long_title_or_description_is_refused()
    {
        Assert.NotNull(WorkshopPage.Validate(new WorkshopPageEdit { Title = new string('t', WorkshopPage.MaxTitle + 1) }));
        Assert.NotNull(WorkshopPage.Validate(new WorkshopPageEdit { Description = new string('d', WorkshopPage.MaxDescription + 1) }));
        Assert.Null(WorkshopPage.Validate(new WorkshopPageEdit { Description = new string('d', WorkshopPage.MaxDescription) }));
    }

    // ------------------------------------------------------------ the page on Steam

    [Fact]
    public void Steam_details_are_read()
    {
        const string json = """
            {"response":{"result":1,"resultcount":1,"publishedfiledetails":[{"publishedfileid":"123","result":1,
             "title":"My Mod","description":"[b]Hi[/b]\nLine","visibility":3,"tags":[{"tag":"Mod"},{"tag":"1.5"}]}]}}
            """;

        var d = WorkshopPage.ParseDetails(json);

        Assert.NotNull(d);
        Assert.Equal("My Mod", d!.Title);
        Assert.Equal("[b]Hi[/b]\nLine", d.Description);
        Assert.Equal(3, d.Visibility);
        Assert.Equal(new[] { "Mod", "1.5" }, d.Tags);
    }

    [Theory]
    [InlineData("""{"response":{"result":1,"resultcount":1,"publishedfiledetails":[{"publishedfileid":"123","result":9}]}}""")]
    [InlineData("""{"response":{}}""")]
    [InlineData("not json")]
    public void Missing_or_private_items_give_nothing(string json)
    {
        Assert.Null(WorkshopPage.ParseDetails(json));
    }

    // ------------------------------------------------------------ build file

    static string? VdfValue(string vdf, string key)
    {
        var m = Regex.Match(vdf, "\"" + key + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? Regex.Replace(m.Groups[1].Value, @"\\(.)", x => x.Groups[1].Value == "n" ? "\n" : x.Groups[1].Value) : null;
    }

    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    async Task<string> CapturedVdf(ModInfo mod, WorkshopPageEdit? page)
    {
        var copy = Path.Combine(_repo.Root, "captured.vdf");
        var steam = _repo.Write("_steamcmd/steamcmd.sh", $"#!/bin/sh\ncp \"$4\" '{copy}'\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", steam })!.WaitForExit();

        var pub = new Publisher(_repo.Root, _cfg, steam, "account", Path.Combine(_repo.Root, "_temp")) { PageEdits = _ => page };
        var res = await pub.RunAsync(mod, upload: true, CancellationToken.None);

        Assert.True(File.Exists(copy), "SteamCMD was not given a build file: " + res.Message);
        return await File.ReadAllTextAsync(copy);
    }

    [Fact]
    public async Task Update_sends_only_the_changed_fields()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var vdf = await CapturedVdf(mod, new WorkshopPageEdit { Title = " Alpha \"Remastered\" ", Visibility = 3 });

        Assert.Equal("123456789", VdfValue(vdf, "publishedfileid"));
        Assert.Equal("Alpha \"Remastered\"", VdfValue(vdf, "title"));
        Assert.Equal("3", VdfValue(vdf, "visibility"));
        Assert.Null(VdfValue(vdf, "description"));
        Assert.Null(VdfValue(vdf, "previewfile"));
    }

    [Fact]
    public async Task Update_without_changes_leaves_the_page_alone()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var vdf = await CapturedVdf(mod, null);

        Assert.Null(VdfValue(vdf, "title"));
        Assert.Null(VdfValue(vdf, "description"));
        Assert.Null(VdfValue(vdf, "visibility"));
    }

    [Fact]
    public async Task New_item_uses_the_changes_in_place_of_its_files()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: null);
        _repo.Write("Alpha/About/steam_description.txt", "The description from the file, long enough.");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var vdf = await CapturedVdf(mod, new WorkshopPageEdit { Description = "[h1]Typed in the app[/h1]\nSecond line", Visibility = 1 });

        Assert.Equal("0", VdfValue(vdf, "publishedfileid"));
        Assert.Equal("Alpha Display", VdfValue(vdf, "title"));
        Assert.Equal("[h1]Typed in the app[/h1]\nSecond line", VdfValue(vdf, "description"));
        Assert.Equal("1", VdfValue(vdf, "visibility"));
        Assert.NotNull(VdfValue(vdf, "previewfile"));
    }

    [Fact]
    public async Task Invalid_changes_stop_the_upload()
    {
        _repo.AddMod("Alpha");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));
        var pub = new Publisher(_repo.Root, _cfg, "steamcmd", "account", Path.Combine(_repo.Root, "_temp"))
        {
            PageEdits = _ => new WorkshopPageEdit { Title = "" }
        };

        var res = await pub.RunAsync(mod, upload: false, CancellationToken.None);

        Assert.False(res.Ok);
        Assert.Contains("title", res.Message);
    }
}
