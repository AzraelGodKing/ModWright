using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The build step, uploading from a git ref and rolling back, test items, retrying when Steam is busy.</summary>
public sealed class UploadControlTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_control_" + Guid.NewGuid().ToString("N"));
    readonly string _repo, _temp, _modDir;

    public UploadControlTests()
    {
        _repo = Path.Combine(_root, "repo");
        _temp = Path.Combine(_root, "temp");
        _modDir = Path.Combine(_repo, "MyMod");
        Directory.CreateDirectory(_temp);
        WriteMod("1.0.0", "1.0.0\n- First release.");
        File.WriteAllBytes(Path.Combine(_modDir, "About", "Preview.png"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(_modDir, "About", "steam_description.txt"), "A description long enough for a new item.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    static bool OnUnix => !OperatingSystem.IsWindows();

    void WriteMod(string version, string changelog)
    {
        Directory.CreateDirectory(Path.Combine(_modDir, "About"));
        File.WriteAllText(Path.Combine(_modDir, "About", "About.xml"), $"<ModMetaData><name>My Mod</name><modVersion>{version}</modVersion></ModMetaData>");
        File.WriteAllText(Path.Combine(_modDir, "About", "changelog.txt"), changelog);
    }

    void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.email=t@t", "-c", "user.name=t" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, p.StandardError.ReadToEnd());
    }

    static ProjectConfig Cfg(bool git) => TestConfig.RimWorld(c => { c.UseGitArchive = git; c.BuiltFiles = Array.Empty<string>(); });

    ModInfo Mod(string? id = "2001112223") => ModScanner.Scan(_repo, Cfg(false)).Single() is var m && id == null ? Without(m) : ModScanner.Scan(_repo, Cfg(false)).Single();

    static ModInfo Without(ModInfo m) { m.WorkshopId = null; return m; }

    /// <summary>A stand-in steamcmd: prints <paramref name="first"/> on its first run and Success after, writing <paramref name="newId"/> into a new item's build file.</summary>
    string FakeSteamCmd(string first = "Success.", string? newId = null)
    {
        var path = Path.Combine(_root, "steamcmd.sh");
        var runs = Path.Combine(_root, "runs.txt");
        var body = "#!/bin/sh\n"
            + $"echo run >> '{runs}'\n"
            + $"cp \"$4\" '{_root}/last.vdf'\n"
            + (newId != null ? $"sed -i 's/\"publishedfileid\" \"0\"/\"publishedfileid\" \"{newId}\"/' \"$4\"\n" : "")
            + $"if [ $(wc -l < '{runs}') -eq 1 ]; then echo '{first}'; else echo 'Success.'; fi\n"
            + "exit 0\n";
        File.WriteAllText(path, body);
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    int Runs => File.Exists(Path.Combine(_root, "runs.txt")) ? File.ReadAllLines(Path.Combine(_root, "runs.txt")).Length : 0;

    // ------------------------------------------------------------ build step

    [Fact]
    public async Task Build_step_reports_success_and_failure()
    {
        if (!OnUnix) return;
        var lines = new List<string>();
        Assert.True(await BuildStep.RunCheckedAsync(_repo, "echo built > out.txt && echo done", lines.Add, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(_repo, "out.txt")));
        Assert.Contains("    done", lines);
        Assert.False(await BuildStep.RunCheckedAsync(_repo, "echo broken; exit 3", lines.Add, CancellationToken.None));
        Assert.Contains(lines, l => l.Contains("exit code 3") && l.Contains("nothing was uploaded"));
        Assert.True(await BuildStep.RunCheckedAsync(_repo, "  ", lines.Add, CancellationToken.None));
    }

    [Fact]
    public async Task A_failing_build_step_stops_the_command_line_upload()
    {
        if (!OnUnix) return;
        File.WriteAllText(Path.Combine(_modDir, "About", "PublishedFileId.txt"), "2001112223");
        var o = Cli.Parse(new[] { "upload", "--all", "--dry-run" });
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, _repo, "steamcmd", "modder", sw, CancellationToken.None, Path.Combine(_root, "rec.json"), _temp, buildCommand: "exit 1");
        Assert.Equal(Cli.ExitFailed, code);
        Assert.Contains("build step failed", sw.ToString());

        o = Cli.Parse(new[] { "upload", "--all", "--dry-run", "--no-build" });
        Assert.Equal(Cli.ExitOk, await Cli.RunAsync(o, _repo, "steamcmd", "modder", new StringWriter(), CancellationToken.None, Path.Combine(_root, "rec.json"), _temp, buildCommand: "exit 1"));
    }

    [Theory]
    [InlineData("list", "--test")]
    [InlineData("version", "MyMod", "patch", "--ref", "v1")]
    public void Upload_only_options_are_refused_elsewhere(params string[] args) => Assert.NotNull(Cli.Parse(args).Error);

    // ------------------------------------------------------------ git refs and rolling back

    [Fact]
    public async Task Packaging_from_a_tag_sends_that_versions_files_and_note()
    {
        if (!OnUnix) return;
        Git("init", "-q");
        Git("add", "-A");
        Git("commit", "-q", "-m", "1.0.0");
        Git("tag", "MyMod-v1.0.0");
        WriteMod("1.1.0", "1.1.0\n- Second release.\n1.0.0\n- First release.");
        Git("commit", "-q", "-am", "1.1.0");

        var steam = FakeSteamCmd();
        var pub = new Publisher(_repo, Cfg(true), steam, "modder", _temp) { GitRef = "MyMod-v1.0.0" };
        var mod = ModScanner.Scan(_repo, Cfg(true)).Single();
        mod.WorkshopId = "2001112223";
        Assert.Equal("1.1.0", mod.Version);

        var r = await pub.RunAsync(mod, true, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
        Assert.Equal("1.0.0", r.Version);
        var vdf = File.ReadAllText(Path.Combine(_root, "last.vdf"));
        Assert.Contains("First release", vdf);
        Assert.DoesNotContain("Second release", vdf);

        var found = GitOps.FindUpload(_repo, "MyMod", "1.0.0", DateTimeOffset.UtcNow);
        Assert.Equal("refs/tags/MyMod-v1.0.0", found!.Value.Ref);
        Assert.False(GitOps.RefExists(_repo, "no-such-branch"));
        Assert.False(GitOps.RefExists(_repo, "--output=x"));
    }

    [Theory]
    [InlineData("1.2.0", "1.2.0\n- Fixed it.", "1.2.0 (rolled back to this version)\n- Fixed it.")]
    [InlineData("1.2.0", "- Fixed it.", "Rolled back to version 1.2.0.\n\n- Fixed it.")]
    [InlineData("2.0", "", "Rolled back to version 2.0.")]
    public void Rollback_note_keeps_the_version_first(string version, string note, string expected) =>
        Assert.Equal(expected, GitOps.RollbackNote(version, note));

    // ------------------------------------------------------------ test items

    [Fact]
    public async Task Test_upload_creates_a_private_test_item_and_leaves_the_real_one_alone()
    {
        if (!OnUnix) return;
        File.WriteAllText(Path.Combine(_modDir, "About", "PublishedFileId.txt"), "2001112223");
        var steam = FakeSteamCmd(newId: "3000000001");
        var pub = new Publisher(_repo, Cfg(false), steam, "modder", _temp) { TestItems = true, PageEdits = _ => new WorkshopPageEdit { Visibility = 0 } };
        var batch = await new BatchUpload(pub, Path.Combine(_root, "rec.json")).RunAsync(new[] { Mod() }, true, CancellationToken.None);

        Assert.Equal(BatchStatus.Created, batch[0].Status);
        Assert.Equal("3000000001", batch[0].WorkshopId);
        Assert.Equal("3000000001", File.ReadAllText(Path.Combine(_modDir, ModInfo.TestIdFile)));
        Assert.Equal("2001112223", File.ReadAllText(Path.Combine(_modDir, "About", "PublishedFileId.txt")));
        var vdf = File.ReadAllText(Path.Combine(_root, "last.vdf"));
        Assert.Contains("\"visibility\"      \"2\"", vdf); // private, whatever the real item's page change says
        Assert.Contains("My Mod (test)", vdf);
        Assert.False(File.Exists(Path.Combine(_root, "rec.json")));

        // The next test upload updates that item, and the id file is never packaged.
        var m = Mod();
        Assert.Equal("3000000001", m.TestWorkshopId);
        var again = await new BatchUpload(pub).RunAsync(new[] { m }, true, CancellationToken.None);
        Assert.Equal(BatchStatus.Uploaded, again[0].Status);
        Assert.Contains("\"publishedfileid\" \"3000000001\"", File.ReadAllText(Path.Combine(_root, "last.vdf")));
        PackageReport? report = null;
        pub.Packaged += (_, r) => report = r;
        await pub.RunAsync(m, false, CancellationToken.None);
        Assert.DoesNotContain(report!.Files, f => f.Path.EndsWith(ModInfo.TestIdFile));
    }

    // ------------------------------------------------------------ retrying

    [Theory]
    [InlineData("SteamCMD reported a failure: ERROR! Failed to update workshop item (Timeout).", true)]
    [InlineData("SteamCMD reported a failure: ERROR! Failed to update workshop item (Service Unavailable).", true)]
    [InlineData("SteamCMD reported a failure: ERROR! Failed to update workshop item (Access Denied).", false)]
    [InlineData("Login Failure: Rate Limit Exceeded", false)]
    public void Only_busy_or_unreachable_Steam_is_retried(string reason, bool expected) => Assert.Equal(expected, BatchUpload.IsTransient(reason));

    [Fact]
    public async Task A_busy_Steam_gets_one_more_try()
    {
        if (!OnUnix) return;
        File.WriteAllText(Path.Combine(_modDir, "About", "PublishedFileId.txt"), "2001112223");
        var steam = FakeSteamCmd(first: "ERROR! Failed to update workshop item (Timeout).");
        var pub = new Publisher(_repo, Cfg(false), steam, "modder", _temp);
        var retried = 0;
        var batch = new BatchUpload(pub, Path.Combine(_root, "rec.json")) { RetryAfter = TimeSpan.Zero };
        batch.Retrying += (_, _, _) => retried++;
        var results = await batch.RunAsync(new[] { Mod() }, true, CancellationToken.None);
        Assert.Equal(BatchStatus.Uploaded, results[0].Status);
        Assert.Equal(1, retried);
        Assert.Equal(2, Runs);
    }

    [Fact]
    public async Task Without_RetryAfter_a_busy_Steam_fails()
    {
        if (!OnUnix) return;
        File.WriteAllText(Path.Combine(_modDir, "About", "PublishedFileId.txt"), "2001112223");
        var steam = FakeSteamCmd(first: "ERROR! Failed to update workshop item (Timeout).");
        var results = await new BatchUpload(new Publisher(_repo, Cfg(false), steam, "modder", _temp)).RunAsync(new[] { Mod() }, true, CancellationToken.None);
        Assert.Equal(BatchStatus.Failed, results[0].Status);
        Assert.Equal(1, Runs);
    }

    [Fact]
    public void Settings_keep_build_steps_and_refs_per_folder()
    {
        var s = new AppSettings();
        s.SetBuild(@"C:\mods", "dotnet build");
        s.SetRef(@"C:\mods\", "release");
        Assert.Equal("dotnet build", s.BuildFor(@"c:\MODS"));
        Assert.Equal("release", s.RefFor(@"C:\mods"));
        s.SetRef(@"C:\mods", " ");
        Assert.Equal("", s.RefFor(@"C:\mods"));
    }
}
