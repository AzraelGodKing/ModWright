using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Batch uploads and the command line, against a RimWorld-style temp repo and a fake SteamCMD.</summary>
public sealed class BatchAndCliTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _work = Path.Combine(Path.GetTempPath(), "steamcmd_helper_cli_" + Guid.NewGuid().ToString("N"));
    readonly string _temp, _record;

    public BatchAndCliTests()
    {
        _temp = Path.Combine(_work, "temp");
        _record = Path.Combine(_work, "uploaded.json");
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_work, true); } catch { }
    }

    // The fake SteamCMD is a shell script, so the upload tests run on Linux and macOS only.
    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    /// <summary>
    /// A stand-in for steamcmd. It logs each run's arguments to calls.txt, prints the login line, then for an
    /// upload prints <paramref name="uploadLines"/> unless the build file names <paramref name="failFor"/>.
    /// </summary>
    string FakeSteamCmd(bool loginOk = true, string? failFor = null, string uploadLine = "Success.")
    {
        var dir = Path.Combine(_work, "steamcmd");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "steamcmd.sh");
        var calls = Path.Combine(_work, "calls.txt");
        var body = "#!/bin/sh\n"
            + $"echo \"$*\" >> '{calls}'\n"
            + (loginOk ? "echo \"Logging in user '$2' to Steam Public...OK\"\necho 'Waiting for user info...OK'\n"
                       : "echo \"Logging in user '$2' to Steam Public...FAILED (Invalid Password)\"\nexit 5\n")
            + "[ \"$3\" = '+workshop_build_item' ] || exit 0\n"
            + (failFor != null ? $"if grep -q '{failFor}' \"$4\"; then echo 'ERROR! Failed to update workshop item (Access Denied).'; exit 0; fi\n" : "")
            + "echo 'Uploading content...'\n"
            + $"echo '{uploadLine}'\n"
            + "exit 0\n";
        File.WriteAllText(path, body);
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    string[] Calls() => File.Exists(Path.Combine(_work, "calls.txt")) ? File.ReadAllLines(Path.Combine(_work, "calls.txt")) : Array.Empty<string>();

    async Task<(int code, string text)> Run(string steamCmd, params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, o.Repo ?? _repo.Root, steamCmd, o.Account ?? "modder", sw, CancellationToken.None, _record, _temp);
        return (code, sw.ToString());
    }

    // ------------------------------------------------------------ parsing

    [Theory]
    [InlineData("upload")]
    [InlineData("LIST")]
    [InlineData("help")]
    [InlineData("--help")]
    public void Commands_are_recognised(string first) => Assert.True(Cli.IsCommand(new[] { first, "x" }));

    [Theory]
    [InlineData]
    [InlineData("--selftest", "out.txt")]
    [InlineData("--shot", "dir")]
    public void Other_arguments_open_the_window_or_old_checks(params string[] args) => Assert.False(Cli.IsCommand(args));

    [Fact]
    public void Parse_reads_mods_and_options()
    {
        var o = Cli.Parse(new[] { "upload", "Alpha", "Beta", "--repo", "C:/mods", "--dry-run", "--allow-create", "--account", "me", "--log", "l.txt" });
        Assert.Null(o.Error);
        Assert.Equal(new[] { "Alpha", "Beta" }, o.Mods);
        Assert.Equal("C:/mods", o.Repo);
        Assert.Equal("me", o.Account);
        Assert.Equal("l.txt", o.LogFile);
        Assert.True(o.DryRun && o.AllowCreate);
        Assert.False(o.All);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("upload", "Alpha", "--all")]
    [InlineData("upload", "Alpha", "--bogus")]
    [InlineData("upload", "Alpha", "--repo")]
    [InlineData("list", "Alpha")]
    [InlineData("uplaod", "Alpha")]
    public async Task Bad_arguments_exit_2_and_show_usage(params string[] args)
    {
        var (code, text) = await Run("steamcmd", args);
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Usage:", text);
    }

    // ------------------------------------------------------------ list and checks before uploading

    [Fact]
    public async Task List_shows_every_mod_and_its_status()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Broken", changelog: null);
        var (code, text) = await Run("steamcmd", "list");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Alpha", text);
        Assert.Contains("Broken", text);
        Assert.Contains("Fix:", text);
    }

    [Theory]
    [InlineData("upload", "Alpha", "--changed")]
    [InlineData("list", "--changed")]
    [InlineData("version", "Alpha", "patch", "--json")]
    public async Task Changed_and_json_only_go_where_they_belong(params string[] args)
    {
        var (code, text) = await Run("steamcmd", args);
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Usage:", text);
    }

    [Fact]
    public async Task List_json_is_the_only_output_and_parses()
    {
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Broken", changelog: null, workshopId: null);
        var (code, text) = await Run("steamcmd", "list", "--json");
        Assert.Equal(Cli.ExitOk, code);
        using var doc = System.Text.Json.JsonDocument.Parse(text);
        var mods = doc.RootElement.GetProperty("mods").EnumerateArray().ToList();
        Assert.Equal(2, mods.Count);
        var alpha = mods.Single(m => m.GetProperty("folder").GetString() == "Alpha");
        Assert.Equal("1.2.3", alpha.GetProperty("version").GetString());
        Assert.Equal("1111111", alpha.GetProperty("workshopId").GetString());
        Assert.True(alpha.GetProperty("ready").GetBoolean());
        var broken = mods.Single(m => m.GetProperty("folder").GetString() == "Broken");
        Assert.False(broken.GetProperty("ready").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, broken.GetProperty("workshopId").ValueKind);
        Assert.NotEmpty(broken.GetProperty("problems").EnumerateArray());
    }

    [Fact]
    public void Steam_version_prefers_the_change_note_then_this_pcs_uploads()
    {
        var last = new UploadEntry(DateTimeOffset.UtcNow, "1", "Alpha", "1.0.1", "");
        Assert.Equal("2.0.0", Cli.SteamVersion("2.0.0", last, "1.0.0"));
        Assert.Equal("1.0.1", Cli.SteamVersion(null, last, "1.0.0"));
        Assert.Equal("1.0.0", Cli.SteamVersion(null, null, "1.0.0"));
        Assert.Null(Cli.SteamVersion(null, null, null));
    }

    [Fact]
    public async Task Unknown_mod_uploads_nothing_and_names_the_mods()
    {
        _repo.AddMod("Alpha");
        var (code, text) = await Run("steamcmd", "upload", "Nope");
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Nope: no such mod", text);
        Assert.Contains("Mods in this repo: Alpha", text);
    }

    [Fact]
    public async Task A_mod_that_needs_fixing_stops_the_whole_command()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha");
        _repo.AddMod("Broken", changelog: null);
        var steam = FakeSteamCmd();
        var (code, text) = await Run(steam, "upload", "Alpha", "Broken");
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Nothing was uploaded", text);
        Assert.Empty(Calls());
    }

    [Fact]
    public async Task New_item_needs_allow_create()
    {
        _repo.AddMod("Fresh", workshopId: null);
        var (code, text) = await Run("steamcmd", "upload", "Fresh", "--dry-run");
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("--allow-create", text);
    }

    [Fact]
    public async Task Mods_are_found_by_display_name_too()
    {
        _repo.AddMod("Alpha");
        var (code, text) = await Run("steamcmd", "upload", "alpha display", "--dry-run");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("CHECKED  Alpha Display v1.2.3", text);
    }

    [Fact]
    public async Task Dry_run_all_skips_mods_that_need_fixing_and_does_not_log_in()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        _repo.AddMod("Broken", changelog: null);
        var (code, text) = await Run(Path.Combine(_work, "no-steamcmd"), "upload", "--all", "--dry-run");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Skipping Broken", text);
        Assert.Contains("Finished: 2 checked.", text);
        Assert.DoesNotContain("Checking the Steam login", text);
        Assert.Empty(Directory.GetDirectories(_temp));
    }

    [Fact]
    public async Task Real_upload_without_steamcmd_exits_3()
    {
        _repo.AddMod("Alpha");
        var (code, text) = await Run(Path.Combine(_work, "no-steamcmd"), "upload", "Alpha");
        Assert.Equal(Cli.ExitSteam, code);
        Assert.Contains("SteamCMD is not installed", text);
    }

    // ------------------------------------------------------------ real uploads with the fake SteamCMD

    [Fact]
    public async Task Upload_all_checks_the_login_once_then_uploads_each_mod()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        var steam = FakeSteamCmd();

        var (code, text) = await Run(steam, "upload", "--all");

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Finished: 2 uploaded.", text);
        Assert.Contains("UPLOADED Alpha Display v1.2.3 (Workshop id 1111111)", text);
        var calls = Calls();
        Assert.Equal(3, calls.Length);
        Assert.Equal("+login modder +quit", calls[0]);
        Assert.All(calls.Skip(1), c => Assert.Contains("+workshop_build_item", c));
        var record = UploadedVersions.LoadRecord(_record);
        Assert.Equal("1.2.3", record["1111111"]);
        Assert.Equal("1.2.3", record["2222222"]);
    }

    [Fact]
    public async Task Upload_changed_skips_mods_steam_already_has()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");                 // Steam has 1.2.3 already
        _repo.AddMod("Beta", workshopId: "2222222");                  // Steam has an older version
        _repo.AddMod("Gamma", workshopId: "3333333");                 // never uploaded from here: unknown
        UploadedVersions.Remember("1111111", "1.2.3", _record);
        UploadedVersions.Remember("2222222", "1.2.2", _record);
        var steam = FakeSteamCmd();

        var (code, text) = await Run(steam, "upload", "--changed");

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Skipping Alpha: Steam already has v1.2.3.", text);
        Assert.Contains("Beta: Steam has v1.2.2, uploading v1.2.3.", text);
        Assert.Contains("Gamma: the version on Steam is not known", text);
        Assert.Contains("Finished: 2 uploaded.", text);
        Assert.Equal(3, Calls().Length); // the login check, then Beta and Gamma
    }

    [Fact]
    public async Task Upload_changed_with_everything_up_to_date_does_nothing_and_exits_0()
    {
        _repo.AddMod("Alpha", workshopId: "1111111");
        UploadedVersions.Remember("1111111", "1.2.3", _record);
        var (code, text) = await Run("no-steamcmd-needed", "upload", "--changed");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("every mod on Steam is up to date", text);
    }

    [Fact]
    public async Task One_failed_mod_does_not_stop_the_others_and_exits_1()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        _repo.AddMod("Gamma", workshopId: "3333333");
        var steam = FakeSteamCmd(failFor: "2222222");

        var (code, text) = await Run(steam, "upload", "Alpha", "Beta", "Gamma");

        Assert.Equal(Cli.ExitFailed, code);
        Assert.Contains("Finished: 2 uploaded, 1 failed.", text);
        Assert.Contains("FAILED   Beta Display v1.2.3 (Workshop id 2222222): SteamCMD reported a failure", text);
        Assert.False(UploadedVersions.LoadRecord(_record).ContainsKey("2222222"));
    }

    [Fact]
    public async Task No_saved_login_exits_3_before_uploading_anything()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha");
        var steam = FakeSteamCmd(loginOk: false);
        var (code, text) = await Run(steam, "upload", "Alpha");
        Assert.Equal(Cli.ExitSteam, code);
        Assert.Contains("no saved Steam login", text);
        Assert.Single(Calls());
    }

    // ------------------------------------------------------------ the batch runner itself

    [Fact]
    public async Task Login_refused_mid_batch_marks_the_rest_not_run()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        var steam = FakeSteamCmd(loginOk: false);
        var cfg = TestConfig.RimWorld();
        var mods = ModScanner.Scan(_repo.Root, cfg);
        var batch = new BatchUpload(new Publisher(_repo.Root, cfg, steam, "modder", _temp), _record);

        var results = await batch.RunAsync(mods, upload: true, CancellationToken.None);

        Assert.Equal(new[] { BatchStatus.Failed, BatchStatus.NotRun }, results.Select(r => r.Status));
        Assert.Single(Calls()); // Beta never reached SteamCMD
        Assert.Contains("Steam refused the login", results[1].Message);
        Assert.Equal("1 failed, 1 not run", BatchUpload.Counts(results));
    }

    [Fact]
    public async Task Cancelled_batch_reports_every_mod_as_not_run()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        var cfg = TestConfig.RimWorld();
        var mods = ModScanner.Scan(_repo.Root, cfg);
        var batch = new BatchUpload(new Publisher(_repo.Root, cfg, "steamcmd", "modder", _temp), _record);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = await batch.RunAsync(mods, upload: false, cts.Token);

        Assert.All(results, r => Assert.Equal(BatchStatus.NotRun, r.Status));
        Assert.Contains("NOT RUN  Alpha Display", BatchUpload.Summary(results));
    }

    [Theory]
    [InlineData("SteamCMD reported a failure: Logging in user 'x' to Steam Public...FAILED (Invalid Password)", true)]
    [InlineData("SteamCMD reported a failure: FAILED login with result code Two-factor code mismatch", true)]
    [InlineData("SteamCMD reported a failure: ERROR! Failed to update workshop item (Access Denied).", false)]
    [InlineData("About/changelog.txt has no entry for version 1.0", false)]
    public void Login_failures_are_told_apart_from_item_failures(string reason, bool login) =>
        Assert.Equal(login, BatchUpload.IsLoginFailure(reason));
}
