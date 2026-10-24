using System.Text;
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
    /// <param name="strict">Treat the About.xml warnings (see <see cref="DropPod.RimWorld.AboutLint"/>) as problems that block the upload.</param>
    public static (List<string> Problems, List<string> Warnings) Check(string modDir, bool strict = false)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        XDocument? about;
        try { about = ReadAbout(modDir); }
        catch { return (problems, warnings); } // the scan already reports an unreadable About.xml
        if (about?.Root is not { } meta) return (problems, warnings);

        var supported = meta.Element("supportedVersions")?.Elements("li").Select(e => Short(e.Value)).Where(v => v.Length > 0).Distinct().ToList() ?? new();
        // About.xml itself, read by the parser the Drop Pod uses too.
        if (DropPod.RimWorld.RimWorldAbout.ReadFolder(modDir, DropPod.ModSource.Local) is { } manifest)
            foreach (var f in DropPod.RimWorld.AboutLint.Check(manifest))
                (f.Level == DropPod.RimWorld.LintLevel.Block || strict ? problems : warnings).Add(f.Message);

        var folders = Directory.GetDirectories(modDir).Select(Path.GetFileName).Where(n => VersionFolder.IsMatch(n!)).Select(n => n!).ToList();
        var loadFolders = Path.Combine(modDir, "loadFolders.xml");
        if (File.Exists(loadFolders)) CheckLoadFolders(modDir, loadFolders, supported, problems, warnings);
        else
            foreach (var f in folders.Where(f => !supported.Contains(f)))
                warnings.Add($"has a {f} folder, but About.xml does not list {f} in supportedVersions, so RimWorld {f} never loads it");

        foreach (var (file, error) in BrokenXml(modDir))
            problems.Add($"{Path.GetRelativePath(modDir, file).Replace('\\', '/')} is not valid XML ({error}): every player would get red errors");
        warnings.AddRange(TranslationGaps(modDir));
        warnings.AddRange(HarmonyWarnings(modDir, meta));
        warnings.AddRange(RimWorldXmlLint.Check(modDir, supported));
        return (problems, warnings);
    }

    // ------------------------------------------------------------ translations

    /// <summary>Languages folders RimWorld reads: the mod's own and those in its version folders.</summary>
    static IEnumerable<string> LanguagesFolders(string modDir)
    {
        var root = Path.Combine(modDir, "Languages");
        if (Directory.Exists(root)) yield return root;
        string[] dirs;
        try { dirs = Directory.GetDirectories(modDir); } catch { yield break; }
        foreach (var d in dirs.Where(d => VersionFolder.IsMatch(Path.GetFileName(d)) || Path.GetFileName(d).Equals("Common", StringComparison.OrdinalIgnoreCase)).OrderBy(d => d))
        {
            var langs = Path.Combine(d, "Languages");
            if (Directory.Exists(langs)) yield return langs;
        }
    }

    /// <summary>The keys of every Keyed file in <paramref name="keyed"/> (the element names under LanguageData).</summary>
    public static HashSet<string> KeyedKeys(string keyed)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(keyed)) return keys;
        foreach (var file in Directory.GetFiles(keyed, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                if (XDocument.Load(file).Root is { } root)
                    foreach (var e in root.Elements()) keys.Add(e.Name.LocalName);
            }
            catch (XmlException) { } // reported with the other broken XML
            catch (IOException) { }
        }
        return keys;
    }

    /// <summary>
    /// For each language with a Keyed folder: English keys it doesn't have (players see the raw key, such as
    /// "MyMod_Setting") and keys English no longer has (leftovers from renamed texts).
    /// </summary>
    public static List<string> TranslationGaps(string modDir)
    {
        var w = new List<string>();
        foreach (var languages in LanguagesFolders(modDir))
        {
            var englishDir = Directory.GetDirectories(languages).FirstOrDefault(d => Path.GetFileName(d).Equals("English", StringComparison.OrdinalIgnoreCase));
            if (englishDir == null) continue;
            var english = KeyedKeys(Path.Combine(englishDir, "Keyed"));
            if (english.Count == 0) continue;
            var where = Path.GetRelativePath(modDir, languages).Replace('\\', '/');
            foreach (var lang in Directory.GetDirectories(languages).Where(d => d != englishDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var keyedDir = Path.Combine(lang, "Keyed");
                if (!Directory.Exists(keyedDir)) continue;
                var have = KeyedKeys(keyedDir);
                var name = Path.GetFileName(lang);
                var missing = english.Where(k => !have.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var extra = have.Where(k => !english.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (missing.Count > 0)
                    w.Add($"{where}/{name} is missing {missing.Count} of {english.Count} English Keyed texts, so players see the raw key: {PackageCheck.Examples(missing)}");
                if (extra.Count > 0)
                    w.Add($"{where}/{name} has {extra.Count} Keyed text{(extra.Count == 1 ? "" : "s")} English no longer has (left over from a rename?): {PackageCheck.Examples(extra)}");
            }
        }
        return w;
    }

    // ------------------------------------------------------------ Harmony

    /// <summary>The Harmony mod's packageId, which a mod that uses Harmony should list in modDependencies.</summary>
    public const string HarmonyId = "brrainz.harmony";

    static readonly byte[] HarmonyRef = Encoding.ASCII.GetBytes("0Harmony");
    static readonly Dictionary<string, (DateTime Time, long Size, bool Uses)> UsesCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the assembly references Harmony (its metadata names the 0Harmony assembly).</summary>
    public static bool UsesHarmony(string dll)
    {
        FileInfo info;
        try { info = new FileInfo(dll); } catch { return false; }
        lock (UsesCache)
            if (UsesCache.TryGetValue(dll, out var hit) && hit.Time == info.LastWriteTimeUtc && hit.Size == info.Length) return hit.Uses;
        bool uses;
        try { uses = File.ReadAllBytes(dll).AsSpan().IndexOf(HarmonyRef) >= 0; }
        catch (IOException) { return false; }
        lock (UsesCache) UsesCache[dll] = (info.LastWriteTimeUtc, info.Length, uses);
        return uses;
    }

    /// <summary>
    /// A mod that ships its own 0Harmony.dll (two copies of Harmony break other mods; depend on the Harmony mod
    /// instead), or whose code uses Harmony without listing it in modDependencies.
    /// </summary>
    public static List<string> HarmonyWarnings(string modDir, XElement meta)
    {
        var w = new List<string>();
        var assemblies = new List<string>();
        var rootAsm = Path.Combine(modDir, "Assemblies");
        if (Directory.Exists(rootAsm)) assemblies.Add(rootAsm);
        try
        {
            foreach (var d in Directory.GetDirectories(modDir).Where(d => VersionFolder.IsMatch(Path.GetFileName(d)) || Path.GetFileName(d).Equals("Common", StringComparison.OrdinalIgnoreCase)))
                if (Directory.Exists(Path.Combine(d, "Assemblies"))) assemblies.Add(Path.Combine(d, "Assemblies"));
        }
        catch (IOException) { }
        var shipped = new List<string>();
        var uses = false;
        foreach (var dir in assemblies)
            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
            {
                if (Path.GetFileName(dll).Equals("0Harmony.dll", StringComparison.OrdinalIgnoreCase)) shipped.Add(Path.GetRelativePath(modDir, dll).Replace('\\', '/'));
                else if (!uses && UsesHarmony(dll)) uses = true;
            }
        if (shipped.Count > 0)
            w.Add($"ships its own {PackageCheck.Examples(shipped)}: two copies of Harmony break other mods, so list {HarmonyId} in modDependencies and leave the dll out (in your .csproj, set Private to false on the Lib.Harmony reference)");
        var deps = meta.Element("modDependencies")?.Elements("li").Select(li => li.Element("packageId")?.Value.Trim() ?? "") ?? Enumerable.Empty<string>();
        if ((uses || shipped.Count > 0) && !deps.Any(d => d.Equals(HarmonyId, StringComparison.OrdinalIgnoreCase)))
            w.Add($"uses Harmony, but About.xml's modDependencies doesn't list {HarmonyId}, so players without the Harmony mod get errors");
        return w;
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
