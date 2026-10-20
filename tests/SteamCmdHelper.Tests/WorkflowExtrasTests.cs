using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Mod groups, the after-upload command, kept packages, exports, the note command and the setup check.</summary>
public sealed class WorkflowExtrasTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _data = Path.Combine(Path.GetTempPath(), "steamcmd_helper_workflow_" + Guid.NewGuid().ToString("N"));

    public WorkflowExtrasTests() => Directory.CreateDirectory(_data);

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_data, true); } catch { }
    }

    async Task<(int code, string text)> Run(AppSettings? settings, params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, _repo.Root, "steamcmd", "modder", sw, CancellationToken.None, Path.Combine(_data, "uploaded.json"), Path.Combine(_data, "temp"), settings: settings);
        return (code, sw.ToString());
    }

    // ------------------------------------------------------------ groups

    [Fact]
    public void Groups_are_kept_per_mods_folder_and_follow_a_move()
    {
        var s = new AppSettings();
        s.SetGroup(@"D:\mods", "Core", new[] { "Alpha", "Beta", "alpha" });
        s.SetGroup(@"D:\mods\", "Patches", new[] { "Gamma" });
        Assert.Equal(new[] { "Core", "Patches" }, s.GroupsFor(@"d:\MODS").Keys.ToArray());
        Assert.Equal(new[] { "Alpha", "Beta" }, s.GroupsFor(@"D:\mods")["core"].ToArray());
        Assert.Empty(s.GroupsFor(@"E:\other"));
        s.SetGroup(@"D:\mods", "core", Array.Empty<string>());
        Assert.Equal(new[] { "Patches" }, s.GroupsFor(@"D:\mods").Keys.ToArray());
        Assert.Contains(@"D:\mods", s.Folders());
        s.MoveFolder(@"D:\mods", @"F:\mods");
        Assert.Single(s.GroupsFor(@"F:\mods"));
    }

    [Fact]
    public async Task Upload_group_picks_the_saved_mods()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        _repo.AddMod("Gamma");
        var s = new AppSettings();
        s.SetGroup(_repo.Root, "Core", new[] { "Alpha", "Gamma" });
        var (code, text) = await Run(s, "upload", "--group", "core", "--dry-run");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("--- Alpha Display", text);
        Assert.Contains("--- Gamma Display", text);
        Assert.DoesNotContain("--- Beta Display", text);

        (code, text) = await Run(s, "upload", "--group", "Nope", "--dry-run");
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Groups: Core", text);
        Assert.NotNull(Cli.Parse(new[] { "upload", "--group", "Core", "--all" }).Error);
        Assert.NotNull(Cli.Parse(new[] { "list", "--group", "Core" }).Error);
        Assert.Null(Cli.Parse(new[] { "upload", "--group", "Core" }).Error);
    }
}
