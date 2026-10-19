using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <param name="Folder">Where SteamCMD put the item's files, when the download worked.</param>
public sealed record DownloadResult(bool Ok, string Message, string? Folder);

/// <summary>How the files Steam hands out compare with what was uploaded.</summary>
/// <param name="Missing">Uploaded, but Steam's copy does not have them.</param>
/// <param name="Extra">In Steam's copy, but not in the upload.</param>
/// <param name="Different">In both, with different contents.</param>
public sealed record SteamCopyCheck(int Files, IReadOnlyList<string> Missing, IReadOnlyList<string> Extra, IReadOnlyList<string> Different)
{
    public bool Matches => Missing.Count == 0 && Extra.Count == 0 && Different.Count == 0;
}

/// <summary>
/// Downloads a Workshop item with SteamCMD's "workshop_download_item", the way a subscriber gets it. Used to check an
/// upload by comparing what Steam hands out with what was sent, and to restore a mod whose local copy is lost.
/// Needs the saved login of an account that owns the game; Steam refuses anonymous downloads for most games.
/// </summary>
public static class WorkshopDownload
{
    const RegexOptions Ic = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    // "Success. Downloaded item 123456789 to "C:\steamcmd\steamapps\workshop\content\294100\123456789" (12345 bytes)"
    static readonly Regex Done = new("Success\\.?\\s*Downloaded item (\\d+) to \"([^\"]+)\"", Ic);
    static readonly Regex Failed = new(@"ERROR!.*|Download item \d+ failed.*|(?:^|\.\.\.)\s*FAILED.*", Ic);

    public static string[] Args(string account, string appId, string itemId) =>
        SteamCmd.FailFast.Concat(SteamCmd.Login(account)).Concat(new[] { "+workshop_download_item", appId, itemId, "validate", "+quit" }).ToArray();

    /// <summary>Where SteamCMD keeps a downloaded item when its output does not say.</summary>
    public static string DefaultFolder(string steamCmd, string appId, string itemId) =>
        Path.Combine(Path.GetDirectoryName(steamCmd) ?? "", "steamapps", "workshop", "content", appId, itemId);

    /// <summary>Reads the outcome from SteamCMD's output.</summary>
    public static DownloadResult Evaluate(IReadOnlyList<string> lines, int exit, string steamCmd, string appId, string itemId)
    {
        var clean = lines.Select(SteamCmd.Clean).ToList();
        if (SteamCmd.LoginFailure(clean) is { } login) return new DownloadResult(false, "Steam refused the login: " + login, null);
        var done = clean.Select(l => Done.Match(l)).FirstOrDefault(m => m.Success && m.Groups[1].Value == itemId);
        if (done != null) return new DownloadResult(true, "Downloaded", done.Groups[2].Value);
        var failed = clean.FirstOrDefault(l => Failed.IsMatch(l));
        if (failed != null) return new DownloadResult(false, "SteamCMD could not download it: " + failed, null);
        var fallback = DefaultFolder(steamCmd, appId, itemId);
        if (exit == 0 && Directory.Exists(fallback) && Directory.EnumerateFileSystemEntries(fallback).Any())
            return new DownloadResult(true, "Downloaded", fallback);
        return new DownloadResult(false, exit != 0 ? $"SteamCMD exited with code {exit} without downloading it." : "SteamCMD finished without saying the item downloaded.", null);
    }

    /// <summary>Downloads one item into SteamCMD's Workshop folder.</summary>
    public static async Task<DownloadResult> DownloadAsync(string steamCmd, string account, string appId, string itemId, Action<string>? onLine, CancellationToken ct)
    {
        var run = await SteamCmd.RunAsync(steamCmd, Args(account, appId, itemId), onLine, ct);
        return Evaluate(run.Lines, run.Exit, steamCmd, appId, itemId);
    }

    /// <summary>Compares the files in <paramref name="folder"/> with what an upload sent, by path and SHA-256.</summary>
    public static SteamCopyCheck Compare(IReadOnlyList<PackageFile> sent, string folder)
    {
        var got = PackageCheck.Inspect(folder, hash: true).Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var sentPaths = sent.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        var different = new List<string>();
        foreach (var f in sent)
        {
            if (!got.TryGetValue(f.Path, out var g)) missing.Add(f.Path);
            else if (g.Bytes != f.Bytes || (f.Hash.Length > 0 && !f.Hash.Equals(g.Hash, StringComparison.OrdinalIgnoreCase))) different.Add(f.Path);
        }
        var extra = got.Keys.Where(p => !sentPaths.Contains(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        return new SteamCopyCheck(got.Count, missing, extra, different);
    }

    /// <summary>The log lines for a check: one summary line, then up to <paramref name="max"/> files that differ.</summary>
    public static List<string> Describe(SteamCopyCheck c, string what, int max = 8)
    {
        if (c.Matches) return new() { $"Steam's copy matches {what}: all {c.Files} file{(c.Files == 1 ? "" : "s")} are the same." };
        var parts = new List<string>();
        if (c.Missing.Count > 0) parts.Add($"{c.Missing.Count} missing");
        if (c.Different.Count > 0) parts.Add($"{c.Different.Count} different");
        if (c.Extra.Count > 0) parts.Add($"{c.Extra.Count} extra");
        var lines = new List<string> { $"Warning: Steam's copy does not match {what}: {string.Join(", ", parts)}." };
        var all = c.Missing.Select(p => "  - " + p).Concat(c.Different.Select(p => "  ~ " + p)).Concat(c.Extra.Select(p => "  + " + p)).ToList();
        lines.AddRange(all.Take(max));
        if (all.Count > max) lines.Add($"  ...and {all.Count - max} more");
        lines.Add("Steam can hand out the previous version for a few minutes after an upload. If this stays, upload the mod again.");
        return lines;
    }

    /// <summary>Copies a downloaded item into <paramref name="dest"/>, which must be empty or not exist yet.</summary>
    public static int CopyTo(string folder, string dest)
    {
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
            throw new IOException($"{dest} is not empty. Pick an empty folder, so nothing is overwritten.");
        var n = 0;
        foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(dest, Path.GetRelativePath(folder, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
            n++;
        }
        return n;
    }

    /// <summary>
    /// Removes SteamCMD's copy once it has been used, so its Workshop folder does not keep growing. Only a folder inside
    /// SteamCMD's own folder is removed. Never throws.
    /// </summary>
    public static void Forget(string steamCmd, string? folder)
    {
        try
        {
            if (folder == null || !Directory.Exists(folder)) return;
            var root = Path.GetFullPath(Path.GetDirectoryName(steamCmd) ?? "") + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder, true);
        }
        catch { }
    }
}
