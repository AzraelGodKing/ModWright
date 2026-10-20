using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>What Steam shows publicly for one Workshop item.</summary>
public sealed record WorkshopItem(
    string Id,
    bool Public,
    string Title = "",
    DateTimeOffset? Updated = null,
    int Visibility = 0,
    long Size = 0,
    long Subscribers = 0,
    bool Banned = false,
    long Favorites = 0,
    long LifetimeSubscribers = 0,
    long Views = 0)
{
    /// <summary>Steam's visibility as words. Only public and unlisted items are readable without signing in.</summary>
    public string VisibilityText => Visibility switch { 0 => "public", 1 => "friends only", 2 => "private", 3 => "unlisted", _ => "visibility " + Visibility };

    /// <summary>The short text for the list's "Updated on Steam" column.</summary>
    public string UpdatedText =>
        !Public ? "not public"
        : Banned ? "banned"
        : Updated is { } u ? u.ToLocalTime().ToString("d MMM yyyy") : "?";

    /// <summary>One line for the row tooltip.</summary>
    public string Summary =>
        !Public
            ? "On Steam: not publicly visible (private, friends only, or removed), so its details can't be read."
            : $"On Steam: \"{Title}\", {VisibilityText}{(Banned ? ", BANNED" : "")}"
              + (Updated is { } u ? $", updated {u.ToLocalTime():d MMM yyyy HH:mm}" : "")
              + $", {Subscribers:N0} subscriber{(Subscribers == 1 ? "" : "s")}"
              + (Favorites > 0 ? $", {Favorites:N0} favourite{(Favorites == 1 ? "" : "s")}" : "")
              + (Size > 0 ? $", {Size / 1048576.0:0.##} MB" : "");
}

/// <summary>
/// Reads each item's live details from Steam's public GetPublishedFileDetails API: one request for the whole list,
/// no key and no login. Private items are not returned to anonymous callers, so they come back as not public.
/// </summary>
public static class WorkshopStatus
{
    const string Url = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    const int Batch = 100;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static readonly Dictionary<string, (WorkshopItem item, DateTime at)> Cache = new();
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    /// <summary>The items Steam returned, by id. Ids it did not mention are left out.</summary>
    public static Dictionary<string, WorkshopItem> Parse(string json)
    {
        var all = new Dictionary<string, WorkshopItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var resp) ||
            !resp.TryGetProperty("publishedfiledetails", out var list) || list.ValueKind != JsonValueKind.Array)
            return all;
        foreach (var e in list.EnumerateArray())
        {
            var id = Str(e, "publishedfileid");
            if (string.IsNullOrEmpty(id)) continue;
            // result 1 = OK; anything else (usually 9, "file not found") is how private and removed items look.
            if (Num(e, "result") != 1) { all[id] = new WorkshopItem(id, false); continue; }
            var updated = Num(e, "time_updated");
            all[id] = new WorkshopItem(
                id, true,
                Title: Str(e, "title") ?? "",
                Updated: updated > 0 ? DateTimeOffset.FromUnixTimeSeconds(updated) : null,
                Visibility: (int)Num(e, "visibility"),
                Size: Num(e, "file_size"),
                Subscribers: Num(e, "subscriptions"),
                Banned: Num(e, "banned") != 0,
                Favorites: Num(e, "favorited"),
                LifetimeSubscribers: Num(e, "lifetime_subscriptions"),
                Views: Num(e, "views"));
        }
        return all;
    }

    /// <summary>The form body for one request.</summary>
    public static FormUrlEncodedContent Body(IReadOnlyList<string> ids)
    {
        var fields = new List<KeyValuePair<string, string>> { new("itemcount", ids.Count.ToString()) };
        for (var i = 0; i < ids.Count; i++) fields.Add(new($"publishedfileids[{i}]", ids[i]));
        return new FormUrlEncodedContent(fields);
    }

    /// <summary>Live details for the given ids. Ids missing from the result could not be read (offline, Steam down).</summary>
    public static async Task<Dictionary<string, WorkshopItem>> FetchAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<string, WorkshopItem>();
        var todo = new List<string>();
        lock (Cache)
            foreach (var id in ids.Distinct())
                if (Cache.TryGetValue(id, out var hit) && DateTime.UtcNow - hit.at < CacheFor) result[id] = hit.item;
                else todo.Add(id);

        foreach (var chunk in todo.Chunk(Batch))
        {
            try
            {
                using var resp = await Http.PostAsync(Url, Body(chunk), ct);
                resp.EnsureSuccessStatusCode();
                var got = Parse(await resp.Content.ReadAsStringAsync(ct));
                lock (Cache)
                    foreach (var (id, item) in got) { Cache[id] = (item, DateTime.UtcNow); result[id] = item; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* offline or Steam is down: leave these ids out */ }
        }
        return result;
    }

    /// <summary>Forgets the cached details, e.g. after uploading.</summary>
    public static void Forget(string id) { lock (Cache) Cache.Remove(id); }

    // Steam sends some numbers as strings ("file_size": "12345"), so accept both.
    static long Num(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? 0
        : v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n
        : v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s) ? s
        : 0;

    static string? Str(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.String ? v.GetString()
        : v.ValueKind == JsonValueKind.Number ? v.GetRawText()
        : null;
}
