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

    /// <summary>Tags HEAD. Returns null on success, "exists" when the tag is already there, else git's message.</summary>
    public static string? Tag(string repo, string name, string message)
    {
        if (Run(repo, "rev-parse", "-q", "--verify", "refs/tags/" + name).code == 0) return "exists";
        var r = Run(repo, "tag", "-a", name, "-m", message);
        return r.code == 0 ? null : r.output;
    }
}
