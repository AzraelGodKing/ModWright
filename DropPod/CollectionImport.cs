using System.Text.Json;

namespace SteamCmdHelper.DropPod;

/// <summary>One entry of a Steam collection: an item, or another collection inside it.</summary>
public sealed record CollectionChild(string Id, int Order, bool IsCollection);

/// <summary>
/// Loads a Steam Workshop collection as a modlist: the other direction of <see cref="SteamCollections"/>, and how most
/// RimWorld packs are shared. Steam's public GetCollectionDetails API lists a collection's items without a key or a
/// login; collections inside it are followed one level deep.
/// </summary>
public static class CollectionImport
{
    const string Url = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>The children of each collection in GetCollectionDetails' answer, by collection id, in their sort order.</summary>
    public static Dictionary<string, List<CollectionChild>> Parse(string json)
    {
        var all = new Dictionary<string, List<CollectionChild>>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var resp) || !resp.TryGetProperty("collectiondetails", out var list) || list.ValueKind != JsonValueKind.Array)
            return all;
        foreach (var c in list.EnumerateArray())
        {
            var id = Str(c, "publishedfileid");
            if (id == null || Num(c, "result") != 1) continue;
            var kids = new List<CollectionChild>();
            if (c.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                foreach (var k in ch.EnumerateArray())
                    if (Str(k, "publishedfileid") is { } kid) kids.Add(new CollectionChild(kid, (int)Num(k, "sortorder"), Num(k, "filetype") == 2));
            all[id] = kids.OrderBy(k => k.Order).ToList();
        }
        return all;
    }

    public static FormUrlEncodedContent Body(IReadOnlyList<string> ids)
    {
        var fields = new List<KeyValuePair<string, string>> { new("collectioncount", ids.Count.ToString()) };
        for (var i = 0; i < ids.Count; i++) fields.Add(new($"publishedfileids[{i}]", ids[i]));
        return new FormUrlEncodedContent(fields);
    }

    /// <summary>
    /// The Workshop item ids in the collection, in its order, each once; collections inside it are opened in place. Throws
    /// <see cref="IOException"/> when Steam can't be reached or doesn't know the collection.
    /// </summary>
    public static async Task<List<string>> ItemsAsync(string collectionId, CancellationToken ct = default, Func<IReadOnlyList<string>, CancellationToken, Task<string>>? fetch = null)
    {
        fetch ??= FetchAsync;
        var top = Parse(await fetch(new[] { collectionId }, ct));
        if (!top.TryGetValue(collectionId, out var kids)) throw new IOException($"Steam has no public collection {collectionId} (it may be private, or not a collection).");
        var inner = kids.Where(k => k.IsCollection).Select(k => k.Id).Distinct().ToList();
        var nested = inner.Count > 0 ? Parse(await fetch(inner, ct)) : new();
        var items = new List<string>();
        foreach (var k in kids)
        {
            if (!k.IsCollection) { if (!items.Contains(k.Id)) items.Add(k.Id); continue; }
            if (nested.TryGetValue(k.Id, out var sub))
                foreach (var s in sub.Where(s => !s.IsCollection)) if (!items.Contains(s.Id)) items.Add(s.Id);
        }
        return items;
    }

    static async Task<string> FetchAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.PostAsync(Url, Body(ids), ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new IOException("Steam could not be reached: " + e.Message, e);
        }
    }

    /// <summary>
    /// A modlist of the collection's items: installed ones by their package id, the rest by Workshop id until they are
    /// downloaded (<see cref="Modlist.Match"/> finds them by Workshop id then). Titles come from <paramref name="titles"/>.
    /// </summary>
    public static Modlist ToModlist(string name, string appId, IReadOnlyList<string> items, LibraryScan? scan, IReadOnlyDictionary<string, string> titles)
    {
        var byWorkshop = new Dictionary<string, ModManifest>();
        if (scan != null) foreach (var m in scan.ById().Values) if (m.WorkshopId != null) byWorkshop.TryAdd(m.WorkshopId, m);
        var list = new Modlist { Name = name, AppId = appId, GameVersion = scan?.Install.Version ?? "", Saved = DateTime.UtcNow };
        foreach (var id in items)
            list.Mods.Add(byWorkshop.TryGetValue(id, out var m)
                ? new ModlistEntry(m.Id.Length > 0 ? m.Id : m.Key, m.DisplayName, id, m.ModVersion.Length > 0 ? m.ModVersion : null)
                : new ModlistEntry("steam." + id, titles.GetValueOrDefault(id), id));
        return list;
    }

    static long Num(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? 0
        : v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n
        : v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s) ? s : 0;

    static string? Str(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null : v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;
}
