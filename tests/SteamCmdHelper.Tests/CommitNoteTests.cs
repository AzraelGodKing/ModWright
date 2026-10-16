using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Drafting a change note from the commits that touched a mod since its last upload.</summary>
public sealed class CommitNoteTests : IDisposable
{
    readonly string _repo = Path.Combine(Path.GetTempPath(), "steamcmd_helper_notes_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_repo, true); } catch { } }

    static bool HasGit()
    {
        try { return GitOps.Run(Path.GetTempPath(), "--version").code == 0; } catch { return false; }
    }

    void Init()
    {
        Directory.CreateDirectory(_repo);
        GitOps.Run(_repo, "init", "-q");
        GitOps.Run(_repo, "config", "user.name", "Test");
        GitOps.Run(_repo, "config", "user.email", "test@example.com");
        GitOps.Run(_repo, "config", "commit.gpgsign", "false");
        GitOps.Run(_repo, "config", "tag.gpgsign", "false");
    }

    void Commit(string file, string message)
    {
        var path = Path.Combine(_repo, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, message + "\n");
        GitOps.Run(_repo, "add", "-A");
        GitOps.Run(_repo, "commit", "-q", "-m", message);
    }

    [Fact]
    public void Subjects_become_note_lines_without_the_noise()
    {
        var note = GitOps.NoteFromSubjects(new[]
        {
            "Fix the crash on load", "fixup! Fix the crash on load", "WIP trying things", "Merge branch 'x'",
            "  Add a settings page  ", "fix the crash on load", "",
        });
        Assert.Equal("- Fix the crash on load\n- Add a settings page", note);
    }

    [Fact]
    public void Commits_since_the_upload_tag_that_touched_the_mod()
    {
        if (!HasGit()) return;
        Init();
        Commit("Alpha/a.txt", "First version");
        GitOps.Run(_repo, "tag", "-a", GitOps.TagName("Alpha", "1.0.0"), "-m", "uploaded");
        Commit("Alpha/a.txt", "Fix the crash on load");
        Commit("Beta/b.txt", "Beta only change");
        Commit("Alpha/b.txt", "Add a settings page");

        var n = GitOps.DraftNote(_repo, "Alpha", "Alpha", "1.0.0", DateTimeOffset.UtcNow.AddDays(-30));

        Assert.NotNull(n);
        Assert.Equal("- Add a settings page\n- Fix the crash on load", n!.Note);
        Assert.Equal(2, n.Count);
        Assert.Contains("Alpha-v1.0.0 tag", n.Source);
    }

    [Fact]
    public void Without_a_tag_it_uses_the_last_upload_time()
    {
        if (!HasGit()) return;
        Init();
        Commit("Alpha/a.txt", "Old change");

        var future = GitOps.DraftNote(_repo, "Alpha", "Alpha", "1.0.0", DateTimeOffset.UtcNow.AddMinutes(5));
        var past = GitOps.DraftNote(_repo, "Alpha", "Alpha", "1.0.0", DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Null(future); // nothing committed since the upload
        Assert.Equal("- Old change", past!.Note);
        Assert.Contains("last upload", past.Source);
    }

    [Fact]
    public void With_no_upload_to_start_from_it_takes_the_newest_commits()
    {
        if (!HasGit()) return;
        Init();
        for (var i = 1; i <= 12; i++) Commit("Alpha/a.txt", $"Change {i}");

        var n = GitOps.DraftNote(_repo, "Alpha", "Alpha", null, null);

        Assert.Equal(10, n!.Count);
        Assert.StartsWith("- Change 12\n", n.Note);
    }

    [Fact]
    public void Not_a_git_repo_gives_nothing()
    {
        Directory.CreateDirectory(_repo);
        Assert.Null(GitOps.DraftNote(_repo, "Alpha", "Alpha", "1.0.0", null));
    }
}
