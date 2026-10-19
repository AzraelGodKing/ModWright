using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>
/// The SteamCMD jobs besides a plain upload: fail-fast flags, one login per batch, licenses, game branches, Workshop
/// downloads, test installs and the GitHub Actions setup.
/// </summary>
public sealed class SteamCmdExtrasTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _work = Path.Combine(Path.GetTempPath(), "steamcmd_helper_extras_" + Guid.NewGuid().ToString("N"));
    readonly string _temp, _record;

    public SteamCmdExtrasTests()
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

    // The fake SteamCMD is a shell script, so these run on Linux and macOS only.
    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    string Calls => Path.Combine(_work, "calls.txt");
    string[] CallLines() => File.Exists(Calls) ? File.ReadAllLines(Calls) : Array.Empty<string>();

    /// <summary>A fake steamcmd: logs its raw arguments to calls.txt, then runs <paramref name="script"/> (flags dropped).</summary>
    string Fake(string script)
    {
        var dir = Path.Combine(_work, "steamcmd");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "steamcmd.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + $"echo \"$*\" >> '{Calls}'\n" + TestConfig.SkipSteamFlags + script);
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    /// <summary>
    /// A fake that uploads every build file it is given in one run: a login line, then per build file "Uploading
    /// content..." and "Success." (or a failure when the file names <paramref name="failFor"/>).
    /// </summary>
    string SessionFake(bool loginOk = true, string? failFor = null) => Fake(
        (loginOk ? "echo \"Logging in user '$2' to Steam Public...OK\"\n" : "echo \"Logging in user '$2' to Steam Public...FAILED (Invalid Password)\"\nexit 5\n")
        + "shift 2\n"
        + "while [ $# -gt 0 ]; do\n"
        + "  if [ \"$1\" = '+workshop_build_item' ]; then\n"
        + "    echo 'Uploading content...'\n"
        + (failFor != null ? $"    if grep -q '{failFor}' \"$2\"; then echo 'ERROR! Failed to update workshop item (Access Denied).'; shift 2; continue; fi\n" : "")
        + "    echo 'Committing update...Success.'\n"
        + "    shift 2\n"
        + "  else shift; fi\n"
        + "done\n"
        + "exit 0\n");

    async Task<(int code, string text)> Run(string steamCmd, params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, _repo.Root, steamCmd, "modder", sw, CancellationToken.None, _record, _temp);
        return (code, sw.ToString());
    }

    // ------------------------------------------------------------ fail fast

    [Fact]
    public async Task Uploads_and_login_checks_tell_steamcmd_to_fail_fast()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var steam = Fake("echo 'Waiting for user info...OK'\necho 'Committing update...Success.'\nexit 0\n");

        var (code, text) = await Run(steam, "upload", "Alpha");

        Assert.Equal(Cli.ExitOk, code);
        var calls = CallLines();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, c => Assert.StartsWith("+@ShutdownOnFailedCommand 1 +@NoPromptForPassword 1 +login modder", c));
    }

    // ------------------------------------------------------------ one login per batch

    [Fact]
    public async Task One_session_uploads_every_mod_in_one_steamcmd_run()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        _repo.AddMod("Gamma", workshopId: "3333333");
        var steam = SessionFake(failFor: "2222222");
        var cfg = TestConfig.RimWorld();
        var started = new List<int>();
        var batch = new BatchUpload(new Publisher(_repo.Root, cfg, steam, "modder", _temp), _record) { OneSession = true };
        batch.Starting += (_, i, _) => started.Add(i);

        var results = await batch.RunAsync(ModScanner.Scan(_repo.Root, cfg), upload: true, CancellationToken.None);

        Assert.Equal(new[] { BatchStatus.Uploaded, BatchStatus.Failed, BatchStatus.Uploaded }, results.Select(r => r.Status));
        Assert.Contains("Access Denied", results[1].Message);
        var call = Assert.Single(CallLines());
        Assert.Equal(3, call.Split("+workshop_build_item").Length - 1);
        Assert.Contains("+@ShutdownOnFailedCommand 0", call); // one failed mod must not stop the rest
        Assert.Equal("1.2.3", UploadedVersions.LoadRecord(_record)["3333333"]);
        Assert.False(UploadedVersions.LoadRecord(_record).ContainsKey("2222222"));
        Assert.Contains(2, started);
        // Only the failed mod's package is kept, for inspection.
        var kept = Assert.Single(Directory.GetDirectories(_temp, Publisher.StagePrefix + "*"));
        Assert.True(File.Exists(Path.Combine(kept, "Beta.vdf")));
    }

    [Fact]
    public async Task One_session_with_a_refused_login_fails_the_first_and_skips_the_rest()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        var steam = SessionFake(loginOk: false);
        var cfg = TestConfig.RimWorld();
        var batch = new BatchUpload(new Publisher(_repo.Root, cfg, steam, "modder", _temp), _record) { OneSession = true };

        var results = await batch.RunAsync(ModScanner.Scan(_repo.Root, cfg), upload: true, CancellationToken.None);

        Assert.Equal(new[] { BatchStatus.Failed, BatchStatus.NotRun }, results.Select(r => r.Status));
        Assert.Single(CallLines());
        Assert.Contains("Steam refused the login", results[1].Message);
    }

    [Fact]
    public async Task One_session_leaves_mods_that_fail_their_checks_out_of_the_steamcmd_run()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        File.WriteAllText(Path.Combine(_repo.Root, "Beta", "About", "changelog.txt"), "0.0.1\n- Old entry only.");
        var steam = SessionFake();
        var cfg = TestConfig.RimWorld();
        var batch = new BatchUpload(new Publisher(_repo.Root, cfg, steam, "modder", _temp), _record) { OneSession = true };

        var results = await batch.RunAsync(ModScanner.Scan(_repo.Root, cfg), upload: true, CancellationToken.None);

        Assert.Equal(new[] { BatchStatus.Uploaded, BatchStatus.Failed }, results.Select(r => r.Status));
        Assert.Contains("no entry for version", results[1].Message);
        Assert.Equal(1, CallLines().Single().Split("+workshop_build_item").Length - 1);
    }

    [Fact]
    public void Lines_that_end_an_upload_are_its_success_or_failure()
    {
        Assert.True(SteamCmdOutput.EndsItem("Committing update...Success."));
        Assert.True(SteamCmdOutput.EndsItem("Success."));
        Assert.True(SteamCmdOutput.EndsItem("ERROR! Failed to update workshop item (Access Denied)."));
        Assert.False(SteamCmdOutput.EndsItem("Uploading content..."));
        Assert.False(SteamCmdOutput.EndsItem("Loading Textures/Error.png"));
    }

    // ------------------------------------------------------------ licenses

    [Fact]
    public void Licenses_list_their_apps()
    {
        var apps = SteamCmd.ParseLicenses(new[]
        {
            "License packageID 0:",
            " - State   : Active( flags 0 ) - Purchased : Sat Jan  1 00:00:00 2022 in \"US\", Complimentary",
            " - Apps    : 7,  (1 in total)",
            " - Depots  : 228980,  (1 in total)",
            "License packageID 12345:",
            " - Apps    : 294100, 294101,  (2 in total)",
        });
        Assert.NotNull(apps);
        Assert.Contains("294100", apps!);
        Assert.Contains("7", apps!);
        Assert.DoesNotContain("228980", apps!);
        Assert.DoesNotContain("2", apps!); // "(2 in total)" is a count, not an app
    }

    [Fact]
    public void No_license_list_means_unknown() => Assert.Null(SteamCmd.ParseLicenses(new[] { "Logging in user 'x' to Steam Public...FAILED (Invalid Password)" }));

    [Fact]
    public async Task Ownership_comes_from_licenses_print()
    {
        if (!CanFakeSteamCmd) return;
        var steam = Fake("echo 'Waiting for user info...OK'\necho ' - Apps    : 294100,  (1 in total)'\nexit 0\n");
        Assert.True(await SteamCmd.OwnsAsync(steam, "modder", "294100", CancellationToken.None));
        Assert.False(await SteamCmd.OwnsAsync(steam, "modder", "431240", CancellationToken.None));
        Assert.Contains("+licenses_print", CallLines()[0]);
    }

    [Fact]
    public async Task Upload_warns_when_the_account_does_not_own_the_game()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var steam = Fake("echo 'Waiting for user info...OK'\necho 'Committing update...Success.'\nexit 0\n");
        var o = Cli.Parse(new[] { "upload", "Alpha" });
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, _repo.Root, steam, "modder", sw, CancellationToken.None, _record, _temp,
            ownership: (_, _, _) => Task.FromResult<bool?>(false));
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("does not include RimWorld", sw.ToString());
    }

    // ------------------------------------------------------------ KeyValues and game branches

    static readonly string[] AppInfo =
    {
        "Connecting anonymously to Steam Public...OK",
        "AppID : 294100, change number : 1/0, last change : Sat Oct 18 2026",
        "\"294100\"",
        "{",
        "\t\"common\"",
        "\t{",
        "\t\t\"name\"\t\t\"RimWorld \\\"Anomaly\\\"\"",
        "\t}",
        "\t\"depots\"",
        "\t{",
        "\t\t\"branches\"",
        "\t\t{",
        "\t\t\t\"unstable\"",
        "\t\t\t{",
        "\t\t\t\t\"buildid\"\t\t\"2000\"",
        "\t\t\t\t\"description\"\t\t\"Unstable testing branch\"",
        "\t\t\t\t\"timeupdated\"\t\t\"1792000000\"",
        "\t\t\t}",
        "\t\t\t\"public\"",
        "\t\t\t{",
        "\t\t\t\t\"buildid\"\t\t\"1000\"",
        "\t\t\t\t\"timeupdated\"\t\t\"1790000000\"",
        "\t\t\t}",
        "\t\t\t\"secret\"",
        "\t\t\t{",
        "\t\t\t\t\"buildid\"\t\t\"1500\"",
        "\t\t\t\t\"pwdrequired\"\t\t\"1\"",
        "\t\t\t}",
        "\t\t}",
        "\t}",
        "}",
    };

    [Fact]
    public void KeyValues_reads_nested_blocks_and_escapes()
    {
        var kv = KeyValues.Parse(string.Join("\n", AppInfo.Skip(2)));
        Assert.Equal("RimWorld \"Anomaly\"", kv.Block("294100")!.Block("COMMON")!.Value("name"));
        Assert.Equal("1000", kv.Block("294100")!.Block("depots")!.Block("branches")!.Block("public")!.Value("buildid"));
    }

    [Fact]
    public void Game_branches_are_read_from_app_info_public_first()
    {
        var b = GameBuilds.Parse("294100", AppInfo);
        Assert.Equal(new[] { "public", "secret", "unstable" }, b.Select(x => x.Name));
        Assert.Equal("1000", b[0].BuildId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), b[0].Updated);
        Assert.True(b[1].Password);
        Assert.Equal("Unstable testing branch", b[2].Description);
        Assert.Contains("needs a password", GameBuilds.Describe(b[1]));
    }

    [Fact]
    public void Other_output_has_no_branches() => Assert.Empty(GameBuilds.Parse("294100", new[] { "No app info for AppID 294100 found" }));

    [Fact]
    public void Game_build_changes_are_new_public_builds_and_new_or_updated_betas()
    {
        var now = GameBuilds.Parse("294100", AppInfo);
        Assert.Empty(GameBuilds.Changes(now, null, "RimWorld")); // the first look has nothing to compare with

        var same = new SeenBuilds(DateTimeOffset.UtcNow, new() { ["public"] = "1000", ["unstable"] = "2000", ["secret"] = "1500" });
        Assert.Empty(GameBuilds.Changes(now, same, "RimWorld"));

        var older = new SeenBuilds(DateTimeOffset.UtcNow, new() { ["public"] = "900", ["unstable"] = "1900" });
        var changes = GameBuilds.Changes(now, older, "RimWorld");
        Assert.Equal(3, changes.Count);
        Assert.Contains(changes, c => c.StartsWith("RimWorld has a new public build (1000"));
        Assert.Contains(changes, c => c.Contains("new beta branch \"secret\""));
        Assert.Contains(changes, c => c.Contains("\"unstable\" beta was updated (build 2000"));
    }

    [Fact]
    public void Seen_builds_are_remembered_and_due_after_12_hours()
    {
        var file = Path.Combine(_work, "builds.json");
        Assert.True(GameBuilds.Due("294100", DateTimeOffset.UtcNow, file));
        GameBuilds.Remember("294100", GameBuilds.Parse("294100", AppInfo), file);
        Assert.Equal("2000", GameBuilds.Load(file)["294100"].Builds["unstable"]);
        Assert.False(GameBuilds.Due("294100", DateTimeOffset.UtcNow, file));
        Assert.True(GameBuilds.Due("294100", DateTimeOffset.UtcNow.AddHours(13), file));
        Assert.True(GameBuilds.Due("431240", DateTimeOffset.UtcNow, file));
    }

    [Fact]
    public void Game_branches_are_read_anonymously_with_fresh_app_info()
    {
        var args = string.Join(" ", GameBuilds.Args("294100"));
        Assert.Contains("+login anonymous +app_info_update 1 +app_info_print 294100 +quit", args);
    }

    [Fact]
    public async Task Game_builds_command_lists_branches_then_reports_changes()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha");
        var steam = Fake(string.Concat(AppInfo.Select(l => $"printf '%s\\n' '{l.Replace("'", "'\\''")}'\n")) + "exit 0\n");

        var (code, text) = await Run(steam, "game-builds");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("public: build 1000", text);
        Assert.Contains("(anonymous login)", text);

        GameBuilds.Remember("294100", new List<GameBranch> { new("public", "999", null, "", false) }, Path.Combine(_work, "game-builds.json"));
        (_, text) = await Run(steam, "game-builds");
        Assert.Contains("RimWorld has a new public build (1000", text);
    }

    // ------------------------------------------------------------ Workshop downloads

    [Fact]
    public void Download_output_gives_the_folder()
    {
        var r = WorkshopDownload.Evaluate(new[] { "Downloading item 123456789 ...", "Success. Downloaded item 123456789 to \"C:\\steamcmd\\steamapps\\workshop\\content\\294100\\123456789\" (2048 bytes)" },
            0, "/x/steamcmd.sh", "294100", "123456789");
        Assert.True(r.Ok);
        Assert.Equal("C:\\steamcmd\\steamapps\\workshop\\content\\294100\\123456789", r.Folder);
    }

    [Theory]
    [InlineData("Logging in user 'modder' to Steam Public...FAILED (Invalid Password)", "refused the login")]
    [InlineData("ERROR! Download item 123456789 failed (Access Denied).", "could not download")]
    [InlineData("Unloading Steam API...OK", "without saying")]
    public void Failed_downloads_say_why(string line, string expected)
    {
        var r = WorkshopDownload.Evaluate(new[] { line }, 0, Path.Combine(_work, "nope", "steamcmd.sh"), "294100", "123456789");
        Assert.False(r.Ok);
        Assert.Contains(expected, r.Message);
    }

    [Fact]
    public void Downloads_are_compared_by_path_and_content()
    {
        var folder = Path.Combine(_work, "dl");
        Directory.CreateDirectory(Path.Combine(folder, "About"));
        File.WriteAllText(Path.Combine(folder, "About", "About.xml"), "same");
        File.WriteAllText(Path.Combine(folder, "changed.txt"), "steam's");
        File.WriteAllText(Path.Combine(folder, "extra.txt"), "x");
        var sent = PackageCheck.Inspect(folder, hash: true).Files.Where(f => f.Path == "About/About.xml").ToList();
        sent.Add(new PackageFile("changed.txt", 3, "00"));
        sent.Add(new PackageFile("missing.txt", 1, "00"));

        var c = WorkshopDownload.Compare(sent, folder);

        Assert.False(c.Matches);
        Assert.Equal(new[] { "missing.txt" }, c.Missing);
        Assert.Equal(new[] { "changed.txt" }, c.Different);
        Assert.Equal(new[] { "extra.txt" }, c.Extra);
        Assert.StartsWith("Warning: Steam's copy does not match", WorkshopDownload.Describe(c, "this upload")[0]);
        Assert.True(WorkshopDownload.Compare(PackageCheck.Inspect(folder, hash: true).Files, folder).Matches);
    }

    [Fact]
    public void Restores_never_overwrite_and_cleanup_stays_inside_steamcmd()
    {
        var src = Path.Combine(_work, "src");
        Directory.CreateDirectory(Path.Combine(src, "a"));
        File.WriteAllText(Path.Combine(src, "a", "f.txt"), "x");
        var dest = Path.Combine(_work, "dest");
        Assert.Equal(1, WorkshopDownload.CopyTo(src, dest));
        Assert.Throws<IOException>(() => WorkshopDownload.CopyTo(src, dest));

        WorkshopDownload.Forget(Path.Combine(_work, "steamcmd", "steamcmd.sh"), dest); // not inside SteamCMD's folder
        Assert.True(Directory.Exists(dest));
        var cached = Path.Combine(_work, "steamcmd", "steamapps", "workshop", "content", "1", "2");
        Directory.CreateDirectory(cached);
        WorkshopDownload.Forget(Path.Combine(_work, "steamcmd", "steamcmd.sh"), cached);
        Assert.False(Directory.Exists(cached));
    }

    /// <summary>A fake that "downloads" an item into SteamCMD's Workshop folder and prints the success line.</summary>
    string DownloadFake(params (string Path, string Text)[] files)
    {
        var steamDir = Path.Combine(_work, "steamcmd");
        var script = "echo 'Waiting for user info...OK'\n"
            + "dir=\"$(dirname \"$0\")/steamapps/workshop/content/$4/$5\"\n"
            + "mkdir -p \"$dir\"\n"
            + string.Concat(files.Select(f => $"mkdir -p \"$(dirname \"$dir/{f.Path}\")\"\nprintf '%s' '{f.Text}' > \"$dir/{f.Path}\"\n"))
            + "echo \"Success. Downloaded item $5 to \\\"$dir\\\" (10 bytes)\"\nexit 0\n";
        return Fake(script);
    }

    [Fact]
    public async Task Download_command_saves_steams_copy_into_an_empty_folder()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var steam = DownloadFake(("About/About.xml", "<x/>"), ("Assemblies/Alpha.dll", "dll"));
        var dest = Path.Combine(_work, "restored");

        var (code, text) = await Run(steam, "download", "Alpha", "--to", dest);

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Saved 2 files", text);
        Assert.Equal("dll", File.ReadAllText(Path.Combine(dest, "Assemblies", "Alpha.dll")));
        Assert.Contains("+workshop_download_item 294100 1111111 validate", CallLines()[0]);
        Assert.False(Directory.Exists(Path.Combine(_work, "steamcmd", "steamapps", "workshop", "content", "294100", "1111111")));

        (code, text) = await Run(steam, "download", "1111111", "--to", dest);
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("is not empty", text);
    }

    [Fact]
    public async Task Verify_upload_downloads_it_back_and_compares()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var cfg = TestConfig.RimWorld();
        var steam = Fake("echo 'Waiting for user info...OK'\n"
            + "if [ \"$3\" = '+workshop_download_item' ]; then dir=\"$(dirname \"$0\")/dl\"; mkdir -p \"$dir/About\"; printf 'only this' > \"$dir/About/About.xml\"; "
            + "echo \"Success. Downloaded item $5 to \\\"$dir\\\" (9 bytes)\"; exit 0; fi\n"
            + "echo 'Committing update...Success.'\nexit 0\n");
        var pub = new Publisher(_repo.Root, cfg, steam, "modder", _temp) { VerifyUploads = true };
        var log = new List<string>();
        pub.Log += log.Add;

        var r = await pub.RunAsync(ModScanner.Scan(_repo.Root, cfg).Single(), true, CancellationToken.None);

        Assert.True(r.Ok, r.Message);
        Assert.Contains(log, l => l.StartsWith("Warning: Steam's copy does not match this upload"));
        Assert.Contains(log, l => l.Contains("- Assemblies/Alpha.dll"));
        Assert.False(Directory.Exists(Path.Combine(_work, "steamcmd", "dl")));
    }

    [Fact]
    public async Task One_session_checks_uploads_in_the_same_steamcmd_run()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        var cfg = TestConfig.RimWorld();
        // Uploads every build file, then "downloads" each item it is asked for as a copy holding only About/About.xml.
        var steam = Fake("echo \"Logging in user '$2' to Steam Public...OK\"\n"
            + "shift 2\n"
            + "while [ $# -gt 0 ]; do\n"
            + "  if [ \"$1\" = '+workshop_build_item' ]; then echo 'Committing update...Success.'; shift 2\n"
            + "  elif [ \"$1\" = '+workshop_download_item' ]; then dir=\"$(dirname \"$0\")/dl/$3\"; mkdir -p \"$dir/About\"; printf 'x' > \"$dir/About/About.xml\"; "
            + "echo \"Success. Downloaded item $3 to \\\"$dir\\\" (1 bytes)\"; shift 4\n"
            + "  else shift; fi\n"
            + "done\n"
            + "exit 0\n");
        var pub = new Publisher(_repo.Root, cfg, steam, "modder", _temp) { VerifyUploads = true };
        var log = new List<string>();
        pub.Log += log.Add;
        var batch = new BatchUpload(pub, _record) { OneSession = true };

        var results = await batch.RunAsync(ModScanner.Scan(_repo.Root, cfg), upload: true, CancellationToken.None);

        Assert.All(results, r => Assert.Equal(BatchStatus.Uploaded, r.Status));
        var call = Assert.Single(CallLines()); // one login for the uploads and the checks
        Assert.Contains("+workshop_download_item 294100 1111111 validate", call);
        Assert.Contains("+workshop_download_item 294100 2222222 validate", call);
        Assert.True(call.IndexOf("+workshop_build_item", StringComparison.Ordinal) < call.IndexOf("+workshop_download_item", StringComparison.Ordinal));
        Assert.Equal(2, log.Count(l => l.StartsWith("Warning: Steam's copy does not match this upload")));
        Assert.DoesNotContain(log, l => l.StartsWith("Downloading ")); // no separate download run
        Assert.False(Directory.Exists(Path.Combine(_work, "steamcmd", "dl", "1111111")));
    }

    [Fact]
    public async Task A_batch_packages_ahead_and_leaves_no_packages_behind()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        _repo.AddMod("Gamma", workshopId: "3333333");
        var cfg = TestConfig.RimWorld();
        var steam = Fake("echo 'Waiting for user info...OK'\necho 'Committing update...Success.'\nexit 0\n");
        var batch = new BatchUpload(new Publisher(_repo.Root, cfg, steam, "modder", _temp), _record);

        var results = await batch.RunAsync(ModScanner.Scan(_repo.Root, cfg), upload: true, CancellationToken.None);

        Assert.All(results, r => Assert.Equal(BatchStatus.Uploaded, r.Status));
        Assert.Equal(3, CallLines().Length);
        Assert.Empty(Directory.GetDirectories(_temp, Publisher.StagePrefix + "*"));
    }

    [Fact]
    public async Task Packages_started_ahead_but_not_used_are_removed()
    {
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");
        var cfg = TestConfig.RimWorld();
        var pub = new Publisher(_repo.Root, cfg, "no-steamcmd-needed", "modder", _temp);

        pub.PackageAhead(ModScanner.Scan(_repo.Root, cfg), CancellationToken.None);
        await pub.DropAheadAsync();

        Assert.Empty(Directory.GetDirectories(_temp, Publisher.StagePrefix + "*"));
    }

    // ------------------------------------------------------------ test installs

    [Fact]
    public void Test_install_arguments_set_the_folder_before_logging_in()
    {
        var a = string.Join(" ", TestInstall.Args("D:/rw", "modder", "294100", "unstable", null));
        Assert.Contains("+force_install_dir D:/rw +login modder +app_update 294100 -beta unstable validate +quit", a);
        Assert.DoesNotContain("-beta", string.Join(" ", TestInstall.Args("D:/rw", "modder", "294100", "public", null)));
        Assert.Contains("-beta secret -betapassword hunter2", string.Join(" ", TestInstall.Args("D:/rw", "modder", "294100", "secret", "hunter2")));
    }

    [Fact]
    public void Test_install_progress_and_outcome_are_read_from_steamcmd()
    {
        Assert.Equal(("downloading", 12.5), TestInstall.ParseProgress(" Update state (0x61) downloading, progress: 12.50 (100 / 800)"));
        Assert.Null(TestInstall.ParseProgress("Uploading content..."));
        Assert.True(TestInstall.Evaluate(new[] { "Success! App '294100' fully installed." }, 0, "294100").Ok);
        var bad = TestInstall.Evaluate(new[] { "ERROR! Failed to install app '294100' (Invalid Beta Password)" }, 8, "294100");
        Assert.False(bad.Ok);
        Assert.Contains("needs a password", bad.Message);
    }

    [Fact]
    public void Test_installs_only_go_into_empty_folders_or_an_earlier_test_install()
    {
        var folder = Path.Combine(_work, "install");
        Assert.Null(TestInstall.FolderProblem(folder, "294100"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");
        Assert.NotNull(TestInstall.FolderProblem(folder, "294100"));
        Directory.CreateDirectory(Path.Combine(folder, "steamapps"));
        File.WriteAllText(Path.Combine(folder, "steamapps", "appmanifest_294100.acf"), "x");
        Assert.Null(TestInstall.FolderProblem(folder, "294100"));
    }

    [Fact]
    public async Task Test_install_puts_the_mod_and_its_dependencies_in_the_mods_folder()
    {
        if (!CanFakeSteamCmd) return;
        var modDir = _repo.AddMod("Alpha", workshopId: "1111111");
        File.WriteAllText(Path.Combine(modDir, "About", "About.xml"),
            "<ModMetaData><name>Alpha Display</name><modVersion>1.2.3</modVersion><description>A test mod.</description><modDependencies><li>" +
            "<steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li></modDependencies></ModMetaData>");
        var steam = Fake("echo 'Waiting for user info...OK'\n"
            + "if [ \"$1\" = '+force_install_dir' ]; then mkdir -p \"$2/steamapps\"; echo x > \"$2/steamapps/appmanifest_294100.acf\"; echo \"Success! App '294100' fully installed.\"; exit 0; fi\n"
            + "dir=\"$(dirname \"$0\")/steamapps/workshop/content/$4/$5\"; mkdir -p \"$dir/About\"; echo dep > \"$dir/About/About.xml\"\n"
            + "echo \"Success. Downloaded item $5 to \\\"$dir\\\" (4 bytes)\"\nexit 0\n");
        var folder = Path.Combine(_work, "rw-beta");

        var (code, text) = await Run(steam, "test-install", "--to", folder, "--branch", "unstable", "Alpha");

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("The game is installed.", text);
        Assert.True(File.Exists(Path.Combine(folder, "Mods", "Alpha", "Assemblies", "Alpha.dll")));
        Assert.True(File.Exists(Path.Combine(folder, "Mods", "2009463077", "About", "About.xml")));
        Assert.Contains("-beta unstable", CallLines()[0]);
    }

    // ------------------------------------------------------------ GitHub Actions

    [Fact]
    public void Workflow_is_written_once_and_names_its_secrets()
    {
        var path = GitHubActions.Write(_repo.Root);
        var text = File.ReadAllText(path);
        Assert.Contains("secrets.STEAM_CONFIG_VDF", text);
        Assert.Contains("secrets.STEAM_ACCOUNT", text);
        Assert.Contains("'upload', '--changed'", text);
        Assert.Contains("runs-on: windows-latest", text);
        Assert.DoesNotContain("\r", text);
        Assert.Throws<IOException>(() => GitHubActions.Write(_repo.Root));
        GitHubActions.Write(_repo.Root, overwrite: true);
    }

    [Fact]
    public void Login_secret_is_steamcmds_config_vdf_in_base64()
    {
        var steam = Path.Combine(_work, "sc", "steamcmd.exe");
        Assert.Null(GitHubActions.LoginSecret(steam));
        Directory.CreateDirectory(Path.Combine(_work, "sc", "config"));
        File.WriteAllText(Path.Combine(_work, "sc", "config", "config.vdf"), "\"InstallConfigStore\" {}");
        Assert.Equal("\"InstallConfigStore\" {}", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(GitHubActions.LoginSecret(steam)!)));
    }

    [Fact]
    public async Task Ci_setup_writes_the_workflow_and_the_next_steps()
    {
        _repo.AddMod("Alpha");
        var (code, text) = await Run("steamcmd", "ci-setup");
        Assert.Equal(Cli.ExitOk, code);
        Assert.True(File.Exists(Path.Combine(_repo.Root, ".github", "workflows", "steam-workshop-upload.yml")));
        Assert.Contains("STEAM_CONFIG_VDF", text);
        (code, _) = await Run("steamcmd", "ci-setup");
        Assert.Equal(Cli.ExitFailed, code);
        (code, _) = await Run("steamcmd", "ci-setup", "--force");
        Assert.Equal(Cli.ExitOk, code);
    }

    // ------------------------------------------------------------ parsing the new commands

    [Theory]
    [InlineData("download", "Alpha")]
    [InlineData("download", "--to", "x")]
    [InlineData("download", "A", "B", "--to", "x")]
    [InlineData("verify")]
    [InlineData("test-install", "Alpha")]
    [InlineData("game-builds", "Alpha")]
    [InlineData("list", "--one-session")]
    [InlineData("upload", "Alpha", "--to", "x")]
    [InlineData("upload", "Alpha", "--branch", "unstable")]
    [InlineData("upload", "Alpha", "--force")]
    public void New_commands_reject_what_does_not_fit(params string[] args) => Assert.NotNull(Cli.Parse(args).Error);

    [Theory]
    [InlineData("download", "Alpha", "--to", "x")]
    [InlineData("verify", "Alpha", "Beta")]
    [InlineData("test-install", "--to", "x", "--branch", "unstable", "--beta-password", "pw", "Alpha")]
    [InlineData("game-builds")]
    [InlineData("ci-setup", "--force")]
    [InlineData("login-secret")]
    [InlineData("upload", "--changed", "--one-session", "--verify")]
    public void New_commands_parse(params string[] args)
    {
        Assert.True(Cli.IsCommand(args));
        Assert.Null(Cli.Parse(args).Error);
    }
}
