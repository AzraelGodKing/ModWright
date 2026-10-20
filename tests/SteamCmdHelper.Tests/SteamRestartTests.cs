using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Bringing the Steam client back online after SteamCMD's login took its place.</summary>
public sealed class SteamRestartTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _work = Path.Combine(Path.GetTempPath(), "steamcmd_helper_restart_" + Guid.NewGuid().ToString("N"));

    public SteamRestartTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_work, true); } catch { }
    }

    static SteamRestart.State Online(string account = "modder") => new(true, @"C:\Steam\steam.exe", account, true, 0);

    /// <summary>A pretend Steam: "-shutdown" closes it (unless it is stuck), "-silent" starts it signed in (unless it signs in slowly).</summary>
    sealed class FakeSteam
    {
        public SteamRestart.State Now;
        public readonly List<string> Started = new();
        public bool Stuck, NoSignIn;
        public FakeSteam(SteamRestart.State now) => Now = now;

        public SteamRestart.Machine Machine => new()
        {
            Read = () => Now,
            Start = (_, args) =>
            {
                Started.Add(args);
                if (args == "-shutdown" && !Stuck) Now = Now with { Running = false, SignedIn = false };
                if (args == "-silent") Now = Now with { Running = true, SignedIn = !NoSignIn };
            },
            Wait = (_, _) => Task.CompletedTask,
        };
    }

    [Fact]
    public void Steam_is_left_alone_when_closed_busy_or_signed_in_as_someone_else()
    {
        Assert.Null(SteamRestart.SkipReason(Online(), "modder"));
        Assert.Null(SteamRestart.SkipReason(Online("Modder"), "modder")); // Steam keeps account names in any case
        Assert.Contains("not running", SteamRestart.SkipReason(Online() with { Running = false }, "modder"));
        Assert.Contains("could not be found", SteamRestart.SkipReason(Online() with { Exe = null }, "modder"));
        Assert.Contains("signs in as other", SteamRestart.SkipReason(Online("other"), "modder"));
        Assert.Null(SteamRestart.SkipReason(Online("other"), null)); // Restart Steam now restarts whoever is signed in
        Assert.Contains("A game (app 294100) is running", SteamRestart.SkipReason(Online() with { RunningGame = 294100 }, "modder"));
        Assert.Null(SteamRestart.SkipReason(Online(""), "modder")); // no saved login: still logged out by SteamCMD
    }

    [Fact]
    public async Task Steam_is_closed_then_started_in_the_tray_and_waits_to_sign_in()
    {
        var steam = new FakeSteam(Online() with { SignedIn = false });
        var said = await SteamRestart.RestartAsync("modder", steam.Machine);
        Assert.Equal(new[] { "-shutdown", "-silent" }, steam.Started);
        Assert.Equal("Steam is back online as modder.", said);
    }

    [Fact]
    public async Task Steam_that_will_not_close_is_not_started_again()
    {
        var steam = new FakeSteam(Online()) { Stuck = true };
        var said = await SteamRestart.RestartAsync("modder", steam.Machine);
        Assert.Equal(new[] { "-shutdown" }, steam.Started);
        Assert.StartsWith("Warning: Steam did not close within 60 seconds", said);
    }

    [Fact]
    public async Task Steam_without_a_saved_login_is_said_to_be_waiting_for_a_password()
    {
        var steam = new FakeSteam(Online("")) { NoSignIn = true };
        var said = await SteamRestart.RestartAsync("modder", steam.Machine);
        Assert.Contains("waiting for you to sign in", said);
    }

    [Fact]
    public void Nothing_is_brought_back_when_Steam_was_not_running_before()
    {
        Assert.Null(SteamRestart.Prepare("modder", new FakeSteam(Online() with { Running = false }).Machine));
        Assert.NotNull(SteamRestart.Prepare("modder", new FakeSteam(Online()).Machine));
    }

    string FakeSteamCmd()
    {
        var path = Path.Combine(_work, "steamcmd.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + TestConfig.SkipSteamFlags + "echo \"Logging in user '$2' to Steam Public...OK\"\necho 'Waiting for user info...OK'\n"
            + "[ \"$3\" = '+workshop_build_item' ] || exit 0\necho 'Uploading content...'\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    async Task<(int Code, string Output)> UploadAsync(string[] args, List<string> order, bool withExtras)
    {
        var sw = new StringWriter();
        var temp = Path.Combine(_work, "temp");
        Directory.CreateDirectory(temp);
        var code = await Cli.RunAsync(Cli.Parse(args), _repo.Root, FakeSteamCmd(), "modder", sw, CancellationToken.None,
            Path.Combine(_work, "uploaded.json"), temp,
            pageExtras: withExtras ? (r, _) => { order.Add("extras " + r.ItemId); return Task.FromResult(new ExtrasResult { Ok = true }); } : null,
            prepareSteamBack: account =>
            {
                order.Add("looked as " + account);
                return _ => { order.Add("restart"); return Task.FromResult<string?>("Steam is back online as modder."); };
            });
        return (code, sw.ToString());
    }

    [Fact]
    public async Task An_upload_brings_Steam_back_once_at_the_end()
    {
        if (OperatingSystem.IsWindows()) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var order = new List<string>();
        var (code, output) = await UploadAsync(new[] { "upload", "Alpha" }, order, withExtras: false);
        Assert.Equal(Cli.ExitOk, code);
        Assert.Equal(new[] { "looked as modder", "restart" }, order);
        Assert.Contains("Steam is back online as modder.", output);
    }

    [Fact]
    public async Task Steam_is_back_before_each_page_step_that_goes_through_it_and_not_again_at_the_end()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var name in new[] { "Alpha", "Beta" })
        {
            var dir = _repo.AddMod(name, workshopId: name == "Alpha" ? "1111111" : "2222222");
            File.AppendAllText(Path.Combine(dir, "About", "About.xml"), "<!-- steam://url/CommunityFilePage/2009463077 -->");
        }
        var order = new List<string>();
        var (code, output) = await UploadAsync(new[] { "upload", "Alpha", "Beta" }, order, withExtras: true);
        Assert.True(code == Cli.ExitOk, output);
        // Each mod's SteamCMD run logs in again, so Steam is brought back before each page step, and then not again at the end.
        Assert.Equal(new[] { "looked as modder", "restart", "extras 1111111", "restart", "extras 2222222" }, order);
    }

    [Fact]
    public async Task A_dry_run_leaves_Steam_alone()
    {
        if (OperatingSystem.IsWindows()) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var order = new List<string>();
        var (code, _) = await UploadAsync(new[] { "upload", "Alpha", "--dry-run" }, order, withExtras: false);
        Assert.Equal(Cli.ExitOk, code);
        Assert.Empty(order);
    }

    [Fact]
    public async Task Check_setup_warns_when_Steam_would_wait_for_a_password_after_a_restart()
    {
        var s = new AppSettings { RepoPath = _repo.Root };
        Doctor.Probes Probes(SteamRestart.State st) => new() { ToolRuns = _ => true, SteamRunning = () => st.Running, SteamState = () => st, FreeBytes = _ => long.MaxValue };
        var noLogin = await Doctor.RunAsync(s, _repo.Root, "", "modder", Probes(Online("")));
        Assert.Contains(noLogin, c => c.Name == "Steam restart" && c.Level == CheckLevel.Warn);
        var fine = await Doctor.RunAsync(s, _repo.Root, "", "modder", Probes(Online()));
        Assert.Contains(fine, c => c.Name == "Steam restart" && c.Level == CheckLevel.Ok && c.Detail.Contains("signs back in as modder"));
        s.SteamBackOnline = false;
        Assert.DoesNotContain(await Doctor.RunAsync(s, _repo.Root, "", "modder", Probes(Online(""))), c => c.Name == "Steam restart");
    }
}
