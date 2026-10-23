using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml;
using System.Xml.Linq;
using SteamCmdHelper.DropPod.RimWorld;

namespace SteamCmdHelper;

/// <summary>
/// XML checks for a RimWorld mod before upload that need no game: a def defined twice in the mod (the second silently
/// replaces the first), a texPath with no texture where the mod keeps its own textures, and English Keyed texts that
/// nothing in the mod's code or XML mentions. Checked with the folders the newest supported version loads.
/// </summary>
public static class RimWorldXmlLint
{
    static readonly string[] TextureExtensions = { ".png", ".dds", ".jpg", ".jpeg" };
    static readonly string[] Facings = { "_north", "_south", "_east", "_west" };

    public static List<string> Check(string modDir, IReadOnlyList<string> supported)
    {
        var w = new List<string>();
        var version = supported.Where(v => Version.TryParse(v, out _)).OrderByDescending(Version.Parse).FirstOrDefault();
        // Folders behind IfModActive are compatibility extras; checking them as always loaded would invent duplicates.
        var folders = LoadFolders.For(modDir, version, _ => false);
        var defs = new List<(string Type, string Name, string File)>();
        var texPaths = new List<(string Path, string Def, string File)>();
        foreach (var file in LoadFolders.Files(folders, "Defs"))
        {
            XDocument doc;
            try { doc = XDocument.Load(file); }
            catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException) { continue; } // broken XML is its own check
            var rel = Path.GetRelativePath(modDir, file).Replace('\\', '/');
            foreach (var def in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var name = def.Element("defName")?.Value.Trim();
                if (string.IsNullOrEmpty(name) || string.Equals((string?)def.Attribute("Abstract"), "true", StringComparison.OrdinalIgnoreCase)) continue;
                defs.Add((def.Name.LocalName, name, rel));
                foreach (var t in def.Descendants().Where(e => e.Name.LocalName is "texPath" or "uiIconPath" && !e.HasElements))
                    if (t.Value.Trim() is { Length: > 0 } p) texPaths.Add((p.Replace('\\', '/').Trim('/'), $"{def.Name.LocalName} {name}", rel));
            }
        }

        foreach (var g in defs.GroupBy(d => (d.Type, d.Name)).Where(g => g.Count() > 1))
            w.Add($"defines {g.Key.Type} {g.Key.Name} {g.Count()} times ({string.Join(", ", g.Select(d => d.File).Distinct())}); the last one loaded replaces the others");

        var textureRoots = folders.Select(f => Path.Combine(f, "Textures")).Where(Directory.Exists).ToList();
        var missing = texPaths.Where(t => !TextureExists(textureRoots, t.Path) && OwnTextureFolder(textureRoots, t.Path))
            .Select(t => $"{t.Path} ({t.Def})").Distinct().ToList();
        if (missing.Count > 0)
            w.Add($"{missing.Count} texture path{(missing.Count == 1 ? "" : "s")} point{(missing.Count == 1 ? "s" : "")} into the mod's own Textures folders but no texture is there, so players see a pink square: {PackageCheck.Examples(missing)}");

        if (UnusedKeyed(modDir, folders) is { Count: > 0 } unused)
            w.Add($"{unused.Count} English Keyed text{(unused.Count == 1 ? "" : "s")} that no DLL or XML in the mod mentions (left over, or built from parts in code?): {PackageCheck.Examples(unused)}");
        return w;
    }

    /// <summary>A Graphic_Single file, a Graphic_Multi set (path_north...) or a folder (Graphic_Random and the like).</summary>
    static bool TextureExists(IEnumerable<string> roots, string texPath) => roots.Any(root =>
    {
        var basePath = Path.Combine(root, texPath.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(basePath)) return true;
        return TextureExtensions.Any(ext => File.Exists(basePath + ext) || Facings.Any(f => File.Exists(basePath + f + ext)));
    });

    /// <summary>True when the texture's folder exists in the mod (so the mod meant to ship it), not a path into the game's own textures.</summary>
    static bool OwnTextureFolder(IEnumerable<string> roots, string texPath)
    {
        var dir = Path.GetDirectoryName(texPath.Replace('/', Path.DirectorySeparatorChar));
        return !string.IsNullOrEmpty(dir) && roots.Any(r => Directory.Exists(Path.Combine(r, dir)));
    }

    static List<string> UnusedKeyed(string modDir, IReadOnlyList<string> folders)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in folders)
        {
            var english = Path.Combine(f, "Languages", "English", "Keyed");
            keys.UnionWith(RimWorldLint.KeyedKeys(english));
        }
        if (keys.Count == 0) return new();
        var dlls = folders.Select(f => Path.Combine(f, "Assemblies")).Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.dll")).ToList();
        // Without code, Keyed texts are usually there to replace the game's own; nothing to compare with.
        if (dlls.Count == 0) return new();
        var strings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dll in dlls) strings.UnionWith(UserStrings(dll));
        var text = new System.Text.StringBuilder();
        foreach (var sub in new[] { "Defs", "Patches" })
            foreach (var file in LoadFolders.Files(folders, sub))
                try { text.Append(File.ReadAllText(file)); } catch (IOException) { }
        var xml = text.ToString();
        return keys.Where(k => !strings.Contains(k) && !strings.Any(s => s.Contains(k, StringComparison.Ordinal)) && !xml.Contains(k, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    static readonly Dictionary<string, (DateTime Time, long Size, List<string> Strings)> StringsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The string literals in a .NET DLL (its user string heap); cached by the file's time and size.</summary>
    public static List<string> UserStrings(string dll)
    {
        FileInfo info;
        try { info = new FileInfo(dll); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(); }
        lock (StringsCache)
            if (StringsCache.TryGetValue(dll, out var hit) && hit.Time == info.LastWriteTimeUtc && hit.Size == info.Length) return hit.Strings;
        var list = new List<string>();
        try
        {
            using var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var pe = new PEReader(fs);
            if (pe.HasMetadata)
            {
                var md = pe.GetMetadataReader();
                for (var h = md.GetNextHandle(default(UserStringHandle)); !h.IsNil; h = md.GetNextHandle(h))
                    list.Add(md.GetUserString(h));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { }
        lock (StringsCache) StringsCache[dll] = (info.LastWriteTimeUtc, info.Length, list);
        return list;
    }
}
