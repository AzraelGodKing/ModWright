using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SteamCmdHelper;

public sealed class ModInfo
{
    public string Dir = "";
    public string Folder = "";
    public string Name = "";
    public string Version = "";
    public string? WorkshopId;
    public bool DllPresent;
    public readonly List<string> Problems = new();
    public readonly List<string> Warnings = new();

    public bool OnSteam => WorkshopId != null;
    public bool Ready => Problems.Count == 0;

    public string StatusText =>
        Problems.Count > 0 ? "Fix: " + string.Join("; ", Problems)
        : Warnings.Count > 0 ? "Ready (" + string.Join("; ", Warnings) + ")"
        : OnSteam ? "Ready" : "Ready to create (private)";
}

/// <summary>Reads each mod folder the same way scripts/steam_publish.ps1 does, so problems show before uploading.</summary>
public static class ModScanner
{
    public static string? GitBranch(string repo)
    {
        var o = Git(repo, "rev-parse --abbrev-ref HEAD");
        return string.IsNullOrWhiteSpace(o) ? null : o.Trim();
    }

    public static List<ModInfo> Scan(string repo)
    {
        var list = new List<ModInfo>();
        if (!Directory.Exists(repo)) return list;

        var dirtyLines = Git(repo, "status --porcelain")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length > 3 && !l.StartsWith("??"))
            .Select(l => l.Substring(3).Trim().Trim('"').Replace('\\', '/'))
            .ToList();

        foreach (var dir in Directory.GetDirectories(repo).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(dir);
            if (folder.StartsWith('.')) continue;
            var aboutXml = Path.Combine(dir, "About", "About.xml");
            if (!File.Exists(aboutXml)) continue;

            var m = new ModInfo { Dir = dir, Folder = folder, Name = folder };
            try
            {
                var root = XDocument.Load(aboutXml).Root;
                var name = ((string?)root?.Element("name"))?.Trim();
                if (!string.IsNullOrEmpty(name)) m.Name = name;
                m.Version = ((string?)root?.Element("modVersion"))?.Trim() ?? "";
            }
            catch (Exception e) { m.Problems.Add("About.xml unreadable (" + e.Message + ")"); }

            var idFile = Path.Combine(dir, "About", "PublishedFileId.txt");
            if (File.Exists(idFile))
            {
                var id = File.ReadAllText(idFile).Trim();
                if (Regex.IsMatch(id, @"^\d+$")) m.WorkshopId = id;
                else m.Problems.Add("PublishedFileId.txt is not a number");
            }

            var cl = Path.Combine(dir, "About", "changelog.txt");
            if (!File.Exists(cl)) m.Problems.Add("no About\\changelog.txt");
            else
            {
                var lines = File.ReadAllLines(cl);
                var first = lines.FirstOrDefault()?.Trim() ?? "";
                var notes = string.Join("\n", lines.Skip(1)).Trim();
                if (!Regex.IsMatch(first, @"^\d+\.\d+\.\d+")) m.Problems.Add("changelog line 1 is not a version");
                else if (m.Version.Length > 0 && first != m.Version)
                    m.Problems.Add($"changelog {first} vs About.xml {m.Version}");
                if (notes.Length < 10) m.Problems.Add("changelog has no notes");
            }

            if (!m.OnSteam && !File.Exists(Path.Combine(dir, "About", "Preview.png")))
                m.Problems.Add("needs About\\Preview.png to create on Steam");

            var asm = Path.Combine(dir, "Assemblies");
            m.DllPresent = Directory.Exists(asm) && Directory.GetFiles(asm, "*.dll").Length > 0;
            if (!m.DllPresent) m.Problems.Add("not built (no DLL)");

            if (dirtyLines.Any(p => p.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)))
                m.Warnings.Add("uncommitted changes will NOT be uploaded");

            list.Add(m);
        }
        return list;
    }

    static string Git(string repo, string args)
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
