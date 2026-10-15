using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Package checks, typed change notes, git steps after an upload, Steam failure advice and time estimates.</summary>
public sealed class UploadWorkflowTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_workflow_" + Guid.NewGuid().ToString("N"));
    readonly string _repo, _temp, _modDir;

    public UploadWorkflowTests()
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

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    static ProjectConfig CopyConfig() => TestConfig.RimWorld(c => { c.UseGitArchive = false; c.BuiltFiles = Array.Empty<string>(); c.Exclude = Array.Empty<string>(); });
    ModInfo Mod() => new() { Dir = _modDir, Folder = "MyMod", RelPath = "MyMod", Name = "My Mod", Version = "1.0.0", WorkshopId = "2001112223" };

    [Fact]
    public void Package_warnings_name_the_kinds_of_stray_files()
    {
        var files = new[]
        {
            new PackageFile("About/About.xml", 100),
            new PackageFile("Textures/Icon.psd", 5_000_000),
            new PackageFile(".git/config", 10),
            new PackageFile("obj/Debug/x.cache", 10),
            new PackageFile("Assemblies/Mod.pdb", 10),
            new PackageFile("Thumbs.db", 10),
            new PackageFile("Backup.zip", 10),
            new PackageFile("Sounds/huge.ogg", PackageCheck.BigFile + 1),
        };

        var w = PackageCheck.Warn(files);

        Assert.Contains(w, x => x.Contains(".git") && x.Contains("obj"));
        Assert.Contains(w, x => x.Contains("source art") && x.Contains("Icon.psd"));
        Assert.Contains(w, x => x.Contains("leftover") && x.Contains("Mod.pdb") && x.Contains("Thumbs.db"));
        Assert.Contains(w, x => x.Contains("archives") && x.Contains("Backup.zip"));
        Assert.Contains(w, x => x.Contains("very large") && x.Contains("huge.ogg"));
        Assert.Empty(PackageCheck.Warn(new[] { new PackageFile("About/About.xml", 100), new PackageFile("Assemblies/Mod.dll", 2000) }));
    }

    [Fact]
    public async Task Dry_run_reports_the_package_and_warns_about_stray_files()
    {
        File.WriteAllText(Path.Combine(_modDir, "Icon.psd"), "art");
        var log = new List<string>();
        PackageReport? report = null;
        var pub = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp);
        pub.Log += log.Add;
        pub.Packaged += (_, r) => report = r;

        var r = await pub.RunAsync(Mod(), upload: false, CancellationToken.None);

        Assert.True(r.Ok, r.Message);
        Assert.NotNull(report);
        Assert.Contains(report!.Files, f => f.Path == "About/About.xml");
        Assert.Contains(log, l => l.StartsWith("Warning: the package has source art"));
    }

    [Fact]
    public async Task Empty_package_is_never_uploaded()
    {
        var cfg = CopyConfig();
        cfg.Exclude = new[] { "*" };
        var r = await new Publisher(_repo, cfg, "steamcmd", "modder", _temp).RunAsync(Mod(), upload: false, CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Contains("empty", r.Message);
    }

    [Fact]
    public async Task Typed_change_note_replaces_the_changelog_entry_even_when_the_entry_is_missing()
    {
        File.WriteAllText(Path.Combine(_modDir, "About", "changelog.txt"), "0.9.0\nOld.");
        var log = new List<string>();
        var pub = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp) { NoteOverride = _ => "Typed in the window." };
        pub.Log += log.Add;

        var r = await pub.RunAsync(Mod(), upload: false, CancellationToken.None);

        Assert.True(r.Ok, r.Message);
        Assert.Contains(log, l => l.EndsWith($"notes {"Typed in the window.".Length} chars"));
        Assert.Equal("", Publisher.NotesFor(Mod(), CopyConfig()));
    }

    [Theory]
    [InlineData("SteamCMD reported a failure: Logging in user 'x' to Steam Public...FAILED (Invalid Password)", "log in again", true)]
    [InlineData("SteamCMD reported a failure: FAILED (Rate Limit Exceeded)", "half an hour", true)]
    [InlineData("SteamCMD reported a failure: FAILED (Two-factor code mismatch)", "Steam Guard", true)]
    [InlineData("SteamCMD reported a failure: ERROR! Failed to update workshop item (Access Denied).", "own the item", false)]
    [InlineData("SteamCMD reported a failure: ERROR! Failed to update workshop item (File Not Found).", "Workshop ID", false)]
    [InlineData("SteamCMD exited with code 7.", null, false)]
    public void Failures_get_plain_advice_and_login_ones_stop_the_batch(string reason, string? advice, bool login)
    {
        var a = SteamCmdOutput.Advice(reason);
        if (advice == null) Assert.Null(a); else Assert.Contains(advice, a, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(login, BatchUpload.IsLoginFailure(reason));
    }

    [Fact]
    public void Summary_says_what_to_do_under_a_failed_mod()
    {
        var s = BatchUpload.Summary(new[] { new BatchItem(Mod(), BatchStatus.Failed, "ERROR! Failed to update workshop item (Access Denied).", "1") });
        Assert.Contains("What to do: Steam says this account may not change the item", s);
    }

    [Fact]
    public void Time_left_comes_from_the_speed_of_recent_uploads()
    {
        Assert.Null(UploadHistory.EstimateSeconds(Array.Empty<UploadEntry>(), 1000));
        UploadEntry E(long bytes, double seconds) => new(DateTimeOffset.UtcNow, "1", "m", "1", "", Bytes: bytes, Seconds: seconds);
        var history = new[] { E(10_000_000, 18), E(20_000_000, 28), E(0, 50) }; // 1 MB/s after the fixed 8 s; the last has no size
        Assert.Equal(8 + 30, UploadHistory.EstimateSeconds(history, 30_000_000)!.Value, 1);
    }

    [Theory]
    [InlineData("BetterStorage", "1.4.0", "BetterStorage-v1.4.0")]
    [InlineData("My Mod", "2.0 beta", "My-Mod-v2.0-beta")]
    [InlineData("Odd:Name?", "1..2", "Odd-Name-v1-2")]
    [InlineData("Mod", "1.0.lock", "Mod-v1.0")]
    public void Tag_names_are_valid_git_refs(string folder, string version, string tag)
    {
        Assert.Equal(tag, GitOps.TagName(folder, version));
    }

    static bool HasGit()
    {
        try { return GitOps.Run(Path.GetTempPath(), "--version").code == 0; } catch { return false; }
    }

    void InitRepo()
    {
        GitOps.Run(_repo, "init", "-q");
        GitOps.Run(_repo, "config", "user.name", "Test");
        GitOps.Run(_repo, "config", "user.email", "test@example.com");
        GitOps.Run(_repo, "config", "commit.gpgsign", "false");
        GitOps.Run(_repo, "config", "tag.gpgsign", "false");
        File.WriteAllText(Path.Combine(_repo, "Other.txt"), "other");
        GitOps.Run(_repo, "add", "-A");
        GitOps.Run(_repo, "commit", "-q", "-m", "start");
    }

    [Fact]
    public void Commit_takes_only_the_given_paths_and_tagging_twice_is_reported()
    {
        if (!HasGit()) return;
        InitRepo();
        File.WriteAllText(Path.Combine(_modDir, "About", "PublishedFileId.txt"), "123456789");
        File.WriteAllText(Path.Combine(_repo, "Other.txt"), "changed but not part of the upload");

        Assert.Null(GitOps.CommitPaths(_repo, new[] { "MyMod/About/PublishedFileId.txt" }, "Add Workshop ID for My Mod"));
        Assert.Null(GitOps.CommitPaths(_repo, new[] { "MyMod/About/PublishedFileId.txt" }, "nothing new")); // nothing to commit is fine

        var last = GitOps.Run(_repo, "show", "--name-only", "--format=%s", "HEAD").output;
        Assert.Contains("Add Workshop ID for My Mod", last);
        Assert.Contains("MyMod/About/PublishedFileId.txt", last);
        Assert.Contains("Other.txt", GitOps.Run(_repo, "status", "--porcelain").output); // left alone

        Assert.Null(GitOps.Tag(_repo, "MyMod-v1.0.0", "uploaded"));
        Assert.Equal("exists", GitOps.Tag(_repo, "MyMod-v1.0.0", "uploaded"));
    }
}
