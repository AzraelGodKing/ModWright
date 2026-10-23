using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod.SunHaven;

/// <summary>A package's newest version on Thunderstore.</summary>
public sealed record ThunderstorePackage(string Id, string Version, string Download, string Page, IReadOnlyList<string> Dependencies);

/// <summary>An installed plugin with a newer Thunderstore version.</summary>
public sealed record ThunderstoreUpdate(ModManifest Mod, ThunderstorePackage Package);

/// <summary>
/// Getting Sun Haven mods from Thunderstore the way r2modman lays them out: each package in BepInEx/plugins/Author-Name
/// (its "plugins" folder flattened into it), its config files in BepInEx/config (never over the player's own), its
/// patchers in BepInEx/patchers/Author-Name. Missing dependencies come along; BepInEx itself is never touched.
/// </summary>
public static class ThunderstoreMods
{
    public const string Community = "sun-haven";
    const string Api = "https://thunderstore.io/api/experimental/package/";
    static readonly Regex Dependency = new(@"^(?<id>[A-Za-z0-9_]+-[A-Za-z0-9_]+)-\d+\.\d+\.\d+$");
    static readonly Regex PackageId = new(@"^[A-Za-z0-9_]+-[A-Za-z0-9_]+$");

    /// <summary>"Author-Name" from a dependency string "Author-Name-1.2.3", or null.</summary>
    public static string? DependencyId(string? dependency)
    {
        var m = Dependency.Match((dependency ?? "").Trim());
        return m.Success ? m.Groups["id"].Value : null;
    }

    /// <summary>True for an id Thunderstore could have ("Author-Name").</summary>
    public static bool IsPackageId(string id) => PackageId.IsMatch(id);

    public static string PageUrl(string id)
    {
        var dash = id.IndexOf('-');
        return dash <= 0 ? Thunderstore.Site : Thunderstore.PackageUrl(Community, id[..dash], id[(dash + 1)..]);
    }

    /// <summary>Reads the experimental API's package answer; null when it has no usable latest version.</summary>
    public static ThunderstorePackage? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("latest", out var latest) || latest.ValueKind != JsonValueKind.Object) return null;
        static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var ns = Str(latest, "namespace") ?? Str(r, "namespace");
        var name = Str(latest, "name") ?? Str(r, "name");
        var version = Str(latest, "version_number");
        var download = Str(latest, "download_url");
        if (ns == null || name == null || version == null || download == null) return null;
        var deps = latest.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? DependencyId(x.GetString()) : null).OfType<string>().ToList()
            : new List<string>();
        return new ThunderstorePackage($"{ns}-{name}", version, download, Str(r, "package_url") ?? PageUrl($"{ns}-{name}"), deps);
    }

    public static async Task<ThunderstorePackage?> FetchAsync(string id, HttpClient http, CancellationToken ct)
    {
        var dash = id.IndexOf('-');
        if (dash <= 0) return null;
        try { return Parse(await http.GetStringAsync($"{Api}{Uri.EscapeDataString(id[..dash])}/{Uri.EscapeDataString(id[(dash + 1)..])}/", ct)); }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    /// <summary>Installed plugins that came from Thunderstore: a folder named like a package, with manifest.json and a version.</summary>
    public static List<ModManifest> Candidates(IEnumerable<ModManifest> mods) =>
        mods.Where(m => Directory.Exists(m.Folder) && IsPackageId(m.Id) && m.ModVersion.Length > 0 && SafeFiles.Child(m.Folder, "manifest.json") != null)
            .DistinctBy(m => m.Key).ToList();

    /// <summary>Asks Thunderstore for each candidate's newest version (at most 100).</summary>
    public static async Task<List<ThunderstoreUpdate>> CheckAsync(IEnumerable<ModManifest> candidates, HttpClient http, CancellationToken ct)
    {
        var list = new List<ThunderstoreUpdate>();
        foreach (var m in candidates.Take(100))
        {
            ct.ThrowIfCancellationRequested();
            if (await FetchAsync(m.Id, http, ct) is { } p && UpdateCheck.IsNewer(p.Version, m.ModVersion)) list.Add(new ThunderstoreUpdate(m, p));
        }
        return list;
    }

    /// <summary>
    /// Sorts an unpacked package into <paramref name="staged"/> (what goes in BepInEx/plugins/Author-Name) and copies its
    /// config and patchers into <paramref name="bepinex"/>. Returns the dependencies its manifest names.
    /// </summary>
    public static List<string> Place(string unpacked, string staged, string bepinex, string id)
    {
        Directory.CreateDirectory(staged);
        foreach (var file in Directory.GetFiles(unpacked, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(unpacked, file).Replace('\\', '/');
            if (rel.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) rel = rel["BepInEx/".Length..];
            string? target;
            if (Strip(rel, "plugins/") is { } p) target = Path.Combine(staged, p);
            else if (Strip(rel, "config/") is { } c) { target = Path.Combine(bepinex, "config", c); if (File.Exists(target)) continue; }
            else if (Strip(rel, "patchers/") is { } pa) target = Path.Combine(bepinex, "patchers", id, pa);
            else if (Strip(rel, "core/") != null) continue; // BepInEx's own files; the player's BepInEx stays as it is
            else target = Path.Combine(staged, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        var manifest = SafeFiles.Child(staged, "manifest.json");
        if (manifest == null) return new();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest), new JsonDocumentOptions { AllowTrailingCommas = true });
            return doc.RootElement.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Array
                ? d.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? DependencyId(x.GetString()) : null).OfType<string>().ToList()
                : new();
        }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    static string? Strip(string rel, string prefix) =>
        rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && rel.Length > prefix.Length ? rel[prefix.Length..] : null;

    /// <summary>
    /// Installs <paramref name="ids"/> and whatever they need that isn't in <paramref name="installed"/> (keys), into
    /// BepInEx/plugins. Returns the ids installed and the ones Thunderstore didn't have.
    /// </summary>
    public static async Task<(List<string> Installed, List<string> NotFound)> InstallAsync(IEnumerable<string> ids, ISet<string> installed, GameInstall install, HttpClient http, CancellationToken ct)
    {
        var done = new List<string>();
        var notFound = new List<string>();
        var queue = new Queue<string>(ids);
        var seen = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0 && done.Count < 50)
        {
            var id = queue.Dequeue();
            if (ModManifest.KeyOf(id).StartsWith(SunHavenModule.BepInExPack, StringComparison.Ordinal) || !seen.Add(ModManifest.KeyOf(id))) continue;
            var p = await FetchAsync(id, http, ct);
            if (p == null) { notFound.Add(id); continue; }
            var target = Path.Combine(SunHavenModule.PluginsFolder(install), p.Id);
            await WithPackageAsync(p, http, SunHavenModule.BepInExFolder(install), ct, staged =>
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                WorkshopDownload.CopyTo(staged, target);
            }, queue);
            done.Add(p.Id);
        }
        return (done, notFound);
    }

    /// <summary>Puts the newer version in place of the installed one (zipped into <paramref name="backups"/> first). Returns the backup's path.</summary>
    public static async Task<string> UpdateAsync(ThunderstoreUpdate u, GameInstall install, HttpClient http, string backups, CancellationToken ct)
    {
        string backup = "";
        await WithPackageAsync(u.Package, http, SunHavenModule.BepInExFolder(install), ct, staged => backup = GitHubModUpdates.Replace(u.Mod, staged, backups), null);
        return backup;
    }

    static async Task WithPackageAsync(ThunderstorePackage p, HttpClient http, string bepinex, CancellationToken ct, Action<string> use, Queue<string>? dependencies)
    {
        var work = Path.Combine(Path.GetTempPath(), "modwright-ts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "package.zip");
            await using (var s = await http.GetStreamAsync(p.Download, ct))
            await using (var f = File.Create(zip))
                await s.CopyToAsync(f, ct);
            var unpacked = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zip, unpacked); // refuses entries that would land outside the folder
            var staged = Path.Combine(work, "staged");
            var deps = Place(unpacked, staged, bepinex, p.Id);
            use(staged);
            if (dependencies != null) foreach (var d in deps.Concat(p.Dependencies).Distinct(StringComparer.OrdinalIgnoreCase)) dependencies.Enqueue(d);
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }
}
