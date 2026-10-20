using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>Where and how a mods folder publishes to Thunderstore.</summary>
public sealed record ThunderstoreTarget(string Team, string Community, string[] Categories, bool Nsfw = false);

/// <summary>
/// Publishing the same mod to Thunderstore (the mod site r2modman and the Thunderstore app install from, for BepInEx
/// games), next to the Steam Workshop. A mod takes part when its folder has Thunderstore's manifest.json; the package
/// is the same one that goes to Steam, with the version to upload written into the manifest. The upload follows
/// Thunderstore's own CLI: start a user-media upload, send the zip in the parts it asks for, finish it, then submit
/// the package to the team and community.
/// </summary>
public static class Thunderstore
{
    public const string ManifestFile = "manifest.json";
    public const string Site = "https://thunderstore.io/";

    static readonly Regex Name = new(@"^[a-zA-Z0-9_]+$");
    static readonly Regex Version = new(@"^\d+\.\d+\.\d+$");
    static readonly Regex Dependency = new(@"^[a-zA-Z0-9_]+-[a-zA-Z0-9_]+-\d+\.\d+\.\d+$");

    /// <summary>Whether the mod has a Thunderstore manifest.json at its root.</summary>
    public static bool Has(string modDir) => File.Exists(Path.Combine(modDir, ManifestFile));

    /// <summary>The manifest's package name, or null when it has none.</summary>
    public static string? NameOf(string modDir)
    {
        try { return JsonNode.Parse(File.ReadAllText(Path.Combine(modDir, ManifestFile)))?["name"]?.GetValue<string>(); }
        catch { return null; }
    }

    /// <summary>The version Thunderstore gets: the mod's version when it is Major.Minor.Patch, else the manifest's own.</summary>
    public static string VersionFor(string modVersion, JsonNode manifest)
    {
        var v = modVersion.Trim().TrimStart('v', 'V');
        if (Version.IsMatch(v)) return v;
        // 1.2 becomes 1.2.0; 1.2.3.4 keeps its first three parts.
        var parts = v.Split('.');
        if (parts.Length is >= 2 and <= 4 && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
            return string.Join('.', parts.Concat(new[] { "0", "0" }).Take(3));
        return manifest["version_number"]?.ToString() ?? "";
    }

    /// <summary>What Thunderstore would refuse about the package in <paramref name="dir"/> (with the version to send).</summary>
    public static List<string> Check(string dir, string version)
    {
        var problems = new List<string>();
        JsonNode? m;
        try { m = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, ManifestFile))); }
        catch (FileNotFoundException) { return new() { "has no manifest.json" }; }
        catch (Exception e) { return new() { "manifest.json is not valid JSON: " + e.Message }; }
        if (m is not JsonObject o) return new() { "manifest.json is not a JSON object" };
        var name = o["name"]?.ToString() ?? "";
        if (!Name.IsMatch(name)) problems.Add($"manifest.json name \"{name}\" must be letters, digits and _ only");
        if (!Version.IsMatch(version)) problems.Add($"version \"{version}\" must be Major.Minor.Patch, such as 1.2.0");
        var description = o["description"]?.ToString() ?? "";
        if (description.Length > 250) problems.Add($"manifest.json description is {description.Length} characters; Thunderstore allows 250");
        if (o["website_url"] == null) problems.Add("manifest.json needs a website_url (it may be \"\")");
        if (o["dependencies"] is not JsonArray deps) problems.Add("manifest.json needs a dependencies list (it may be [])");
        else
            foreach (var d in deps)
                if (d?.ToString() is not { } s || !Dependency.IsMatch(s))
                    problems.Add($"dependency \"{d}\" must look like Author-Name-1.0.0");
        var icon = Path.Combine(dir, "icon.png");
        if (!File.Exists(icon)) problems.Add("has no icon.png (256 x 256)");
        else if (PngSize(icon) is not (256, 256)) problems.Add("icon.png must be a 256 x 256 PNG" + (PngSize(icon) is var (w, h) ? $" (it is {w} x {h})" : ""));
        if (!File.Exists(Path.Combine(dir, "README.md"))) problems.Add("has no README.md");
        return problems;
    }

    /// <summary>A PNG's width and height from its header, or null when it is not a PNG.</summary>
    public static (int W, int H)? PngSize(string path)
    {
        try
        {
            var b = new byte[24];
            using var f = File.OpenRead(path);
            if (f.Read(b, 0, 24) < 24 || b[0] != 137 || b[1] != 80 || b[2] != 78 || b[3] != 71) return null;
            static int Be(byte[] x, int i) => (x[i] << 24) | (x[i + 1] << 16) | (x[i + 2] << 8) | x[i + 3];
            return (Be(b, 16), Be(b, 20));
        }
        catch { return null; }
    }

    /// <summary>Zips the package in <paramref name="dir"/> for Thunderstore, with <paramref name="version"/> as the manifest's version_number.</summary>
    public static void Zip(string dir, string zipPath, string version)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (rel.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase))
            {
                var m = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                m["version_number"] = version;
                var entry = zip.CreateEntry(ManifestFile, CompressionLevel.Optimal);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(m.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            else zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
        }
    }

    public static string PackageUrl(string community, string team, string name) =>
        $"{Site}c/{Uri.EscapeDataString(community)}/p/{Uri.EscapeDataString(team)}/{Uri.EscapeDataString(name)}/";

    /// <summary>The newest version of the package on Thunderstore, or null when it is not there yet (or the site can't say).</summary>
    public static async Task<string?> LatestAsync(HttpClient http, string team, string name, CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync($"{Site}api/experimental/package/{Uri.EscapeDataString(team)}/{Uri.EscapeDataString(name)}/", ct);
            if (!r.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await r.Content.ReadAsStringAsync(ct))?["latest"]?["version_number"]?.ToString();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    /// <summary>Uploads the zip and submits it. Returns null when it went through, else what went wrong.</summary>
    public static async Task<string?> PublishAsync(HttpClient http, string token, ThunderstoreTarget target, string zipPath, CancellationToken ct, Action<string>? say = null)
    {
        HttpRequestMessage Api(HttpMethod method, string path, object? body = null)
        {
            var req = new HttpRequestMessage(method, Site + "api/experimental/" + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return req;
        }
        static async Task<string> Why(HttpResponseMessage r, string step) =>
            $"Thunderstore refused {step} ({(int)r.StatusCode} {r.ReasonPhrase}): " + Short(await r.Content.ReadAsStringAsync());

        var size = new FileInfo(zipPath).Length;
        string uuid;
        JsonArray parts;
        using (var r = await http.SendAsync(Api(HttpMethod.Post, "usermedia/initiate-upload/", new { filename = Path.GetFileName(zipPath), file_size_bytes = size }), ct))
        {
            if (r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.Forbidden)
                return "Thunderstore did not accept the token. Make a service account token for the team on thunderstore.io (Teams > Service accounts) and paste it in Options.";
            if (r.StatusCode != HttpStatusCode.Created) return await Why(r, "the upload");
            var data = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct));
            uuid = data?["user_media"]?["uuid"]?.ToString() ?? "";
            parts = data?["upload_urls"] as JsonArray ?? new();
            if (uuid.Length == 0 || parts.Count == 0) return "Thunderstore gave no place to upload to.";
        }
        say?.Invoke($"Sending {size / 1024.0 / 1024.0:0.##} MB to Thunderstore in {parts.Count} part{(parts.Count == 1 ? "" : "s")}...");

        var done = new List<object>();
        try
        {
            foreach (var p in parts)
            {
                var number = p!["part_number"]!.GetValue<int>();
                var offset = p["offset"]!.GetValue<long>();
                var length = p["length"]!.GetValue<int>();
                var chunk = new byte[length];
                using (var f = File.OpenRead(zipPath))
                {
                    f.Seek(offset, SeekOrigin.Begin);
                    await f.ReadExactlyAsync(chunk, ct);
                }
                using var put = new HttpRequestMessage(HttpMethod.Put, p["url"]!.ToString()) { Content = new ByteArrayContent(chunk) };
                put.Content.Headers.ContentMD5 = MD5.HashData(chunk);
                put.Content.Headers.ContentLength = length;
                using var r = await http.SendAsync(put, ct);
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException(await Why(r, $"part {number} of the upload"));
                var etag = r.Headers.ETag?.Tag ?? throw new InvalidOperationException($"Thunderstore's storage gave no ETag for part {number}.");
                done.Add(new Dictionary<string, object> { ["ETag"] = etag, ["PartNumber"] = number });
            }
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            try { using var _ = await http.SendAsync(Api(HttpMethod.Post, $"usermedia/{uuid}/abort-upload/"), CancellationToken.None); } catch { }
            if (e is OperationCanceledException) throw;
            return e.Message;
        }

        using (var r = await http.SendAsync(Api(HttpMethod.Post, $"usermedia/{uuid}/finish-upload/", new { parts = done }), ct))
            if (!r.IsSuccessStatusCode) return await Why(r, "finishing the upload");

        var submit = new Dictionary<string, object>
        {
            ["author_name"] = target.Team,
            ["communities"] = new[] { target.Community },
            ["has_nsfw_content"] = target.Nsfw,
            ["upload_uuid"] = uuid,
        };
        if (target.Categories.Length > 0) submit["community_categories"] = new Dictionary<string, string[]> { [target.Community] = target.Categories };
        using (var r = await http.SendAsync(Api(HttpMethod.Post, "submission/submit/", submit), ct))
            if (!r.IsSuccessStatusCode) return await Why(r, "the package");
        return null;
    }

    static string Short(string body)
    {
        body = body.Trim();
        return body.Length > 400 ? body[..400] + "..." : body;
    }

    /// <summary>"a, b ,c" as Thunderstore category slugs.</summary>
    public static string[] Categories(string text) =>
        text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c => c.ToLowerInvariant().Replace(' ', '-')).Distinct().ToArray();
}
