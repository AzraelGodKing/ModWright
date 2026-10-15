using System.Text;
using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>
/// Changes to a mod's Workshop page that go out with its next upload. Only the fields that are set are sent; the rest
/// stay as they are on Steam. SteamCMD's build file can carry the title, description and visibility, but not tags.
/// </summary>
public sealed class WorkshopPageEdit
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>Steam's visibility number: 0 public, 1 friends only, 2 private (hidden), 3 unlisted.</summary>
    public int? Visibility { get; set; }

    public bool IsEmpty => Title == null && Description == null && Visibility == null;

    /// <summary>What will change, e.g. "title, visibility (Public)".</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Title != null) parts.Add("title");
        if (Description != null) parts.Add("description");
        if (Visibility is { } v) parts.Add($"visibility ({WorkshopPage.VisibilityName(v)})");
        return string.Join(", ", parts);
    }
}

/// <summary>A Workshop item's page as Steam shows it now.</summary>
public sealed record WorkshopDetails(string Title, string Description, int? Visibility, string[] Tags);

public static class WorkshopPage
{
    /// <summary>Steam's limits for an item's title and description.</summary>
    public const int MaxTitle = 128, MaxDescription = 8000;

    /// <summary>Visibility names in Steam's order (the index is the number SteamCMD takes).</summary>
    public static readonly string[] VisibilityNames = { "Public", "Friends only", "Private", "Unlisted" };

    public const int Private = 2;

    public static string VisibilityName(int v) => v >= 0 && v < VisibilityNames.Length ? VisibilityNames[v] : "unknown";

    /// <summary>Null when the edit can be sent, else why not.</summary>
    public static string? Validate(WorkshopPageEdit e)
    {
        if (e.Title != null && e.Title.Trim().Length == 0) return "The title cannot be empty.";
        if (e.Title != null && e.Title.Length > MaxTitle) return $"The title is {e.Title.Length} characters; Steam allows {MaxTitle}.";
        if (e.Description != null && e.Description.Length > MaxDescription) return $"The description is {e.Description.Length} characters; Steam allows {MaxDescription}.";
        if (e.Visibility is { } v && (v < 0 || v >= VisibilityNames.Length)) return "Unknown visibility.";
        return null;
    }

    /// <summary>The build-file lines for the fields that are set, each starting on a new line.</summary>
    public static string VdfLines(string? title, string? description, int? visibility)
    {
        var sb = new StringBuilder();
        if (title != null) sb.Append($"\n    \"title\"           \"{Publisher.Esc(title.Trim())}\"");
        if (description != null) sb.Append($"\n    \"description\"     \"{Publisher.Esc(description)}\"");
        if (visibility != null) sb.Append($"\n    \"visibility\"      \"{visibility}\"");
        return sb.ToString();
    }

    // ------------------------------------------------------------ the page on Steam now

    const string DetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Reads the item's current page from Steam. Null when it cannot (offline, or a private item Steam won't show).</summary>
    public static async Task<WorkshopDetails?> FetchAsync(string workshopId, CancellationToken ct = default)
    {
        try
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["itemcount"] = "1", ["publishedfileids[0]"] = workshopId });
            using var res = await Http.PostAsync(DetailsUrl, body, ct);
            if (!res.IsSuccessStatusCode) return null;
            return ParseDetails(await res.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>Reads a GetPublishedFileDetails answer. Null unless Steam returned the item (result 1).</summary>
    public static WorkshopDetails? ParseDetails(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var r)
                || !r.TryGetProperty("publishedfiledetails", out var list) || list.ValueKind != JsonValueKind.Array) return null;
            var item = list.EnumerateArray().FirstOrDefault();
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("result", out var ok) || !ok.TryGetInt32(out var code) || code != 1) return null;
            var tags = item.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("tag", out var n) ? n.GetString() : null)
                    .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToArray()
                : Array.Empty<string>();
            int? vis = item.TryGetProperty("visibility", out var v) && v.TryGetInt32(out var vi) ? vi : null;
            return new WorkshopDetails(Str(item, "title"), Str(item, "description"), vis, tags);
        }
        catch (JsonException) { return null; }
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // ------------------------------------------------------------ pending edits, kept until the next upload

    static string StorePath => Path.Combine(AppSettings.Folder, "workshop_pages.json");

    static readonly object Gate = new();

    /// <summary>Edits are kept per mod folder (folders are case-insensitive on Windows).</summary>
    public static string Key(string modDir) =>
        Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

    static Dictionary<string, WorkshopPageEdit> LoadAll(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<Dictionary<string, WorkshopPageEdit>>(File.ReadAllText(path)) ?? new();
        }
        catch { }
        return new();
    }

    static void SaveAll(string path, Dictionary<string, WorkshopPageEdit> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The changes waiting for <paramref name="modDir"/>'s next upload, or null.</summary>
    public static WorkshopPageEdit? Get(string modDir, string? path = null)
    {
        lock (Gate) return LoadAll(path ?? StorePath).TryGetValue(Key(modDir), out var e) && !e.IsEmpty ? e : null;
    }

    /// <summary>Saves the changes for the next upload; an empty edit clears them.</summary>
    public static void Set(string modDir, WorkshopPageEdit edit, string? path = null)
    {
        lock (Gate)
        {
            var p = path ?? StorePath;
            var all = LoadAll(p);
            if (edit.IsEmpty) all.Remove(Key(modDir)); else all[Key(modDir)] = edit;
            SaveAll(p, all);
        }
    }

    /// <summary>Forgets the changes, e.g. after they were uploaded.</summary>
    public static void Clear(string modDir, string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? StorePath;
                var all = LoadAll(p);
                if (all.Remove(Key(modDir))) SaveAll(p, all);
            }
            catch { /* a convenience; ignore write failures */ }
        }
    }
}
