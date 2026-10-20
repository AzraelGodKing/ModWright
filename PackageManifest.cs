using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>What one upload sent to a Workshop item: when, which version, and every file with its size and SHA-256.</summary>
public sealed record PackageManifest(DateTimeOffset Time, string Version, List<PackageFile> Files)
{
    public long TotalBytes => Files.Sum(f => f.Bytes);
}

/// <summary>How a package differs from the last one uploaded to the same item, by path and content.</summary>
public sealed record PackageDiff(PackageManifest Last, IReadOnlyList<string> Added, IReadOnlyList<string> Changed,
    IReadOnlyList<string> Removed, long NewBytes)
{
    /// <summary>Every file is the same, byte for byte, as in the last upload.</summary>
    public bool Identical => Added.Count == 0 && Changed.Count == 0 && Removed.Count == 0;

    /// <summary>"2 added, 5 changed, 1 removed", or "no file changed".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Added.Count > 0) parts.Add(Added.Count + " added");
            if (Changed.Count > 0) parts.Add(Changed.Count + " changed");
            if (Removed.Count > 0) parts.Add(Removed.Count + " removed");
            return parts.Count == 0 ? "no file changed" : string.Join(", ", parts);
        }
    }

    /// <summary>"the last upload (v1.2.0 on 3 Oct 2026)".</summary>
    public string LastUpload => $"the last upload ({(Last.Version.Length > 0 ? "v" + Last.Version + " " : "")}on {Last.Time.ToLocalTime():d MMM yyyy})";

    /// <summary>"since the last upload (v1.2.0 on 3 Oct 2026)".</summary>
    public string Since => "since " + LastUpload;

    /// <summary>
    /// Warnings, in the words of <see cref="PackageCheck.Warn"/> ("the package ..."), when the package shrank or grew a lot
    /// or lost many files since the last upload. That usually means something is missing (a build folder) or was added
    /// by mistake.
    /// </summary>
    public List<string> Warnings()
    {
        var w = new List<string>();
        var old = Last.TotalBytes;
        if (Math.Abs(NewBytes - old) >= PackageDiffs.SizeJumpBytes && old > 0 && (NewBytes * 2 < old || NewBytes > old * 2))
            w.Add($"is {PackageCheck.Size(NewBytes)}, against {PackageCheck.Size(old)} {Since}; check that nothing is missing or was added by mistake");
        if (Removed.Count >= Math.Max(PackageDiffs.ManyRemoved, Last.Files.Count * 3 / 10))
            w.Add($"has lost {Removed.Count} of its {Last.Files.Count} files {Since}, such as {string.Join(", ", Removed.Take(3))}");
        return w;
    }
}

/// <summary>
/// Remembers what each upload sent (one file per Workshop item in %LocalAppData%\SteamCmdHelper\manifests) and compares
/// the next package with it.
/// </summary>
public static class PackageDiffs
{
    /// <summary>A size change smaller than this is never worth a warning, whatever the ratio.</summary>
    public const long SizeJumpBytes = 1024 * 1024;

    /// <summary>At least this many removed files (and 30% of the last upload's) is worth a warning.</summary>
    public const int ManyRemoved = 5;

    public static string DefaultDir => Path.Combine(AppSettings.Folder, "manifests");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

    static string FileFor(string dir, string workshopId) => Path.Combine(dir, workshopId + ".json");

    /// <summary>The last upload's manifest for an item, or null when this PC has none (or it cannot be read).</summary>
    public static PackageManifest? Load(string dir, string workshopId)
    {
        try
        {
            var p = FileFor(dir, workshopId);
            return File.Exists(p) ? JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(p)) : null;
        }
        catch { return null; }
    }

    /// <summary>Saves what was just uploaded to an item. Never throws: a missing manifest only means no comparison next time.</summary>
    public static void Save(string dir, string workshopId, PackageManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var p = FileFor(dir, workshopId);
            File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(manifest, Opts));
            File.Move(p + ".tmp", p, true);
        }
        catch { }
    }

    /// <summary>Compares a package with the last upload's manifest. Files without a hash are compared by size.</summary>
    public static PackageDiff Compare(PackageManifest last, IReadOnlyList<PackageFile> files)
    {
        var before = last.Files.GroupBy(f => f.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var now = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        var changed = new List<string>();
        foreach (var f in files)
        {
            if (!before.TryGetValue(f.Path, out var old)) added.Add(f.Path);
            else if (old.Bytes != f.Bytes || (old.Hash.Length > 0 && f.Hash.Length > 0 && !old.Hash.Equals(f.Hash, StringComparison.OrdinalIgnoreCase))) changed.Add(f.Path);
        }
        var removed = last.Files.Select(f => f.Path).Where(p => !now.Contains(p)).ToList();
        return new PackageDiff(last, added, changed, removed, files.Sum(f => f.Bytes));
    }

    /// <summary>The log lines describing a diff: the summary, then up to <paramref name="max"/> files that changed.</summary>
    public static List<string> Describe(PackageDiff d, int max = 8)
    {
        var lines = new List<string> { $"Package {d.Since}: {d.Summary}." };
        var all = d.Added.Select(p => "  + " + p).Concat(d.Changed.Select(p => "  ~ " + p)).Concat(d.Removed.Select(p => "  - " + p)).ToList();
        lines.AddRange(all.Take(max));
        if (all.Count > max) lines.Add($"  ...and {all.Count - max} more (right-click the mod > Package > Preview package... lists them all)");
        return lines;
    }
}
