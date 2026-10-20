using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Keeps a zip of exactly what each upload sent (Options > Uploading), so any recent release can be looked at or
/// restored without git. Kept per Workshop item in the data folder's packages folder, the newest <see cref="Keep"/>.
/// </summary>
public static class PackageArchive
{
    public const int Keep = 5;

    public static string DefaultDir => Path.Combine(AppSettings.Folder, "packages");

    /// <summary>The folder that holds item <paramref name="id"/>'s zips.</summary>
    public static string FolderFor(string dir, string id) => Path.Combine(dir, id);

    /// <summary>Zips <paramref name="stage"/> as version <paramref name="version"/> of item <paramref name="id"/> and drops the oldest beyond <paramref name="keep"/>. Returns the zip.</summary>
    public static string Save(string dir, string id, string version, string stage, int keep = Keep, DateTime? now = null)
    {
        var folder = FolderFor(dir, id);
        Directory.CreateDirectory(folder);
        var safe = Regex.Replace(version.Length > 0 ? version : "unknown", @"[^\w.\-]+", "_");
        var zip = Path.Combine(folder, $"{(now ?? DateTime.Now):yyyyMMdd-HHmmss}_v{safe}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(stage, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
        foreach (var old in List(dir, id).Skip(Math.Max(1, keep)))
            try { File.Delete(old); } catch (IOException) { }
        return zip;
    }

    /// <summary>Item <paramref name="id"/>'s kept zips, newest first.</summary>
    public static List<string> List(string dir, string id)
    {
        var folder = FolderFor(dir, id);
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.zip").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : new List<string>();
    }
}
