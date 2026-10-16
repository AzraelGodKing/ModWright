using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>One file in a staged package: its path inside the mod (forward slashes), size and, when asked for, SHA-256.</summary>
public sealed record PackageFile(string Path, long Bytes, string Hash = "");

/// <summary>What a staged package holds, and anything in it that looks like it should not be uploaded.</summary>
public sealed record PackageReport(IReadOnlyList<PackageFile> Files, long TotalBytes, IReadOnlyList<string> Warnings)
{
    public double MegaBytes => Math.Round(TotalBytes / 1048576.0, 2);

    /// <summary>How the package differs from the last upload of the same item from this PC; null when there is none to compare with.</summary>
    public PackageDiff? Diff { get; init; }
}

/// <summary>
/// Looks through a staged package before it is uploaded. Nothing here blocks an upload except an empty package (which
/// would wipe the item's files on Steam); the rest are warnings about files that usually end up in a package by mistake.
/// </summary>
public static class PackageCheck
{
    public const long BigFile = 100L * 1024 * 1024;
    public const long BigPackage = 1024L * 1024 * 1024;

    static readonly string[] JunkFolders = { ".git", ".svn", ".hg", ".vs", ".idea", ".vscode", "obj", "node_modules", "__pycache__" };
    static readonly string[] SourceArt = { ".psd", ".psb", ".xcf", ".kra", ".blend", ".blend1", ".ai", ".max", ".ma", ".mb", ".spp", ".sai", ".clip", ".afphoto", ".afdesign" };
    static readonly string[] JunkFiles = { ".pdb", ".tmp", ".bak", ".orig", ".log", ".suo", ".user", ".swp" };
    static readonly string[] JunkNames = { "Thumbs.db", ".DS_Store", "desktop.ini" };
    static readonly string[] Archives = { ".zip", ".rar", ".7z", ".tar", ".gz" };

    /// <param name="hash">Also work out each file's SHA-256, to compare the package with the last upload.</param>
    public static PackageReport Inspect(string stage, bool hash = false)
    {
        var files = Directory.Exists(stage)
            ? Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
                .Select(f => new PackageFile(Path.GetRelativePath(stage, f).Replace('\\', '/'), new FileInfo(f).Length, hash ? HashOf(f) : ""))
                .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<PackageFile>();
        return new PackageReport(files, files.Sum(f => f.Bytes), Warn(files));
    }

    /// <summary>The warnings for a list of files (one line per kind of problem, with up to three examples).</summary>
    public static List<string> Warn(IReadOnlyList<PackageFile> files)
    {
        var w = new List<string>();
        static string Examples(IEnumerable<string> names)
        {
            var list = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return string.Join(", ", list.Take(3)) + (list.Count > 3 ? $" and {list.Count - 3} more" : "");
        }
        string Ext(PackageFile f) => Path.GetExtension(f.Path).ToLowerInvariant();

        var folders = files.SelectMany(f => f.Path.Split('/').SkipLast(1))
            .Where(d => JunkFolders.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
        if (folders.Count > 0) w.Add($"has tool or build folders that are rarely meant for players: {Examples(folders)}");

        var art = files.Where(f => SourceArt.Contains(Ext(f))).Select(f => f.Path).ToList();
        if (art.Count > 0) w.Add($"has source art files ({art.Count}): {Examples(art)}");

        var junk = files.Where(f => JunkFiles.Contains(Ext(f)) || JunkNames.Contains(Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)).Select(f => f.Path).ToList();
        if (junk.Count > 0) w.Add($"has leftover or debug files ({junk.Count}): {Examples(junk)}");

        var archives = files.Where(f => Archives.Contains(Ext(f))).Select(f => f.Path).ToList();
        if (archives.Count > 0) w.Add($"has archives inside it ({archives.Count}): {Examples(archives)}");

        var big = files.Where(f => f.Bytes >= BigFile).Select(f => $"{f.Path} ({f.Bytes / 1048576} MB)").ToList();
        if (big.Count > 0) w.Add($"has very large files: {Examples(big)}");

        var total = files.Sum(f => f.Bytes);
        if (total >= BigPackage) w.Add($"is {total / 1048576} MB in total, which takes a long time to upload and download");

        var longPaths = files.Where(f => f.Path.Length > LongPath).Select(f => f.Path).ToList();
        if (longPaths.Count > 0) w.Add($"has paths over {LongPath} characters, which can pass Windows' 260-character limit once installed under steamapps\\workshop: {Examples(longPaths)}");

        var collisions = files.GroupBy(f => f.Path.ToLowerInvariant()).Where(g => g.Count() > 1).Select(g => string.Join(" / ", g.Select(f => f.Path))).ToList();
        if (collisions.Count > 0) w.Add($"has files whose names differ only in capital letters, so they overwrite each other on Windows and macOS: {Examples(collisions)}");

        var badNames = files.Where(f => f.Path.Split('/').Any(BadName)).Select(f => f.Path).ToList();
        if (badNames.Count > 0) w.Add($"has names Windows cannot use (reserved names such as CON or NUL, the characters < > : \" | ? *, or a trailing dot or space): {Examples(badNames)}");
        return w;
    }

    /// <summary>
    /// Longest path inside a package that is safe to install: Steam puts items under
    /// "...\steamapps\workshop\content\&lt;app&gt;\&lt;id&gt;\", which with a typical Steam folder leaves about this much of
    /// Windows' 260 characters.
    /// </summary>
    public const int LongPath = 160;

    static readonly Regex Reserved = new(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase);

    /// <summary>True for a file or folder name that Windows refuses or silently changes.</summary>
    public static bool BadName(string name) =>
        name.Length > 0 && (Reserved.IsMatch(name) || name.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || name.EndsWith('.') || name.EndsWith(' '));

    /// <summary>
    /// The paths of <paramref name="required"/> (paths or globs inside the mod, such as "About/About.xml") that have no
    /// file in the package. Such a package would break the mod for everyone who updates.
    /// </summary>
    public static List<string> MissingRequired(IReadOnlyList<PackageFile> files, IEnumerable<string> required)
    {
        var missing = new List<string>();
        foreach (var r in required.Select(r => r.Replace('\\', '/').Trim('/')).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var re = new Regex("^" + Regex.Escape(r).Replace("\\*", "[^/]*").Replace("\\?", "[^/]") + "$",
                RegexOptions.IgnoreCase);
            if (!files.Any(f => re.IsMatch(f.Path))) missing.Add(r);
        }
        return missing;
    }

    static string HashOf(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
    }

    /// <summary>A size for people: "512 B", "14.2 KB", "3.1 MB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#") + " KB",
        < 1024L * 1024 * 1024 => (bytes / 1048576.0).ToString("0.#") + " MB",
        _ => (bytes / 1073741824.0).ToString("0.##") + " GB",
    };
}
