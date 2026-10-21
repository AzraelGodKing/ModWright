using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod;

/// <summary>One measured start of the game: when, with which list, how many mods, and how long until it settled.</summary>
public sealed class LoadTime
{
    public DateTime At { get; set; }
    /// <summary>The modlist or instance it ran with ("Normal game" when none is picked).</summary>
    public string List { get; set; } = "";
    public int Mods { get; set; }
    public double Seconds { get; set; }
    /// <summary>Mods active this time that the previous run of the same list didn't have (to spot what made it slower).</summary>
    public List<string> Added { get; set; } = new();
}

/// <summary>
/// How long the game takes to load, measured on this PC each time the Drop Pod starts it: from the start until the log
/// stops growing (the game writes steadily while loading mods and goes quiet at the main menu). An estimate, but the
/// same estimate every time, so a jump after adding a mod shows.
/// </summary>
public sealed class LoadTimes
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    readonly string _file;

    public LoadTimes(string file) => _file = file;

    public List<LoadTime> All()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<List<LoadTime>>(File.ReadAllText(_file), Opts) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    /// <summary>Adds a run, keeping the newest 500. <paramref name="active"/> names the mods so the next run can say what was added.</summary>
    public LoadTime Add(string list, IReadOnlyList<string> active, double seconds, IReadOnlyDictionary<string, string>? names = null, DateTime? at = null)
    {
        var all = All();
        var prevFile = _file + ".mods-" + SafeFiles.SafeName(list, "list") + ".txt";
        var prev = File.Exists(prevFile) ? File.ReadAllLines(prevFile).ToHashSet() : null;
        var run = new LoadTime
        {
            At = at ?? DateTime.Now, List = list, Mods = active.Count, Seconds = Math.Round(seconds, 1),
            Added = prev == null ? new() : active.Where(k => !prev.Contains(k)).Select(k => names?.GetValueOrDefault(k) ?? k).ToList(),
        };
        all.Add(run);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        SafeFiles.WriteWithBackup(_file, JsonSerializer.Serialize(all.TakeLast(500).ToList(), Opts), keep: 0);
        File.WriteAllLines(prevFile, active);
        return run;
    }

    /// <summary>
    /// The settled time from a series of (seconds since start, log length) samples: the last time the log grew before it
    /// stayed the same for <paramref name="quiet"/> seconds. Null when it never went quiet (still loading, or the game closed first).
    /// </summary>
    public static double? Settled(IReadOnlyList<(double Seconds, long Length)> samples, double quiet = 20)
    {
        double? lastGrowth = null;
        long last = -1;
        foreach (var (t, len) in samples)
        {
            if (len != last) { lastGrowth = t; last = len; continue; }
            if (lastGrowth is { } g && len > 0 && t - g >= quiet) return g;
        }
        return null;
    }
}

/// <summary>A mod's latest change notes on Steam, for "what changed in this update".</summary>
public sealed record UpdateNote(DateTimeOffset? When, string Text)
{
    /// <summary>Words in the note that usually mean trouble for a running colony.</summary>
    public IReadOnlyList<string> Warnings => UpdateNotes.Risky(Text);
}

/// <summary>
/// Reads a Workshop item's change notes from its public changelog page (Steam's API doesn't give them). The page is
/// HTML, so only the newest few notes' text is taken; if Steam changes the page the notes just come back empty and the
/// Drop Pod offers the page itself.
/// </summary>
public static class UpdateNotes
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly Regex Entry = new(@"<div class=""changelog headline"">\s*(?<head>.*?)\s*</div>\s*<p id=""(?<stamp>\d+)""[^>]*>(?<body>.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    static readonly Regex Tag = new(@"<br\s*/?>", RegexOptions.IgnoreCase);
    static readonly Regex AnyTag = new(@"<[^>]+>");
    static readonly string[] RiskWords = { "breaking", "break saves", "breaks saves", "new save", "new game required", "save-breaking", "save breaking", "not save compatible", "incompatible with old saves", "remove from", "do not update", "don't update", "wipe", "reset settings", "settings were reset", "rewrite" };

    public static string Url(string workshopId) => "https://steamcommunity.com/sharedfiles/filedetails/changelog/" + workshopId;

    /// <summary>The notes on a changelog page, newest first (at most <paramref name="max"/>).</summary>
    public static List<UpdateNote> Parse(string html, int max = 3)
    {
        var list = new List<UpdateNote>();
        foreach (Match m in Entry.Matches(html))
        {
            DateTimeOffset? when = long.TryParse(m.Groups["stamp"].Value, out var unix) && unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;
            var text = WebUtility.HtmlDecode(AnyTag.Replace(Tag.Replace(m.Groups["body"].Value, "\n"), "")).Trim();
            list.Add(new UpdateNote(when, text.Length > 0 ? text : "(no notes)"));
            if (list.Count >= max) break;
        }
        return list;
    }

    public static List<string> Risky(string text) =>
        RiskWords.Where(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)).ToList();

    public static async Task<List<UpdateNote>> FetchAsync(string workshopId, CancellationToken ct = default)
    {
        try
        {
            var html = await Http.GetStringAsync(Url(workshopId), ct);
            return Parse(html);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(); }
    }
}
