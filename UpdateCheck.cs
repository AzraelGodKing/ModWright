using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>A published release that is newer than the running program.</summary>
/// <param name="DownloadUrl">What the browser should download: the standalone exe, else the first .exe or .zip.</param>
/// <param name="ExeUrl">The standalone exe ("...-win-x64.exe"), used to update a single-file install in place.</param>
/// <param name="ZipUrl">The framework-dependent folder build ("...-framework-dependent.zip"), used to update a folder install.</param>
/// <param name="ChecksumsUrl">SHA256SUMS.txt, which every in-place update is checked against.</param>
public sealed record ReleaseInfo(string Version, string PageUrl, string? DownloadUrl,
    string? ExeUrl = null, string? ZipUrl = null, string? ChecksumsUrl = null);

/// <summary>
/// Looks up the latest GitHub release of this program. Only the public releases API is read: nothing about the user
/// or their mods is sent. Downloading and installing it is <see cref="SelfUpdate"/>'s job.
/// </summary>
public static class UpdateCheck
{
    public const string Repo = "AzraelGodKing/SteamCMDHelper";
    public const string LatestUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";

    static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(10));

    /// <summary>A client that identifies this program to GitHub (its API refuses requests without a User-Agent).</summary>
    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SteamCmdHelper", CurrentVersion));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>The running program's version, e.g. "1.0.0".</summary>
    public static string CurrentVersion =>
        (typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>The latest release when it is newer than <paramref name="current"/>; null when up to date or it cannot be read.</summary>
    public static async Task<ReleaseInfo?> FindNewerAsync(string current, CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(LatestUrl, ct);
            var latest = Parse(json);
            return latest != null && IsNewer(latest.Version, current) ? latest : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// Reads a GitHub "latest release" answer. Drafts and pre-releases are ignored. The download is the standalone
    /// exe ("...-win-x64.exe") when the release has one, else the first .exe or .zip.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) return null;
        if (Flag(r, "draft") || Flag(r, "prerelease")) return null;
        var tag = Str(r, "tag_name");
        if (tag == null) return null;
        var version = tag.TrimStart('v', 'V');
        if (Numbers(version) == null) return null;

        var assets = r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => (name: Str(x, "name") ?? "", url: Str(x, "browser_download_url"))).Where(x => x.url != null).ToList()
            : new();
        string? Ending(string end) => assets.FirstOrDefault(x => x.name.EndsWith(end, StringComparison.OrdinalIgnoreCase)).url;
        var exe = Ending("-win-x64.exe");
        var zip = Ending("-framework-dependent.zip");
        var sums = assets.FirstOrDefault(x => x.name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)).url;
        var download = exe ?? Ending(".exe") ?? Ending(".zip");
        return new ReleaseInfo(version, Str(r, "html_url") ?? ReleasesPage, download, exe, zip, sums);
    }

    /// <summary>True when <paramref name="latest"/> is a higher version than <paramref name="current"/> (1.10.0 &gt; 1.9.0).</summary>
    public static bool IsNewer(string latest, string current)
    {
        var a = Numbers(latest);
        var b = Numbers(current);
        if (a == null || b == null) return false;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>The numeric part of a version ("1.2.0-beta" = 1, 2, 0), or null when it does not start with one.</summary>
    static int[]? Numbers(string v)
    {
        var core = v.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length == 0 || parts.Length > 4) return null;
        var n = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out n[i])) return null;
        return n;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
