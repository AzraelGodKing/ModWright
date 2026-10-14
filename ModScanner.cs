using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace SteamCmdHelper;

public sealed class ModInfo
{
    public string Dir = "";
    public string Folder = "";
    public string Name = "";
    public string Version = "";
    public string? WorkshopId;
    public readonly List<string> Problems = new();
    public readonly List<string> Warnings = new();

    public bool OnSteam => WorkshopId != null;
    public bool Ready => Problems.Count == 0;

    public string StatusText =>
        Problems.Count > 0 ? "Fix: " + string.Join("; ", Problems)
        : Warnings.Count > 0 ? "Ready (" + string.Join("; ", Warnings) + ")"
        : OnSteam ? "Ready" : "Ready to create (private)";
}

/// <summary>Reads each mod folder using the repo's <see cref="ProjectConfig"/> so problems show before uploading.</summary>
public static class ModScanner
{
    public static string? GitBranch(string repo)
    {
        var o = Git(repo, "rev-parse --abbrev-ref HEAD");
        return string.IsNullOrWhiteSpace(o) ? null : o.Trim();
    }

    public static bool IsGitRepo(string repo) => !string.IsNullOrWhiteSpace(Git(repo, "rev-parse --is-inside-work-tree"));

    public static string? ReadXml(string file, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return null;
        var doc = XDocument.Load(file);
        var el = doc.XPathSelectElement("/" + xpath.TrimStart('/'));
        var v = el?.Value.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>The changelog split into (version line or null, release notes).</summary>
    public static (string? version, string notes) ReadChangelog(string file, ProjectConfig cfg)
    {
        var lines = File.ReadAllLines(file);
        if (!cfg.ChangelogFirstLineIsVersion) return (null, string.Join("\n", lines).Trim());
        return (lines.FirstOrDefault()?.Trim() ?? "", string.Join("\n", lines.Skip(1)).Trim());
    }

    public static List<ModInfo> Scan(string repo, ProjectConfig cfg)
    {
        var list = new List<ModInfo>();
        if (!Directory.Exists(repo)) return list;

        var dirtyLines = Git(repo, "status --porcelain")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length > 3 && !l.StartsWith("??"))
            .Select(l => l.Substring(3).Trim().Trim('"').Replace('\\', '/'))
            .ToList();

        var marker = ProjectConfig.Norm(cfg.ModMarker);
        foreach (var dir in Directory.GetDirectories(repo).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(dir);
            if (folder.StartsWith('.')) continue;
            var markerFile = Path.Combine(dir, marker);
            if (!File.Exists(markerFile)) continue;

            var m = new ModInfo { Dir = dir, Folder = folder, Name = folder };
            if (markerFile.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var name = ReadXml(markerFile, cfg.NameXPath);
                    if (name != null) m.Name = name;
                    m.Version = ReadXml(markerFile, cfg.VersionXPath) ?? "";
                }
                catch (Exception e) { m.Problems.Add(cfg.ModMarker + " unreadable (" + e.Message + ")"); }
            }

            var idFile = Path.Combine(dir, ProjectConfig.Norm(cfg.WorkshopIdFile));
            if (File.Exists(idFile))
            {
                var id = File.ReadAllText(idFile).Trim();
                if (Regex.IsMatch(id, @"^\d+$")) m.WorkshopId = id;
                else m.Problems.Add(cfg.WorkshopIdFile + " is not a number");
            }

            var clPath = Path.Combine(dir, ProjectConfig.Norm(cfg.Changelog));
            if (!File.Exists(clPath)) m.Problems.Add("no " + cfg.Changelog);
            else
            {
                var (clVersion, notes) = ReadChangelog(clPath, cfg);
                if (clVersion != null)
                {
                    if (!Regex.IsMatch(clVersion, @"^\d+\.\d+\.\d+")) m.Problems.Add("changelog line 1 is not a version");
                    else if (m.Version.Length == 0) m.Version = clVersion;
                    else if (clVersion != m.Version) m.Problems.Add($"changelog {clVersion} vs {m.Version} in {cfg.ModMarker}");
                }
                if (notes.Length < 10) m.Problems.Add("changelog has no notes");
            }

            if (!m.OnSteam && !File.Exists(Path.Combine(dir, ProjectConfig.Norm(cfg.Preview))))
                m.Problems.Add("needs " + cfg.Preview + " to create on Steam");

            foreach (var glob in cfg.BuiltFiles ?? Array.Empty<string>())
                if (MatchGlob(dir, glob).Count == 0) m.Problems.Add($"not built (nothing matches {glob})");

            if (dirtyLines.Any(p => p.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)))
                m.Warnings.Add("uncommitted changes will NOT be uploaded");

            list.Add(m);
        }
        return list;
    }

    /// <summary>Files inside <paramref name="dir"/> matching a simple glob such as "Assemblies/*.dll" (wildcards in the last segment only).</summary>
    public static List<string> MatchGlob(string dir, string glob)
    {
        var g = ProjectConfig.Norm(glob);
        var sub = Path.GetDirectoryName(g) ?? "";
        var pattern = Path.GetFileName(g);
        var folder = Path.Combine(dir, sub);
        return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).ToList() : new List<string>();
    }

    public static string Git(string repo, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return o;
        }
        catch { return ""; }
    }
}
