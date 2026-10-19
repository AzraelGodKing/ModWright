using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The command line's after-upload steps, change-note options and JSON results, and the saved run logs.</summary>
public sealed class CliAndLogsTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _work = Path.Combine(Path.GetTempPath(), "steamcmd_helper_cli2_" + Guid.NewGuid().ToString("N"));
    readonly string _temp, _record;

    public CliAndLogsTests()
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

    static bool HasGit()
    {
        try { return GitOps.Run(Path.GetTempPath(), "--version").code == 0; } catch { return false; }
    }

    /// <summary>A stand-in for steamcmd: logs in, keeps each build file in builds.txt, and gives new items the id 7777777.</summary>
    string FakeSteamCmd()
    {
        var dir = Path.Combine(_work, "steamcmd");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "steamcmd.sh");
        var builds = Path.Combine(_work, "builds.txt");
        File.WriteAllText(path, "#!/bin/sh\n" + TestConfig.SkipSteamFlags
            + "echo \"Logging in user '$2' to Steam Public...OK\"\necho 'Waiting for user info...OK'\n"
            + "[ \"$3\" = '+workshop_build_item' ] || exit 0\n"
            + $"cat \"$4\" >> '{builds}'\n"
            + "if grep -q '\"publishedfileid\"[[:space:]]*\"0\"' \"$4\"; then echo 'PublishFileID : 7777777'; fi\n"
            + "echo 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    string Builds() => File.Exists(Path.Combine(_work, "builds.txt")) ? File.ReadAllText(Path.Combine(_work, "builds.txt")) : "";

    async Task<(int code, string text, string json)> Run(string steamCmd, string[] args, string webhook = "")
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var js = new StringWriter();
        var code = await Cli.RunAsync(o, _repo.Root, steamCmd, "modder", sw, CancellationToken.None, _record, _temp, webhook: webhook, json: js);
        return (code, sw.ToString(), js.ToString());
    }

    void GitInit()
    {
        GitOps.Run(_repo.Root, "init", "-q");
        GitOps.Run(_repo.Root, "config", "user.name", "Test");
        GitOps.Run(_repo.Root, "config", "user.email", "test@example.com");
        GitOps.Run(_repo.Root, "config", "commit.gpgsign", "false");
        GitOps.Run(_repo.Root, "config", "tag.gpgsign", "false");
    }

    void Commit(string message)
    {
        GitOps.Run(_repo.Root, "add", "-A");
        GitOps.Run(_repo.Root, "commit", "-q", "-m", message);
    }

    // ------------------------------------------------------------ parsing

    [Fact]
    public void Upload_takes_the_after_upload_and_note_options()
    {
        var o = Cli.Parse(new[] { "upload", "Alpha", "--tag", "--commit-ids", "--announce", "--note-from-git", "--json" });
        Assert.Null(o.Error);
        Assert.True(o.Tag && o.CommitIds && o.Announce && o.NoteFromGit && o.Json);

        var n = Cli.Parse(new[] { "upload", "--all", "--note", "- Fixed it" });
        Assert.Null(n.Error);
        Assert.Equal("- Fixed it", n.Note);
    }

    [Theory]
    [InlineData("list", "--tag")]
    [InlineData("list", "--announce")]
    [InlineData("list", "--commit-ids")]
    [InlineData("list", "--note-from-git")]
    [InlineData("list", "--note", "x")]
    [InlineData("help", "--json")]
    [InlineData("upload", "Alpha", "--note", "x", "--note-from-git")]
    [InlineData("upload", "Alpha", "--note", " ")]
    [InlineData("upload", "Alpha", "--test", "--tag")]
    public void Options_only_go_where_they_belong(params string[] args) => Assert.NotNull(Cli.Parse(args).Error);

    // ------------------------------------------------------------ uploads

    [Fact]
    public async Task Note_replaces_the_changelog_entry_for_every_mod()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Beta", workshopId: "2222222");

        var (code, _, _) = await Run(FakeSteamCmd(), new[] { "upload", "--all", "--note", "Hotfix for the save bug" });

        Assert.Equal(Cli.ExitOk, code);
        var builds = Builds();
        Assert.Equal(2, builds.Split("Hotfix for the save bug").Length - 1);
        Assert.DoesNotContain("Fixed the thing that was broken", builds);
    }

    [Fact]
    public async Task Json_results_go_out_alone_and_parse()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Fresh", workshopId: null);
        _repo.Write("Fresh/About/steam_description.txt", "A brand new mod for testing uploads.");

        var (code, text, json) = await Run(FakeSteamCmd(), new[] { "upload", "Alpha", "Fresh", "--allow-create", "--json" });

        Assert.True(code == Cli.ExitOk, text);
        Assert.Contains("Uploading 2 mods.", text);
        Assert.DoesNotContain("Uploading", json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        var mods = root.GetProperty("mods").EnumerateArray().ToList();
        Assert.Equal("uploaded", mods[0].GetProperty("status").GetString());
        Assert.Equal("1111111", mods[0].GetProperty("workshopId").GetString());
        Assert.Equal("created", mods[1].GetProperty("status").GetString());
        Assert.Equal("7777777", mods[1].GetProperty("workshopId").GetString());
    }

    [Fact]
    public async Task Json_says_why_nothing_ran()
    {
        _repo.AddMod("Alpha");
        var (code, _, json) = await Run("steamcmd", new[] { "upload", "Nope", "--json" });
        Assert.Equal(Cli.ExitUsage, code);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("Nope", doc.RootElement.GetProperty("error").GetString());
        Assert.Empty(doc.RootElement.GetProperty("mods").EnumerateArray());
    }

    [Fact]
    public async Task Tag_and_commit_ids_need_a_git_repo()
    {
        _repo.AddMod("Alpha");
        var (code, text, _) = await Run("steamcmd", new[] { "upload", "Alpha", "--tag" });
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("need the mods folder to be a git repo", text);
    }

    [Fact]
    public async Task Tag_tags_the_upload_and_commit_ids_commits_the_new_id()
    {
        if (!CanFakeSteamCmd || !HasGit()) return;
        GitInit();
        _repo.AddMod("Alpha", workshopId: "1111111");
        _repo.AddMod("Fresh", workshopId: null);
        _repo.Write("Fresh/About/steam_description.txt", "A brand new mod for testing uploads.");
        Commit("Add the mods");

        var (code, text, json) = await Run(FakeSteamCmd(), new[] { "upload", "Alpha", "Fresh", "--allow-create", "--tag", "--commit-ids", "--json" });

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Tagged Alpha-v1.2.3.", text);
        Assert.Contains("Tagged Fresh-v1.2.3.", text);
        Assert.Equal(0, GitOps.Run(_repo.Root, "rev-parse", "-q", "--verify", "refs/tags/Alpha-v1.2.3").code);
        Assert.Contains("Committed the Workshop ID file for Fresh Display.", text);
        Assert.Equal("Add Workshop ID for Fresh Display", GitOps.Run(_repo.Root, "log", "-1", "--format=%s").output);
        Assert.Equal("", GitOps.Run(_repo.Root, "status", "--porcelain").output);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Alpha-v1.2.3", doc.RootElement.GetProperty("mods")[0].GetProperty("tag").GetString());

        // A second run leaves the existing tag as it is.
        var (again, text2, _) = await Run(FakeSteamCmd(), new[] { "upload", "Alpha", "--tag" });
        Assert.Equal(Cli.ExitOk, again);
        Assert.Contains("Tag Alpha-v1.2.3 already exists; left as it is.", text2);
    }

    [Fact]
    public async Task Note_from_git_drafts_the_note_from_the_mods_commits()
    {
        if (!CanFakeSteamCmd || !HasGit()) return;
        GitInit();
        _repo.AddMod("Alpha", workshopId: "1111111");
        Commit("Add the mods");
        _repo.Write("Alpha/Defs/Things.xml", "<Defs/>");
        Commit("Add the storage shelf");

        var (code, text, _) = await Run(FakeSteamCmd(), new[] { "upload", "Alpha", "--note-from-git" });

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Alpha: change note drafted from 2 commits", text);
        Assert.Contains("Add the storage shelf", Builds());
    }

    [Fact]
    public async Task Announce_without_anything_set_up_says_so()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: "1111111");
        var (code, text, _) = await Run(FakeSteamCmd(), new[] { "upload", "Alpha", "--announce" });
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Nothing to announce to", text);
    }

    [Fact]
    public async Task Dry_run_does_not_tag_or_announce()
    {
        if (!HasGit()) return;
        GitInit();
        _repo.AddMod("Alpha", workshopId: "1111111");
        Commit("Add the mods");
        var (code, text, _) = await Run("steamcmd", new[] { "upload", "Alpha", "--dry-run", "--tag", "--announce" });
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Dry run: nothing was tagged, committed or announced.", text);
        Assert.NotEqual(0, GitOps.Run(_repo.Root, "rev-parse", "-q", "--verify", "refs/tags/Alpha-v1.2.3").code);
    }

    // ------------------------------------------------------------ run logs

    [Fact]
    public void Log_file_names_are_safe_and_sort_by_time()
    {
        var name = RunLogs.FileName(new DateTime(2026, 10, 16, 14, 3, 22), "upload Mod: \"A/B\"?");
        Assert.Equal("2026-10-16_14-03-22 upload Mod A B.log", name);
        Assert.Equal("upload A, B, C and 2 more", RunLogs.Label("upload", new[] { "A", "B", "C", "D", "E" }));
        Assert.Equal("upload A", RunLogs.Label("upload", new[] { "A" }));
    }

    [Fact]
    public void Saved_logs_list_newest_first_and_only_the_newest_are_kept()
    {
        var dir = Path.Combine(_work, "logs");
        var start = new DateTime(2026, 1, 1, 12, 0, 0);
        for (var i = 0; i < RunLogs.Keep + 5; i++)
            Assert.NotNull(RunLogs.Save(dir, "upload Run " + i, start.AddMinutes(i), "line " + i));
        File.WriteAllText(Path.Combine(dir, "notes.log"), "not a run log");

        var logs = RunLogs.List(dir);
        Assert.Equal(RunLogs.Keep, logs.Count);
        Assert.Equal("upload Run " + (RunLogs.Keep + 4), logs[0].Label);
        Assert.Equal(start.AddMinutes(RunLogs.Keep + 4), logs[0].Time);
        Assert.Equal("upload Run 5", logs[^1].Label);
        Assert.True(File.Exists(Path.Combine(dir, "notes.log")));
        Assert.StartsWith("line " + (RunLogs.Keep + 4), File.ReadAllText(logs[0].Path));
    }

    [Fact]
    public void Two_runs_in_the_same_second_both_keep_their_log()
    {
        var dir = Path.Combine(_work, "logs2");
        var t = new DateTime(2026, 1, 1, 12, 0, 0);
        var a = RunLogs.Save(dir, "upload A", t, "first");
        var b = RunLogs.Save(dir, "upload A", t, "second");
        Assert.NotEqual(a, b);
        Assert.Equal(2, RunLogs.List(dir).Count);
    }
}
