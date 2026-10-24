using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod;

/// <summary>A mod's newest GitHub release.</summary>
/// <param name="Download">The release's .zip asset, else GitHub's zip of the tagged source.</param>
public sealed record GitHubModRelease(string Version, string Page, string Download, DateTimeOffset? Published);

/// <summary>A Mods folder mod with a newer GitHub release.</summary>
public sealed record GitHubModUpdate(ModManifest Mod, GitHubModRelease Release);

/// <summary>
/// Updates for mods that aren't on the Workshop but come from GitHub: a Mods folder mod whose About.xml url points at a
/// GitHub repository is checked against that repository's latest release, and can be updated in place with the old
/// copy zipped up first.
/// </summary>
public static class GitHubModUpdates
{
    static readonly Regex RepoUrl = new(@"^https?://(?:www\.)?github\.com/([\w.-]+)/([\w.-]+?)(?:\.git)?(?:[/?#].*)?$", RegexOptions.IgnoreCase);

    /// <summary>"owner/repo" from a GitHub link, or null.</summary>
    public static string? RepoOf(string? url)
    {
        var m = RepoUrl.Match((url ?? "").Trim());
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : null;
    }

    /// <summary>The mods worth checking: Mods folder copies (not Workshop or game ones) that link a GitHub repository and are not someone's working copy.</summary>
    public static List<ModManifest> Candidates(IEnumerable<ModManifest> mods) =>
        mods.Where(m => m.Source == ModSource.Local && RepoOf(m.Url) != null && Updatable(m) == null).DistinctBy(m => m.Folder).ToList();

    /// <summary>Why a mod can't be updated in place (a link or a git working copy), or null when it can.</summary>
    public static string? Updatable(ModManifest m)
    {
        if (FolderLinks.IsLink(m.Folder)) return "it is a link to a folder elsewhere";
        if (Directory.Exists(Path.Combine(m.Folder, ".git"))) return "it is a git working copy";
        return null;
    }

    /// <summary>Reads GitHub's "latest release" answer; null for drafts, pre-releases and answers without a tag.</summary>
    public static GitHubModRelease? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) return null;
        if (Flag(r, "draft") || Flag(r, "prerelease")) return null;
        var tag = Str(r, "tag_name");
        var zipball = Str(r, "zipball_url");
        if (tag == null) return null;
        string? asset = null;
        if (r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array)
            asset = a.EnumerateArray().Where(x => (Str(x, "name") ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Select(x => Str(x, "browser_download_url")).FirstOrDefault(u => u != null);
        var download = asset ?? zipball;
        if (download == null) return null;
        DateTimeOffset? published = DateTimeOffset.TryParse(Str(r, "published_at"), out var p) ? p : null;
        return new GitHubModRelease(tag.Trim().TrimStart('v', 'V'), Str(r, "html_url") ?? "", download, published);
    }

    /// <summary>
    /// True when the release is newer than the installed mod: by version when About.xml has a modVersion, else by date
    /// (published more than a day after the mod's About.xml was last written).
    /// </summary>
    public static bool IsNewer(ModManifest mod, GitHubModRelease release)
    {
        if (mod.ModVersion.Trim().Length > 0) return UpdateCheck.IsNewer(release.Version, mod.ModVersion.Trim());
        if (release.Published is not { } when) return false;
        var about = SafeFiles.Find(mod.Folder, "About/About.xml");
        return about != null && when.UtcDateTime > File.GetLastWriteTimeUtc(about).AddDays(1);
    }

    /// <summary>Asks GitHub for each candidate's latest release (each repository once, at most 50, since GitHub allows 60 questions an hour without signing in).</summary>
    public static async Task<List<GitHubModUpdate>> CheckAsync(IEnumerable<ModManifest> candidates, HttpClient http, CancellationToken ct)
    {
        var list = new List<GitHubModUpdate>();
        var byRepo = candidates.GroupBy(m => RepoOf(m.Url)!, StringComparer.OrdinalIgnoreCase).Take(50);
        foreach (var g in byRepo)
        {
            ct.ThrowIfCancellationRequested();
            GitHubModRelease? release;
            try { release = Parse(await http.GetStringAsync($"https://api.github.com/repos/{g.Key}/releases/latest", ct)); }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { continue; }
            if (release == null) continue;
            list.AddRange(g.Where(m => IsNewer(m, release)).Select(m => new GitHubModUpdate(m, release)));
        }
        return list;
    }

    /// <summary>
    /// The folder in an unpacked release that is this mod: the one whose About.xml has the same packageId, else the only
    /// mod in it. Null when it can't tell.
    /// </summary>
    public static string? FindMod(string unpacked, ModManifest mod, Func<string, ModManifest?> read)
    {
        var found = new List<(string Dir, ModManifest M)>();
        void Walk(string dir, int depth)
        {
            if (read(dir) is { } m) { found.Add((dir, m)); return; }
            if (depth >= 3) return;
            foreach (var sub in Directory.GetDirectories(dir)) Walk(sub, depth + 1);
        }
        Walk(unpacked, 0);
        if (mod.Id.Length > 0 && found.FirstOrDefault(f => f.M.Key == mod.Key) is { Dir: not null } same) return same.Dir;
        return found.Count == 1 ? found[0].Dir : null;
    }

    /// <summary>
    /// Puts <paramref name="newFolder"/>'s files in place of the mod's folder. The old folder is first zipped into
    /// <paramref name="backups"/> ("&lt;folder&gt; &lt;time&gt;.zip"); if anything fails the old folder is put back.
    /// Returns the backup's path.
    /// </summary>
    public static string Replace(ModManifest mod, string newFolder, string backups, DateTime? now = null)
    {
        if (Updatable(mod) is { } why) throw new IOException($"{mod.DisplayName} can't be updated here: {why}.");
        Directory.CreateDirectory(backups);
        var backup = Path.Combine(backups, $"{SafeFiles.SafeName(mod.FolderName, "mod")} {(now ?? DateTime.Now):yyyyMMdd-HHmmss}.zip");
        ZipFile.CreateFromDirectory(mod.Folder, backup, CompressionLevel.Fastest, includeBaseDirectory: false);
        var incoming = mod.Folder + ".incoming";
        var old = mod.Folder + ".old";
        try
        {
            if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
            WorkshopDownload.CopyTo(newFolder, incoming);
            if (Directory.Exists(old)) Directory.Delete(old, true);
            Directory.Move(mod.Folder, old);
            Directory.Move(incoming, mod.Folder);
            Directory.Delete(old, true);
        }
        finally
        {
            try { if (Directory.Exists(incoming)) Directory.Delete(incoming, true); } catch { }
            if (!Directory.Exists(mod.Folder) && Directory.Exists(old)) Directory.Move(old, mod.Folder);
        }
        return backup;
    }

    /// <summary>Downloads the release, unpacks it to a temporary folder and puts the mod in place. Returns the backup's path.</summary>
    public static async Task<string> InstallAsync(GitHubModUpdate u, HttpClient http, Func<string, ModManifest?> read, string backups, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "modwright-gh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "release.zip");
            await using (var s = await http.GetStreamAsync(u.Release.Download, ct))
            await using (var f = File.Create(zip))
                await s.CopyToAsync(f, ct);
            var unpacked = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zip, unpacked); // refuses entries that would land outside the folder
            var dir = FindMod(unpacked, u.Mod, read) ?? throw new IOException($"the release of {u.Mod.DisplayName} doesn't hold the mod (no About.xml with its packageId)");
            return Replace(u.Mod, dir, backups);
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
