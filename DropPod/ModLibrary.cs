using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;

namespace SteamCmdHelper.DropPod;

/// <summary>The mods found for a game: every copy (duplicates included), and what could not be read.</summary>
public sealed class LibraryScan
{
    public GameInstall Install { get; init; } = null!;
    /// <summary>Every mod found, sorted by name. Two copies of the same id are both here; see <see cref="Duplicates"/>.</summary>
    public List<ModManifest> Mods { get; init; } = new();
    /// <summary>Ids of the active mods in load order, or null when the game has no list yet.</summary>
    public IReadOnlyList<string>? Active { get; init; }
    /// <summary>Folders that could not be read at all, in words.</summary>
    public List<string> Notes { get; init; } = new();

    /// <summary>Mods by <see cref="ModManifest.Key"/>; where an id has several copies, the one the game prefers (first mod folder wins).</summary>
    public Dictionary<string, ModManifest> ById()
    {
        var d = new Dictionary<string, ModManifest>(StringComparer.Ordinal);
        foreach (var m in Mods.OrderBy(m => m.Source switch { ModSource.Official => 0, ModSource.Local => 1, _ => 2 }))
            d.TryAdd(m.Key, m);
        return d;
    }

    /// <summary>Ids that more than one mod folder claims.</summary>
    public List<IGrouping<string, ModManifest>> Duplicates() =>
        Mods.Where(m => m.Id.Length > 0).GroupBy(m => m.Key).Where(g => g.Count() > 1).ToList();
}

/// <summary>Reads every mod folder a game has, in parallel, without ever failing on one bad mod.</summary>
public static class ModLibrary
{
    public static LibraryScan Scan(IGameModule game, GameInstall install, CancellationToken ct = default)
    {
        var notes = new ConcurrentBag<string>();
        var found = new ConcurrentBag<ModManifest>();
        var folders = new List<ModFolder>();
        var listed = new List<ModFolder>();
        foreach (var mf in game.ModFolders(install))
        {
            try
            {
                if (!Directory.Exists(mf.Path)) continue;
                listed.Add(mf);
                folders.AddRange(Directory.GetDirectories(mf.Path).Select(d => new ModFolder(d, mf.Source)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notes.Add($"Could not list {mf.Path}: {e.Message}"); }
        }
        Parallel.ForEach(folders, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, f =>
        {
            try
            {
                if (ReadCached(game, f) is { } m) found.Add(m);
            }
            catch (Exception e) { notes.Add($"Could not read {f.Path}: {e.Message}"); }
        });
        // Forget folders that are gone, so the cache doesn't grow with every mod ever seen.
        var seen = folders.Select(f => CacheKey(game, f)).ToHashSet();
        var roots = listed.Select(r => CacheKey(game, r) + Path.DirectorySeparatorChar).ToList();
        foreach (var k in Cache.Keys.Where(k => !seen.Contains(k) && roots.Any(r => k.StartsWith(r, StringComparison.Ordinal))).ToList()) Cache.TryRemove(k, out _);
        IReadOnlyList<string>? active = null;
        try { active = game.ReadActiveMods(install); }
        catch (Exception e) { notes.Add("Could not read the active mod list: " + e.Message); }
        return new LibraryScan
        {
            Install = install,
            Mods = found.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.Folder, StringComparer.OrdinalIgnoreCase).ToList(),
            Active = active,
            Notes = notes.OrderBy(n => n, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>
    /// Mods read before, by folder, with a stamp of their About folder (each file's name, size and time). A rescan reads
    /// only the mods whose About folder changed, which matters with a thousand or more Workshop mods.
    /// </summary>
    static readonly ConcurrentDictionary<string, (string Stamp, ModManifest Mod)> Cache = new(StringComparer.Ordinal);

    static string CacheKey(IGameModule game, ModFolder f) => $"{game.AppId}|{f.Source}|{f.Path}";

    /// <summary>The mod in the folder, from the cache when its About folder hasn't changed. Folders without an About folder are always read.</summary>
    public static ModManifest? ReadCached(IGameModule game, ModFolder f)
    {
        var stamp = Stamp(f.Path);
        var key = CacheKey(game, f);
        if (stamp != null && Cache.TryGetValue(key, out var hit) && hit.Stamp == stamp) return hit.Mod;
        var m = game.ReadMod(f.Path, f.Source);
        if (stamp != null && m != null) Cache[key] = (stamp, m);
        else Cache.TryRemove(key, out _);
        return m;
    }

    /// <summary>The About folder's files with their sizes and times, or null when there's no About folder.</summary>
    public static string? Stamp(string modFolder)
    {
        if (SafeFiles.Child(modFolder, "About", directory: true) is not { } about) return null;
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var file in new DirectoryInfo(about).EnumerateFiles().OrderBy(x => x.Name, StringComparer.Ordinal))
                sb.Append(file.Name).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append(';');
            return sb.ToString();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>File helpers for reading other people's files: case-insensitive lookups and XML that cannot reach out.</summary>
public static class SafeFiles
{
    /// <summary>
    /// The child of <paramref name="dir"/> named <paramref name="name"/> in any letter case (Windows ignores case, mods made on
    /// other systems sometimes ship "about/about.xml"), or null.
    /// </summary>
    public static string? Child(string dir, string name, bool directory = false)
    {
        var exact = Path.Combine(dir, name);
        if (directory ? Directory.Exists(exact) : File.Exists(exact)) return exact;
        try
        {
            if (!Directory.Exists(dir)) return null;
            var all = directory ? Directory.EnumerateDirectories(dir) : Directory.EnumerateFiles(dir);
            return all.FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A relative path ("About/About.xml") resolved one part at a time, ignoring case.</summary>
    public static string? Find(string dir, string relPath)
    {
        var parts = relPath.Split('/', '\\');
        var cur = dir;
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Child(cur, parts[i], directory: i < parts.Length - 1);
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="content"/> safely: written to a temp file first and moved over, so
    /// a crash never leaves half a file. The old file is kept as "&lt;name&gt;.&lt;yyyyMMdd-HHmmss&gt;.bak" next to it (the
    /// newest <paramref name="keep"/> backups are kept).
    /// </summary>
    public static string? WriteWithBackup(string path, string content, int keep = 5, DateTime? now = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        string? backup = null;
        if (File.Exists(path))
        {
            backup = $"{path}.{(now ?? DateTime.Now):yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backup, overwrite: true);
            var name = Path.GetFileName(path);
            foreach (var old in Directory.GetFiles(dir, name + ".*.bak").OrderByDescending(f => f, StringComparer.Ordinal).Skip(keep))
                try { File.Delete(old); } catch (IOException) { }
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
        return backup;
    }

    /// <summary>Reader settings for untrusted XML: no DTDs, no external files, and a size cap so a broken file can't eat memory.</summary>
    /// <summary>A file or folder name for something the player named: unsafe characters replaced, never empty, at most 80 characters.</summary>
    public static string SafeName(string name, string fallback)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var safe = new string(name.Trim().Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (safe.Length == 0) safe = fallback;
        if (System.Text.RegularExpressions.Regex.IsMatch(safe, @"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) safe = "_" + safe;
        return safe.Length > 80 ? safe[..80] : safe;
    }

    public static XmlReaderSettings XmlSettings(long maxChars = 16_000_000) => new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        MaxCharactersInDocument = maxChars,
        IgnoreComments = true,
        CloseInput = true,
    };

    /// <summary>Parses <paramref name="xml"/> with <see cref="XmlSettings"/>.</summary>
    public static XDocument ParseXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), XmlSettings());
        return XDocument.Load(reader);
    }
}
