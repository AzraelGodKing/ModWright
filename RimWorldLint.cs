using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SteamCmdHelper;

/// <summary>
/// RimWorld-specific checks before upload, for mistakes that only show up as red errors once players load the mod: a
/// missing or repeated packageId, an XML file that does not parse, loadFolders.xml pointing at folders that are not
/// there, and version folders that About.xml does not list (or the other way round).
/// </summary>
public static class RimWorldLint
{
    /// <summary>The checks run for the RimWorld layout and any layout for RimWorld's app id.</summary>
    public static bool Applies(ProjectConfig cfg) => cfg.AppId == "294100" && cfg.ModMarker.Replace('\\', '/').Equals("About/About.xml", StringComparison.OrdinalIgnoreCase);

    // RimWorld's own rule (ModMetaData.PackageIdIsValid): letters, digits and dots, with at least one dot inside.
    static readonly Regex PackageId = new(@"^[A-Za-z0-9]+(\.[A-Za-z0-9]+)+$");
    static readonly Regex VersionFolder = new(@"^\d+\.\d+$");

    /// <summary>Folders RimWorld never reads and the RimWorld layout leaves out of the package.</summary>
    static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", ".idea", "obj", "bin", "Source", "Tools", "node_modules" };

    /// <summary>Folders whose XML RimWorld parses (anywhere in the mod, including inside version folders).</summary>
    static readonly HashSet<string> XmlFolders = new(StringComparer.OrdinalIgnoreCase) { "Defs", "Patches", "Languages", "About" };

    /// <summary>The packageId in About.xml, or null when there is none.</summary>
    public static string? PackageIdOf(string modDir)
    {
        try { return ReadAbout(modDir)?.Root?.Element("packageId")?.Value.Trim() is { Length: > 0 } id ? id : null; }
        catch { return null; }
    }

    /// <summary>What would block the upload (Problems) and what is only worth a look (Warnings) for one mod.</summary>
    public static (List<string> Problems, List<string> Warnings) Check(string modDir)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        XDocument? about;
        try { about = ReadAbout(modDir); }
        catch { return (problems, warnings); } // the scan already reports an unreadable About.xml
        if (about?.Root is not { } meta) return (problems, warnings);

        var supported = meta.Element("supportedVersions")?.Elements("li").Select(e => Short(e.Value)).Where(v => v.Length > 0).Distinct().ToList() ?? new();
        var packageId = meta.Element("packageId")?.Value.Trim() ?? "";
        // Only RimWorld 1.0 loaded mods without one.
        if (packageId.Length == 0 && supported.All(v => v == "1.0") && supported.Count > 0)
            warnings.Add("About.xml has no <packageId>; RimWorld 1.1 and later need one (such as YourName.ModName)");
        else if (packageId.Length == 0)
            problems.Add("About.xml has no <packageId> (such as YourName.ModName): RimWorld refuses to load a mod without one");
        else if (!PackageId.IsMatch(packageId))
            warnings.Add($"packageId \"{packageId}\" is not valid for RimWorld: use letters and digits with a dot, such as YourName.ModName");

        foreach (var li in meta.Element("modDependencies")?.Elements("li") ?? Enumerable.Empty<XElement>())
            if (string.IsNullOrWhiteSpace(li.Element("packageId")?.Value))
                warnings.Add("a modDependencies entry" + (li.Element("displayName")?.Value is { Length: > 0 } n ? $" ({n.Trim()})" : "") + " has no packageId, so RimWorld can't check it");

        var folders = Directory.GetDirectories(modDir).Select(Path.GetFileName).Where(n => VersionFolder.IsMatch(n!)).Select(n => n!).ToList();
        var loadFolders = Path.Combine(modDir, "loadFolders.xml");
        if (File.Exists(loadFolders)) CheckLoadFolders(modDir, loadFolders, supported, problems, warnings);
        else
            foreach (var f in folders.Where(f => !supported.Contains(f)))
                warnings.Add($"has a {f} folder, but About.xml does not list {f} in supportedVersions, so RimWorld {f} never loads it");

        foreach (var (file, error) in BrokenXml(modDir))
            problems.Add($"{Path.GetRelativePath(modDir, file).Replace('\\', '/')} is not valid XML ({error}): every player would get red errors");
        return (problems, warnings);
    }

    static void CheckLoadFolders(string modDir, string file, List<string> supported, List<string> problems, List<string> warnings)
    {
        XDocument doc;
        try { doc = XDocument.Load(file); }
        catch (XmlException) { return; } // reported with the other broken XML
        if (doc.Root == null) return;
        var listed = new List<string>();
        foreach (var version in doc.Root.Elements())
        {
            // Entries are named v1.6 (or "default"); each <li> is a folder, relative to the mod, "/" for the mod itself.
            var v = version.Name.LocalName.TrimStart('v', 'V');
            listed.Add(Short(v));
            foreach (var li in version.Elements("li"))
            {
                var rel = li.Value.Trim().Trim('/', '\\');
                if (rel.Length == 0) continue;
                if (!Directory.Exists(Path.Combine(modDir, ProjectConfig.Norm(rel))))
                    warnings.Add($"loadFolders.xml lists \"{rel}\" for {version.Name.LocalName}, but there is no such folder");
            }
        }
        foreach (var v in supported.Where(v => !listed.Contains(v)))
            warnings.Add($"About.xml lists {v}, but loadFolders.xml has no v{v} entry, so RimWorld {v} loads only the mod's root folders");
    }

    /// <summary>"1.6.4104" becomes "1.6"; anything else is left out.</summary>
    static string Short(string v) => Regex.Match(v.Trim(), @"^(\d+\.\d+)") is { Success: true } m ? m.Groups[1].Value : "";

    static XDocument? ReadAbout(string modDir)
    {
        var path = Path.Combine(modDir, "About", "About.xml");
        return File.Exists(path) ? XDocument.Load(path) : null;
    }

    // Parse results by file, so a refresh only reads XML files that changed since the last one.
    static readonly Dictionary<string, (DateTime Time, long Size, string? Error)> Parsed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every XML file RimWorld reads (in Defs, Patches, Languages and About folders) that does not parse, with why.</summary>
    public static List<(string File, string Error)> BrokenXml(string modDir)
    {
        var broken = new List<(string, string)>();
        foreach (var file in XmlFiles(modDir, false))
        {
            FileInfo info;
            try { info = new FileInfo(file); } catch { continue; }
            string? error;
            lock (Parsed)
                if (Parsed.TryGetValue(file, out var hit) && hit.Time == info.LastWriteTimeUtc && hit.Size == info.Length)
                {
                    if (hit.Error != null) broken.Add((file, hit.Error));
                    continue;
                }
            try
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                while (reader.Read()) { }
                error = null;
            }
            catch (XmlException e) { error = $"line {e.LineNumber}: {e.Message.Split(" Line ")[0].TrimEnd('.', ',')}"; }
            catch (IOException) { continue; }
            lock (Parsed) Parsed[file] = (info.LastWriteTimeUtc, info.Length, error);
            if (error != null) broken.Add((file, error));
        }
        return broken;
    }

    static IEnumerable<string> XmlFiles(string dir, bool inXmlFolder)
    {
        string[] files, dirs;
        try { files = Directory.GetFiles(dir, "*.xml"); dirs = Directory.GetDirectories(dir); }
        catch { yield break; }
        if (inXmlFolder) foreach (var f in files) yield return f;
        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith('.') || Skipped.Contains(name)) continue;
            foreach (var f in XmlFiles(d, inXmlFolder || XmlFolders.Contains(name))) yield return f;
        }
    }

    /// <summary>One warning per mod whose packageId another mod in the folder also uses (RimWorld loads only one of them).</summary>
    public static Dictionary<ModInfo, string> DuplicatePackageIds(IEnumerable<(ModInfo Mod, string? PackageId)> mods)
    {
        var result = new Dictionary<ModInfo, string>();
        foreach (var g in mods.Where(x => x.PackageId != null).GroupBy(x => x.PackageId!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var (m, id) in g)
                result[m] = $"packageId {id} is also used by {string.Join(", ", g.Where(o => o.Mod != m).Select(o => o.Mod.Folder))}: RimWorld loads only one mod per packageId";
        return result;
    }
}
