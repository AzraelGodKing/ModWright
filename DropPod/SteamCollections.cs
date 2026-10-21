using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod;

/// <summary>Who can see a Workshop item, with Steam's own numbers.</summary>
public enum CollectionVisibility { Public = 0, FriendsOnly = 1, Private = 2, Unlisted = 3 }

/// <summary>A Steam Workshop collection to create, or to make match a list of items.</summary>
public sealed class CollectionRequest
{
    public string AppId { get; set; } = "";
    /// <summary>The collection to update; null creates a new one.</summary>
    public string? CollectionId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public CollectionVisibility Visibility { get; set; } = CollectionVisibility.Public;
    /// <summary>When updating: also send the title, description and visibility (a new collection always gets them).</summary>
    public bool UpdatePage { get; set; }
    /// <summary>The Workshop items the collection should hold, in order. Everything else in it is taken out.</summary>
    public string[] Items { get; set; } = Array.Empty<string>();
}

/// <summary>What the Steam client did with a <see cref="CollectionRequest"/>.</summary>
public sealed class CollectionResult
{
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    /// <summary>The collection's id; set as soon as it exists, even when a later step failed.</summary>
    public string? CollectionId { get; set; }
    public bool Created { get; set; }
    public int Added { get; set; }
    public int Removed { get; set; }

    public static CollectionResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Puts the active mods in a Steam Workshop collection, so friends can subscribe to the whole list in one click. Only
/// Workshop items can be in a collection: the base game, DLC and local mods without a Workshop id are left out, as are
/// inactive mods. Updating a collection makes it match exactly, taking out what is no longer active. SteamCMD cannot
/// make collections, so this goes through the Steam client in a child copy of the program (SteamworksCollections.cs),
/// like Workshop tags; the planning is here so it can be tested.
/// </summary>
public static class SteamCollections
{
    /// <summary>The argument that makes the program apply one request file and exit (run in a child process).</summary>
    public const string ChildArg = "--steam-collection";

    const string ResultPrefix = "RESULT ";

    static readonly Regex Digits = new(@"^\d{1,20}$");
    static readonly Regex IdInUrl = new(@"[?&]id=(\d{1,20})\b");

    /// <summary>
    /// The Workshop ids of the active mods, in load order and each once, and the names of the active mods that can't
    /// be in a collection (no Workshop id, official content, or not installed).
    /// </summary>
    public static (List<string> Items, List<string> LeftOut) Contents(LibraryScan scan, IReadOnlyList<string> active, IGameModule game)
    {
        var byId = scan.ById();
        var items = new List<string>();
        var left = new List<string>();
        foreach (var key in active.Select(game.ActiveKey).Distinct())
        {
            if (!byId.TryGetValue(key, out var m)) { left.Add(key); continue; }
            if (m.Source == ModSource.Official) continue; // the game and its DLC are not Workshop items; nobody misses them
            if (m.WorkshopId is { } id && Digits.IsMatch(id)) { if (!items.Contains(id)) items.Add(id); }
            else left.Add(m.DisplayName);
        }
        return (items, left);
    }

    /// <summary>What to add to a collection holding <paramref name="current"/> so it holds exactly <paramref name="wanted"/>, and what to take out.</summary>
    public static (List<string> Add, List<string> Remove) Plan(IEnumerable<string> current, IReadOnlyList<string> wanted)
    {
        var have = current.ToList();
        var haveSet = have.ToHashSet();
        var wantSet = wanted.ToHashSet();
        return (wanted.Distinct().Where(id => !haveSet.Contains(id)).ToList(), have.Distinct().Where(id => !wantSet.Contains(id)).ToList());
    }

    /// <summary>A collection id from what the player typed: the number, or a Steam link with "?id=". Null when there is none.</summary>
    public static string? ParseId(string? text)
    {
        var t = (text ?? "").Trim();
        if (Digits.IsMatch(t)) return t.TrimStart('0').Length > 0 ? t : null;
        var m = IdInUrl.Match(t);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string Url(string id) => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;

    /// <summary>The settings key for the collection a modlist was last sent to.</summary>
    public static string Key(string appId, string modlist) => appId + "|" + modlist.Trim();

    /// <summary>
    /// Creates or updates the collection through the Steam client. Needs Steam running and signed in with an account
    /// that owns the game (and, to update, owns the collection).
    /// </summary>
    public static async Task<CollectionResult> ApplyOnSteamAsync(CollectionRequest req, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return CollectionResult.Fail("This needs the Steam client, which only works on Windows here.");
        var file = Path.Combine(Path.GetTempPath(), $"steamcmdhelper-collection-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(req), ct);
            var (output, error) = await SteamClientChild.RunAsync(new[] { ChildArg, file }, TimeSpan.FromMinutes(10), ct);
            return error != null ? CollectionResult.Fail(error) : ParseChild(output!);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static string ChildLine(CollectionResult r) => ResultPrefix + JsonSerializer.Serialize(r);

    public static CollectionResult ParseChild(string output)
    {
        var line = output.Replace("\r", "").Split('\n').LastOrDefault(l => l.StartsWith(ResultPrefix, StringComparison.Ordinal));
        if (line == null) return CollectionResult.Fail("The Steam client step stopped without saying why" + (output.Trim().Length > 0 ? ": " + output.Trim() : "."));
        try { return JsonSerializer.Deserialize<CollectionResult>(line[ResultPrefix.Length..]) ?? CollectionResult.Fail("The Steam client step gave no answer."); }
        catch (JsonException e) { return CollectionResult.Fail("The Steam client step's answer could not be read: " + e.Message); }
    }

    /// <summary>One line for the player about what happened.</summary>
    public static string Describe(CollectionResult r)
    {
        if (!r.Ok) return (r.CollectionId != null ? $"Collection {r.CollectionId}: " : "") + r.Error;
        var changes = r.Added + r.Removed == 0 ? "it already matched" : $"added {r.Added}, took out {r.Removed}";
        return (r.Created ? "Created the collection" : "Updated the collection") + $" ({changes}): {Url(r.CollectionId ?? "")}";
    }
}
