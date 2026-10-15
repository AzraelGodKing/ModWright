using System.Diagnostics;
using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public sealed class PublisherTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_tests_" + Guid.NewGuid().ToString("N"));
    readonly string _repo, _temp, _modDir;

    public PublisherTests()
    {
        _repo = Path.Combine(_root, "repo");
        _temp = Path.Combine(_root, "temp");
        _modDir = Path.Combine(_repo, "MyMod");
        Directory.CreateDirectory(_temp);
        Directory.CreateDirectory(Path.Combine(_modDir, "About"));
        File.WriteAllText(Path.Combine(_modDir, "About", "About.xml"),
            "<ModMetaData><name>My Mod</name><modVersion>1.0.0</modVersion><description>A test mod that does a number of useful things.</description></ModMetaData>");
        File.WriteAllText(Path.Combine(_modDir, "About", "changelog.txt"), "1.0.0\nFixed a crash when loading saves.");
        File.WriteAllBytes(Path.Combine(_modDir, "About", "Preview.png"), new byte[] { 1, 2, 3 });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    static ProjectConfig CopyConfig() => TestConfig.RimWorld(c => { c.UseGitArchive = false; c.BuiltFiles = Array.Empty<string>(); });

    ModInfo Mod(string? id) => new() { Dir = _modDir, Folder = "MyMod", Name = "My Mod", Version = "1.0.0", WorkshopId = id };

    string[] StageDirs() => Directory.GetDirectories(_temp, Publisher.StagePrefix + "*");

    string IdFile => Path.Combine(_modDir, "About", "PublishedFileId.txt");

    /// <summary>A stand-in for steamcmd that prints <paramref name="lines"/>, optionally writes a new id into the build file, and exits.</summary>
    string FakeSteamCmd(int exit, string? assignId, params string[] lines)
    {
        var dir = Path.Combine(_root, "steamcmd");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "steamcmd.sh");
        var body = "#!/bin/sh\n" + string.Concat(lines.Select(l => $"printf '%s\\n' '{l.Replace("'", "'\\''")}'\n"));
        // Arguments are: +login <account> +workshop_build_item <vdf> +quit
        if (assignId != null) body += $"sed -i 's/\"publishedfileid\" \"0\"/\"publishedfileid\" \"{assignId}\"/' \"$4\"\n";
        body += $"exit {exit}\n";
        File.WriteAllText(path, body);
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    // The fake SteamCMD is a shell script, so the upload tests run on Linux and macOS only.
    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    [Fact]
    public async Task Dry_run_removes_its_package()
    {
        var pub = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp);
        var r = await pub.RunAsync(Mod("2001112223"), upload: false, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
        Assert.Empty(StageDirs());
    }

    [Fact]
    public async Task Dry_run_of_a_new_item_removes_its_package()
    {
        File.WriteAllText(Path.Combine(_modDir, "About", "steam_description.txt"), "A long enough Steam description for the item.");
        var pub = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp);
        var r = await pub.RunAsync(Mod(null), upload: false, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
        Assert.Empty(StageDirs());
    }

    [Fact]
    public async Task Missing_description_removes_its_package()
    {
        File.WriteAllText(Path.Combine(_modDir, "About", "About.xml"), "<ModMetaData><name>My Mod</name></ModMetaData>");
        var pub = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp);
        var r = await pub.RunAsync(Mod(null), upload: false, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Empty(StageDirs());
    }

    [Fact]
    public async Task Failed_packaging_removes_its_package()
    {
        // git archive of a mod that was never committed throws while packaging.
        Git("init", "-q");
        File.WriteAllText(Path.Combine(_repo, "readme.txt"), "x");
        Git("add", "readme.txt");
        Git("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "-m", "init");

        var cfg = TestConfig.RimWorld(c => { c.UseGitArchive = true; c.BuiltFiles = Array.Empty<string>(); });
        var pub = new Publisher(_repo, cfg, "steamcmd", "modder", _temp);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pub.RunAsync(Mod("2001112223"), upload: false, CancellationToken.None));
        Assert.Empty(StageDirs());
    }

    [Fact]
    public async Task Successful_update_reports_ok_and_removes_its_package()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Logging in user 'modder' to Steam Public...OK", "Uploading content...", "Committing update...", "Success.");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp);
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
        Assert.Empty(StageDirs());
    }

    static Func<string, CancellationToken, Task<WorkshopItem?>> SteamShows(WorkshopItem? item) => (_, _) => Task.FromResult(item);

    [Fact]
    public async Task Run_without_success_line_counts_when_Steam_shows_the_update()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Uploading content...", "Committing update...");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp)
            { SteamCheck = SteamShows(new WorkshopItem("2001112223", true, Updated: DateTimeOffset.UtcNow)) };
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
        Assert.Empty(StageDirs());
    }

    [Fact]
    public async Task Run_without_success_line_fails_when_Steam_shows_an_older_update()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Uploading content...", "Committing update...");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp)
            { SteamCheck = SteamShows(new WorkshopItem("2001112223", true, Updated: DateTimeOffset.UtcNow.AddDays(-3))) };
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("previous update time", r.Message);
    }

    [Fact]
    public async Task Run_without_success_line_fails_when_Steam_cannot_say()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Uploading content...", "Committing update...");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp) { SteamCheck = SteamShows(new WorkshopItem("2001112223", false)) };
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("could not confirm", r.Message);
    }

    [Fact]
    public async Task Steam_check_failing_does_not_fail_a_confirmed_upload()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Uploading content...", "Committing update...", "Success.");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp)
            { SteamCheck = (_, _) => throw new HttpRequestException("offline") };
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
    }

    [Fact]
    public async Task Steam_check_cannot_rescue_a_reported_failure()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Uploading content...", "ERROR! Failed to update workshop item (Access Denied).");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp)
            { SteamCheck = SteamShows(new WorkshopItem("2001112223", true, Updated: DateTimeOffset.UtcNow)) };
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("Access Denied", r.Message);
    }

    [Fact]
    public async Task Successful_upload_is_added_to_the_history()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Uploading content...", "Committing update...", "Success.");
        var history = Path.Combine(_root, "history.json");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp) { HistoryFile = history };
        Assert.True((await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None)).Ok);

        var e = Assert.Single(UploadHistory.Load(history));
        Assert.Equal("2001112223", e.WorkshopId);
        Assert.Equal("1.0.0", e.Version);
        Assert.EndsWith("Fixed a crash when loading saves.", e.ChangeNote); // the note exactly as sent
        Assert.False(e.Created);
    }

    [Fact]
    public async Task Failed_upload_and_dry_run_are_not_added_to_the_history()
    {
        var history = Path.Combine(_root, "history.json");
        var dry = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp) { HistoryFile = history };
        Assert.True((await dry.RunAsync(Mod("2001112223"), upload: false, CancellationToken.None)).Ok);
        if (CanFakeSteamCmd)
        {
            var steam = FakeSteamCmd(1, null, "ERROR! Timeout uploading manifest");
            var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp) { HistoryFile = history };
            Assert.False((await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None)).Ok);
        }
        Assert.Empty(UploadHistory.Load(history));
    }

    [Fact]
    public async Task Exit_zero_without_success_line_is_a_failure()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Logging in user 'modder' to Steam Public...OK", "Uploading content...");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp);
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Single(StageDirs()); // kept for inspection
    }

    [Fact]
    public async Task Error_line_with_exit_zero_is_a_failure()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd(0, null, "Uploading content...", "ERROR! Failed to update workshop item (Access Denied).", "Success.");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp);
        var r = await pub.RunAsync(Mod("2001112223"), upload: true, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("Access Denied", r.Message);
    }

    [Fact]
    public async Task Create_saves_the_new_id()
    {
        if (!CanFakeSteamCmd) return;
        File.WriteAllText(Path.Combine(_modDir, "About", "steam_description.txt"), "A long enough Steam description for the item.");
        var steam = FakeSteamCmd(0, "3004445556", "Uploading content...", "Committing update...", "Success.");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp);
        var r = await pub.RunAsync(Mod(null), upload: true, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
        Assert.Equal("3004445556", r.NewId);
        Assert.Equal("3004445556", File.ReadAllText(IdFile));
        Assert.Empty(StageDirs());
    }

    [Fact]
    public async Task Failed_create_still_saves_an_assigned_id()
    {
        if (!CanFakeSteamCmd) return;
        File.WriteAllText(Path.Combine(_modDir, "About", "steam_description.txt"), "A long enough Steam description for the item.");
        var steam = FakeSteamCmd(0, "3004445556", "Uploading content...", "ERROR! Timeout uploading manifest (Failure)");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp);
        var r = await pub.RunAsync(Mod(null), upload: true, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Equal("3004445556", File.ReadAllText(IdFile));
        Assert.Contains("do NOT create it again", r.Message);
    }

    [Fact]
    public async Task Create_without_returned_id_is_a_failure_and_saves_nothing()
    {
        if (!CanFakeSteamCmd) return;
        File.WriteAllText(Path.Combine(_modDir, "About", "steam_description.txt"), "A long enough Steam description for the item.");
        var steam = FakeSteamCmd(0, null, "Uploading content...", "Success.");
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp);
        var r = await pub.RunAsync(Mod(null), upload: true, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.False(File.Exists(IdFile));
    }

    [Fact]
    public void Sweep_removes_only_old_staging_folders()
    {
        var old = Path.Combine(_temp, Publisher.StagePrefix + "old");
        var fresh = Path.Combine(_temp, Publisher.StagePrefix + "fresh");
        var unrelated = Path.Combine(_temp, "someone_else");
        foreach (var d in new[] { old, fresh, unrelated }) Directory.CreateDirectory(d);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        Directory.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-30));

        Publisher.SweepStale(_temp, TimeSpan.FromDays(7));

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(unrelated));
    }

    void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}
