using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The setup check (doctor) and uploading later through Task Scheduler.</summary>
[Collection("ScheduledUploads")]
public sealed class SetupAndScheduleTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _data = Path.Combine(Path.GetTempPath(), "steamcmd_helper_setup_" + Guid.NewGuid().ToString("N"));
    readonly List<string[]> _calls = new();
    readonly Func<IReadOnlyList<string>, CancellationToken, Task<(int, string)>> _oldRunner = ScheduledUploads.Runner;

    public SetupAndScheduleTests()
    {
        Directory.CreateDirectory(_data);
        ScheduledUploads.Runner = (args, _) => { _calls.Add(args.ToArray()); return Task.FromResult((0, "SUCCESS")); };
    }

    public void Dispose()
    {
        ScheduledUploads.Runner = _oldRunner;
        _repo.Dispose();
        try { Directory.Delete(_data, true); } catch { }
    }

    static readonly DateTime Now = new(2026, 10, 20, 14, 30, 0);

    static Doctor.Probes Probes(bool tools = true, bool steam = true, long free = 50L << 30) =>
        new() { ToolRuns = _ => tools, SteamRunning = () => steam, FreeBytes = _ => free };

    string FakeSteamCmd()
    {
        var path = Path.Combine(_data, "steamcmd");
        File.WriteAllText(path, "");
        return path;
    }

    // ------------------------------------------------------------ doctor

    [Fact]
    public async Task Doctor_says_what_to_fix_when_nothing_is_set_up()
    {
        var checks = await Doctor.RunAsync(new AppSettings(), "", Path.Combine(_data, "missing", "steamcmd.exe"), "", Probes(), _data);
        Assert.Contains(checks, c => c.Name == "Mods folder" && c.Level == CheckLevel.Fail);
        Assert.Contains(checks, c => c.Name == "SteamCMD" && c.Level == CheckLevel.Fail);
        Assert.Contains(checks, c => c.Name == "Steam account" && c.Level == CheckLevel.Fail);
        Assert.StartsWith("3 things to fix", Doctor.Summary(checks));
    }

    [Fact]
    public async Task Doctor_counts_mods_and_tries_the_login_when_asked()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta", changelog: null);
        TestConfig.RimWorld().Save(_repo.Root);
        var tried = false;
        var p = Probes();
        var probes = new Doctor.Probes { ToolRuns = p.ToolRuns, SteamRunning = p.SteamRunning, FreeBytes = p.FreeBytes, Login = (_, acc, _) => { tried = acc == "modder"; return Task.FromResult(true); } };
        var checks = await Doctor.RunAsync(new AppSettings(), _repo.Root, FakeSteamCmd(), "modder", probes, _data);
        Assert.True(tried);
        var mods = Assert.Single(checks, c => c.Name == "Mods folder");
        Assert.Contains("2 mods, 1 need fixing", mods.Detail);
        Assert.Contains(checks, c => c.Name == "Steam app ID" && c.Level == CheckLevel.Ok);
        Assert.Contains(checks, c => c.Name == "Steam login" && c.Level == CheckLevel.Ok);
        Assert.DoesNotContain(checks, c => c.Level == CheckLevel.Fail);
    }

    [Fact]
    public async Task Doctor_checks_gh_steam_low_space_and_settings_only_when_they_are_used()
    {
        _repo.AddMod("Alpha");
        TestConfig.RimWorld().Save(_repo.Root);
        var s = new AppSettings { UpdateTags = false, UpdateRequired = false, UpdatePageExtras = false };
        var plain = await Doctor.RunAsync(s, _repo.Root, FakeSteamCmd(), "modder", Probes(tools: false, steam: false), _data);
        Assert.DoesNotContain(plain, c => c.Name.StartsWith("gh") || c.Name == "Steam client");

        s.GitHubReleaseRepos.Add(_repo.Root);
        s.UpdateRequired = true;
        s.DiscordWebhooks[_repo.Root] = "https://example.com/not-a-hook";
        var checks = await Doctor.RunAsync(s, _repo.Root, FakeSteamCmd(), "modder", Probes(tools: false, steam: false, free: 100L << 20), _data);
        Assert.Contains(checks, c => c.Name.StartsWith("gh") && c.Level == CheckLevel.Fail);
        Assert.Contains(checks, c => c.Name == "Steam client" && c.Level == CheckLevel.Warn);
        Assert.Contains(checks, c => c.Name == "Discord webhook" && c.Level == CheckLevel.Fail);
        Assert.Contains(checks, c => c.Name == "Free space" && c.Level == CheckLevel.Warn);
    }

    [Fact]
    public async Task Doctor_command_exits_2_when_something_must_be_fixed()
    {
        var sw = new StringWriter();
        var code = await Cli.RunAsync(Cli.Parse(new[] { "doctor" }), "", Path.Combine(_data, "none"), "", sw, CancellationToken.None,
            Path.Combine(_data, "uploaded.json"), _data, settings: new AppSettings(), probes: Probes());
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("FIX   SteamCMD", sw.ToString());
        Assert.Equal("--login only goes with \"doctor\".", Cli.Parse(new[] { "list", "--login" }).Error);
    }

    // ------------------------------------------------------------ upload later

    [Theory]
    [InlineData("2026-10-21 18:00", "2026-10-21 18:00")]
    [InlineData("2026-10-21T08:05", "2026-10-21 08:05")]
    [InlineData("18:00", "2026-10-20 18:00")]
    [InlineData("9:15", "2026-10-21 09:15")]
    [InlineData("tomorrow 18:00", "2026-10-21 18:00")]
    [InlineData("+2h", "2026-10-20 16:30")]
    [InlineData("+45m", "2026-10-20 15:15")]
    [InlineData("+1d", "2026-10-21 14:30")]
    public void Times_are_read_the_same_on_every_PC(string text, string expected) =>
        Assert.Equal(expected, ScheduledUploads.ParseWhen(text, Now)!.Value.ToString("yyyy-MM-dd HH:mm"));

    [Theory]
    [InlineData("soon")]
    [InlineData("25:00")]
    [InlineData("21/10/2026 18:00")]
    public void Unclear_times_are_refused(string text) => Assert.Null(ScheduledUploads.ParseWhen(text, Now));

    [Fact]
    public void The_command_line_is_quoted_for_Windows()
    {
        var args = ScheduledUploads.UploadArgs(@"D:\My Mods\", new[] { "Alpha", "Say \"Hi\"" }, extra: new[] { "--announce" });
        Assert.Equal(@"upload Alpha ""Say \""Hi\"""" --repo ""D:\My Mods\\"" --announce", ScheduledUploads.CommandLine(args));
        Assert.Equal(new[] { "upload", "--group", "Core", "--repo", "D:\\m" }, ScheduledUploads.UploadArgs("D:\\m", Array.Empty<string>(), group: "Core"));
        Assert.Equal(@"SteamCMD Helper\Upload 2026-10-21 1800 AB", ScheduledUploads.TaskName(new DateTime(2026, 10, 21, 18, 0, 0), "A:B?"));
    }

    [Fact]
    public void The_task_runs_once_as_the_user_and_catches_up_when_the_PC_was_off()
    {
        var xml = ScheduledUploads.TaskXml(@"C:\App\SteamCmdHelper.exe", "upload \"A & B\"", @"D:\mods", new DateTime(2026, 10, 21, 18, 0, 0), "Uploads <A>");
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var ns = doc.Root!.Name.Namespace;
        Assert.Equal("2026-10-21T18:00:00", doc.Descendants(ns + "StartBoundary").Single().Value);
        Assert.Equal("InteractiveToken", doc.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("true", doc.Descendants(ns + "StartWhenAvailable").Single().Value);
        Assert.Equal("upload \"A & B\"", doc.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("Uploads <A>", doc.Descendants(ns + "Description").Single().Value);
    }

    [Fact]
    public async Task Schedule_list_and_cancel_from_the_command_line()
    {
        _repo.AddMod("Alpha");
        TestConfig.RimWorld().Save(_repo.Root);
        async Task<(int, string)> Run(params string[] a)
        {
            var sw = new StringWriter();
            var code = await Cli.RunAsync(Cli.Parse(a), _repo.Root, "steamcmd", "modder", sw, CancellationToken.None,
                Path.Combine(_data, "uploaded.json"), _data, settings: new AppSettings(), scheduleNow: Now);
            return (code, sw.ToString());
        }

        var (code, text) = await Run("schedule", "--at", "18:00", "Alpha", "--announce");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Alpha uploads at 2026-10-20 18:00", text);
        var create = Assert.Single(_calls);
        Assert.Equal(new[] { "/Create", "/TN", @"SteamCMD Helper\Upload 2026-10-20 1800 Alpha" }, create[..3]);
        Assert.False(File.Exists(create[4])); // the XML file is removed after use

        (code, text) = await Run("schedule", "--list");
        Assert.Contains("1. 2026-10-20 18:00  |  Alpha", text);

        (code, text) = await Run("schedule", "--at", "12:00", "Nope");
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("no such mod in the repo: Nope", text);

        (code, text) = await Run("schedule", "--cancel", "1");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Equal("/Delete", _calls[^1][0]);
        (_, text) = await Run("schedule", "--list");
        Assert.Contains("No uploads are scheduled.", text);
    }

    [Fact]
    public async Task A_time_already_passed_or_a_refusal_is_reported()
    {
        var (done, error) = await ScheduledUploads.AddAsync(Now.AddMinutes(-1), Now, _repo.Root, "Alpha", new() { "upload" }, "app.exe", Path.Combine(_data, "s.json"), _data);
        Assert.Null(done);
        Assert.Contains("already passed", error);
        ScheduledUploads.Runner = (_, _) => Task.FromResult((1, "ERROR: Access is denied."));
        (done, error) = await ScheduledUploads.AddAsync(Now.AddHours(1), Now, _repo.Root, "Alpha", new() { "upload" }, "app.exe", Path.Combine(_data, "s.json"), _data);
        Assert.Null(done);
        Assert.Equal("Task Scheduler refused it: ERROR: Access is denied.", error);
        Assert.Empty(ScheduledUploads.Load(Now, Path.Combine(_data, "s.json")));
    }

    [Fact]
    public void Schedule_options_are_checked()
    {
        Assert.Equal("Give one of --at <time>, --list and --cancel <which>.", Cli.Parse(new[] { "schedule", "Alpha" }).Error);
        Assert.Equal("Name the mods to upload later, or give one of --group, --all and --changed.", Cli.Parse(new[] { "schedule", "--at", "18:00" }).Error);
        Assert.NotNull(Cli.Parse(new[] { "schedule", "--at", "18:00", "Alpha", "--dry-run" }).Error);
        Assert.Equal("--at, --list and --cancel only go with \"schedule\".", Cli.Parse(new[] { "upload", "Alpha", "--at", "18:00" }).Error);
        Assert.Null(Cli.Parse(new[] { "schedule", "--at", "+2h", "--all", "--allow-create" }).Error);
    }
}
