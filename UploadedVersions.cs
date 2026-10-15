using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Works out which version of a mod is already on Steam. Steam does not store a version, so two sources are used:
/// the newest change note on the item's public changelog page (Add Changenote style notes, which this app also sends
/// for RimWorld, start with the version), and a local record of what this app uploaded from this PC.
/// </summary>
public static class UploadedVersions
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static readonly Regex FirstNote = new(@"class=""changelog headline"".*?<p id=""\d+"">(.*?)</p>", RegexOptions.Singleline);
    static readonly Regex LeadingVersion = new(@"^\s*[vV]?(\d+(?:\.\d+){1,3})\b");

    /// <summary>The version at the start of the newest change note on a Steam changelog page, or null.</summary>
    public static string? FromChangelogPage(string html)
    {
        var note = FirstNote.Match(html);
        if (!note.Success) return null;
        var text = WebUtility.HtmlDecode(Regex.Replace(note.Groups[1].Value, "<[^>]+>", "\n"));
        var v = LeadingVersion.Match(text);
        return v.Success ? v.Groups[1].Value : null;
    }

    // Steam answers "too many requests" quickly, so each item's page is read at most once every few minutes.
    static readonly Dictionary<string, (string? version, DateTime at)> Cache = new();
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    /// <summary>Reads the item's public changelog page. Null when it cannot be read (private item, offline, rate limited) or has no version.</summary>
    public static async Task<string?> FetchAsync(string workshopId, CancellationToken ct = default)
    {
        lock (Cache)
            if (Cache.TryGetValue(workshopId, out var hit) && DateTime.UtcNow - hit.at < CacheFor) return hit.version;
        string? v;
        try
        {
            var html = await Http.GetStringAsync($"https://steamcommunity.com/sharedfiles/filedetails/changelog/{workshopId}", ct);
            v = FromChangelogPage(html);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { v = null; }
        lock (Cache) Cache[workshopId] = (v, DateTime.UtcNow);
        return v;
    }

    /// <summary>Forgets the cached page, e.g. after uploading a new version.</summary>
    public static void Forget(string workshopId) { lock (Cache) Cache.Remove(workshopId); }

    /// <summary>
    /// Picks what the "Uploaded version" column shows. The version in Steam's newest change note wins. Otherwise
    /// this PC's last upload is used, unless Steam says the item was updated after it (an upload from somewhere
    /// else), in which case the version is honestly unknown.
    /// </summary>
    public static (string text, string note) Resolve(string? fromSteamNote, WorkshopItem? live, UploadEntry? last, string? recorded)
    {
        if (fromSteamNote != null) return (fromSteamNote, "Uploaded version: read from the newest change note on Steam.");
        var local = last?.Version ?? recorded;
        if (local == null)
            return ("?", "Uploaded version: unknown. Steam's newest change note does not start with a version, and this PC has no record of uploading it.");
        if (last != null && live?.Updated is { } u && u > last.Time + UpdatedSlack)
            return ("?", $"Uploaded version: unknown. Steam shows an update on {u.ToLocalTime():d MMM yyyy HH:mm}, after this PC uploaded {last.Version} on {last.Time.ToLocalTime():d MMM yyyy HH:mm}.");
        return (local, last != null
            ? $"Uploaded version: uploaded from this PC on {last.Time.ToLocalTime():d MMM yyyy HH:mm}."
            : "Uploaded version: last uploaded from this PC.");
    }

    /// <summary>Steam stamps the update when the upload finishes, a little after this PC records it.</summary>
    public static readonly TimeSpan UpdatedSlack = TimeSpan.FromMinutes(15);

    // ------------------------------------------------------------ local record of uploads made from this PC

    static string RecordPath => Path.Combine(AppSettings.Folder, "uploaded.json");

    static readonly object Gate = new();

    /// <summary>Workshop id to the version last uploaded from this PC.</summary>
    public static Dictionary<string, string> LoadRecord(string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? RecordPath;
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }
    }

    public static void Remember(string workshopId, string version, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(workshopId) || string.IsNullOrWhiteSpace(version)) return;
        if (path == null) Forget(workshopId);
        lock (Gate)
        {
            try
            {
                var p = path ?? RecordPath;
                var all = LoadRecord(p);
                all[workshopId] = version;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* a convenience; ignore write failures */ }
        }
    }
}
