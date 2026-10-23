using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SteamCmdHelper.DropPod.RimWorld;

/// <summary>
/// Which of a mod's folders RimWorld reads for a game version, highest priority first: the entries of loadFolders.xml
/// for that version (with their IfModActive / IfModNotActive conditions), or without that file the version's own
/// folder (or the newest older one), Common and the mod's root.
/// </summary>
public static class LoadFolders
{
    static readonly Regex VersionName = new(@"^\d+\.\d+$");

    /// <param name="isActive">Whether a mod (by packageId, any case) is active, for loadFolders.xml conditions; null treats every condition as met.</param>
    public static List<string> For(string modFolder, string? gameVersion, Func<string, bool>? isActive = null)
    {
        var version = gameVersion == null ? null : GameVersion.Short(gameVersion);
        var file = Path.Combine(modFolder, "loadFolders.xml");
        if (File.Exists(file))
        {
            try
            {
                var root = XDocument.Load(file).Root;
                var entry = root?.Elements().FirstOrDefault(e => version != null && e.Name.LocalName.Equals("v" + version, StringComparison.OrdinalIgnoreCase))
                            ?? root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("default", StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    var list = new List<string>();
                    foreach (var li in entry.Elements("li"))
                    {
                        if (!Allowed(li, isActive)) continue;
                        var rel = li.Value.Trim().Replace('\\', '/').Trim('/');
                        var dir = rel.Length == 0 ? modFolder : Path.Combine(modFolder, rel);
                        if (Directory.Exists(dir)) list.Add(dir);
                    }
                    // Later entries win in loadFolders.xml, so the highest priority is the last one.
                    list.Reverse();
                    return list.Distinct().ToList();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
        }

        var folders = new List<string>();
        if (version != null && BestVersionFolder(modFolder, version) is { } v) folders.Add(v);
        if (Directory.Exists(Path.Combine(modFolder, "Common"))) folders.Add(Path.Combine(modFolder, "Common"));
        folders.Add(modFolder);
        return folders;
    }

    /// <summary>The version's own folder, else the newest version folder older than it.</summary>
    static string? BestVersionFolder(string modFolder, string version)
    {
        string[] dirs;
        try { dirs = Directory.GetDirectories(modFolder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        var want = Version.TryParse(version, out var w) ? w : null;
        return dirs.Select(d => (Dir: d, Name: Path.GetFileName(d)))
            .Where(x => VersionName.IsMatch(x.Name) && Version.TryParse(x.Name, out var v) && (want == null ? x.Name == version : v <= want))
            .OrderByDescending(x => Version.Parse(x.Name))
            .Select(x => x.Dir)
            .FirstOrDefault();
    }

    static bool Allowed(XElement li, Func<string, bool>? isActive)
    {
        if (isActive == null) return true;
        static IEnumerable<string> Ids(XAttribute? a) => a?.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0) ?? Enumerable.Empty<string>();
        var any = Ids(li.Attribute("IfModActive")).ToList();
        if (any.Count > 0 && !any.Any(isActive)) return false;
        var all = Ids(li.Attribute("IfModActiveAll")).ToList();
        if (all.Count > 0 && !all.All(isActive)) return false;
        if (Ids(li.Attribute("IfModNotActive")).Any(isActive)) return false;
        return true;
    }

    /// <summary>
    /// The XML files under <paramref name="sub"/> ("Defs" or "Patches") in the load folders. A file at the same relative
    /// path in a higher priority folder replaces the lower one, as in the game. Sorted by relative path.
    /// </summary>
    public static List<string> Files(IReadOnlyList<string> loadFolders, string sub)
    {
        var byRel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in loadFolders)
        {
            var dir = Path.Combine(folder, sub);
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.xml", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 16 }).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files) byRel.TryAdd(Path.GetRelativePath(dir, f).Replace('\\', '/'), f);
        }
        return byRel.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Value).ToList();
    }
}
