using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// The few git steps around an upload: committing a mod's changes first (git packaging uploads HEAD), committing the
/// Workshop id file a new item got, and tagging what was uploaded. Each runs git with explicit paths, so nothing else
/// in the repo is touched.
/// </summary>
public static class GitOps
{
    /// <summary>Runs git in <paramref name="repo"/> and returns its exit code and combined output.</summary>
    public static (int code, string output) Run(string repo, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            var o = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(60000)) { try { p.Kill(true); } catch { } return (-1, "git did not finish within a minute."); }
            return (p.ExitCode, (o + err.Result).Trim());
        }
        catch (Exception e) { return (-1, "Could not run git: " + e.Message); }
    }

    /// <summary>
    /// Stages everything under the given paths (new, changed and deleted files; ignored files stay out) and commits
    /// only those paths. Returns null on success, else git's message. Nothing to commit counts as success.
    /// </summary>
    public static string? CommitPaths(string repo, IReadOnlyCollection<string> relPaths, string message)
    {
        if (relPaths.Count == 0) return null;
        var add = Run(repo, new[] { "add", "-A", "--" }.Concat(relPaths).ToArray());
        if (add.code != 0) return add.output;
        var staged = Run(repo, new[] { "diff", "--cached", "--quiet", "--" }.Concat(relPaths).ToArray());
        if (staged.code == 0) return null; // nothing changed under those paths
        var commit = Run(repo, new[] { "commit", "-m", message, "--" }.Concat(relPaths).ToArray());
        return commit.code == 0 ? null : commit.output;
    }

    static readonly Regex BadRefChars = new(@"[\s~^:?*\[\\]+|\.{2,}|@\{");

    /// <summary>A valid tag name for a mod's upload, e.g. "BetterStorage-v1.4.0".</summary>
    public static string TagName(string folder, string version)
    {
        var name = Regex.Replace(BadRefChars.Replace($"{folder}-v{version}", "-"), "-{2,}", "-").Trim('-', '.', '/');
        if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        return name.Length == 0 ? "upload" : name;
    }

    /// <summary>A change note drafted from commit subjects, and what it covers ("commits since the last upload on ...").</summary>
    public sealed record CommitNote(string Note, string Source, int Count);

    /// <summary>Most commits a drafted note lists; a longer history is cut to the newest.</summary>
    public const int MaxCommits = 30;

    static readonly Regex Noise = new(@"^(fixup!|squash!|amend!|wip\b|merge\b)", RegexOptions.IgnoreCase);

    /// <summary>
    /// The note lines for commit <paramref name="subjects"/> (newest first): one "- subject" line each, leaving out
    /// fixup/squash/WIP/merge commits and repeats.
    /// </summary>
    public static string NoteFromSubjects(IEnumerable<string> subjects) =>
        string.Join("\n", subjects.Select(s => s.Trim()).Where(s => s.Length > 0 && !Noise.IsMatch(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(s => "- " + s));

    /// <summary>
    /// Drafts a change note for a mod from the commits that touched its folder since it was last uploaded: since its
    /// upload tag (<see cref="TagName"/> of <paramref name="lastVersion"/>) when that exists, else since
    /// <paramref name="lastUpload"/>, else its newest few commits. Null when git cannot be read or nothing qualifies.
    /// </summary>
    public static CommitNote? DraftNote(string repo, string relPath, string folder, string? lastVersion, DateTimeOffset? lastUpload)
    {
        var args = new List<string> { "log", "--no-merges", "--format=%s", "-n", MaxCommits.ToString() };
        string source;
        var tag = lastVersion is { Length: > 0 } ? TagName(folder, lastVersion) : null;
        if (tag != null && Run(repo, "rev-parse", "-q", "--verify", "refs/tags/" + tag).code == 0)
        {
            args.Add(tag + "..HEAD");
            source = $"commits since the {tag} tag";
        }
        else if (lastUpload is { } t)
        {
            args.Add("--since=" + t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            source = $"commits since the last upload from this PC ({t.ToLocalTime():d MMM yyyy})";
        }
        else
        {
            args[4] = "10";
            source = "the newest commits (no upload from this PC to start from)";
        }
        args.Add("--");
        args.Add(relPath.Length > 0 ? relPath : ".");
        var (code, output) = Run(repo, args.ToArray());
        if (code != 0) return null;
        var subjects = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var note = NoteFromSubjects(subjects);
        return note.Length == 0 ? null : new CommitNote(note, source, note.Split('\n').Length);
    }

    /// <summary>Tags <paramref name="commit"/> (HEAD by default). Returns null on success, "exists" when the tag is already there, else git's message.</summary>
    public static string? Tag(string repo, string name, string message, string commit = "HEAD")
    {
        if (Run(repo, "rev-parse", "-q", "--verify", "refs/tags/" + name).code == 0) return "exists";
        var r = Run(repo, "tag", "-a", name, "-m", message, commit);
        return r.code == 0 ? null : r.output;
    }

    /// <summary>True when <paramref name="gitRef"/> names a commit in the repo (a branch, tag or commit id).</summary>
    public static bool RefExists(string repo, string gitRef) =>
        gitRef.Length > 0 && !gitRef.StartsWith('-') && Run(repo, "rev-parse", "-q", "--verify", gitRef + "^{commit}").code == 0;

    /// <summary>
    /// The commit a past upload was made from: its upload tag (<see cref="TagName"/>) when that exists, else the newest
    /// commit at the upload's time. Null when git has neither. The second value says which, for people.
    /// </summary>
    public static (string Ref, string Source)? FindUpload(string repo, string folder, string version, DateTimeOffset time)
    {
        var tag = TagName(folder, version);
        if (RefExists(repo, "refs/tags/" + tag)) return ("refs/tags/" + tag, $"its upload tag {tag}");
        var (code, output) = Run(repo, "rev-list", "-1", "--before=" + time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), "HEAD");
        var commit = output.Trim();
        return code == 0 && commit.Length >= 7 ? (commit, $"the newest commit on {time.ToLocalTime():d MMM yyyy HH:mm} ({commit[..7]}), as it has no upload tag") : null;
    }

    /// <summary>
    /// The change note for re-uploading an old version: the old note marked as a roll-back. A note that starts with its
    /// version (Add Changenote style) keeps the version first, so the Steam version column still reads it.
    /// </summary>
    public static string RollbackNote(string version, string oldNote)
    {
        var note = oldNote.Replace("\r\n", "\n").Trim();
        if (version.Length > 0 && note.StartsWith(version))
        {
            var firstLine = note.Split('\n')[0];
            return $"{firstLine} (rolled back to this version)" + note[firstLine.Length..];
        }
        return $"Rolled back to version {version}." + (note.Length > 0 ? "\n\n" + note : "");
    }
}
