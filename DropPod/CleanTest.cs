using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod;

/// <summary>The outcome of trying a mod in a clean colony: when, which version of its files, and what the log said about it.</summary>
public sealed class CleanTestResult
{
    public DateTime At { get; set; }
    public string GameVersion { get; set; } = "";
    /// <summary>A hash of the mod's files when it was tested, to tell whether it changed since.</summary>
    public string Hash { get; set; } = "";
    public int Errors { get; set; }
    public int Warnings { get; set; }
    /// <summary>The first few errors that point at the mod.</summary>
    public List<string> Titles { get; set; } = new();
}

/// <summary>
/// Test a mod in a clean colony before uploading it: a throwaway instance with only the base game, the DLC, the mod and
/// what it needs, started with RimWorld's -quicktest (straight into a small map), and its log read for errors that point
/// at the mod. This class plans the list and keeps the results; the window starts the game and reads the log.
/// </summary>
public static class CleanTest
{
    /// <summary>The instance the tests run in. It is made the first time and reused.</summary>
    public const string InstanceName = "Launch Pad test";

    /// <summary>
    /// The active list for testing <paramref name="mod"/>: the official content the player has (base game first, then
    /// DLC), then what the mod needs (and what those need) from the installed mods, then the mod itself, sorted by the
    /// usual rules. Also returns what it needs that isn't installed.
    /// </summary>
    public static (List<string> Active, List<ModDependency> Missing) Plan(ModManifest mod, LibraryScan scan, IGameModule game)
    {
        var byId = scan.ById();
        var version = scan.Install.Version;
        var picked = new List<ModManifest>();
        var missing = new List<ModDependency>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Need(ModManifest m)
        {
            if (!seen.Add(m.Key)) return;
            foreach (var d in m.Dependencies.For(version))
            {
                var k = game.ActiveKey(d.Id);
                if (byId.TryGetValue(k, out var dep)) Need(dep);
                else if (d.Id.Length > 0 && missing.All(x => x.Key != d.Key)) missing.Add(d);
            }
            picked.Add(m);
        }
        foreach (var o in scan.Mods.Where(m => m.Source == ModSource.Official).OrderBy(game.SortTier).DistinctBy(m => m.Key)) Need(o);
        Need(mod);
        var sorted = LoadOrder.Sort(picked, version, SortMode.Rules, null, game.SortTier).Order.Select(m => m.Key).ToList();
        return (sorted, missing);
    }

    /// <summary>The log problems that point at <paramref name="modKey"/>, errors first.</summary>
    public static List<LogIssue> About(LogReport report, string modKey) =>
        report.Issues.Where(i => i.Mods.Any(b => b.Mod == modKey)).OrderByDescending(i => i.Level).ThenByDescending(i => i.Count).ToList();

    public static CleanTestResult Result(LogReport report, string modKey, string hash, string? gameVersion, DateTime? at = null)
    {
        var mine = About(report, modKey);
        return new CleanTestResult
        {
            At = at ?? DateTime.Now, GameVersion = gameVersion ?? report.GameVersion ?? "", Hash = hash,
            Errors = mine.Where(i => i.Level == LogLevel.Error).Sum(i => i.Count),
            Warnings = mine.Where(i => i.Level == LogLevel.Warning).Sum(i => i.Count),
            Titles = mine.Where(i => i.Level == LogLevel.Error).Take(5).Select(i => i.Title).ToList(),
        };
    }

    /// <summary>
    /// What the upload confirmation says about a mod's last clean-colony test: errors it found, or that the mod changed
    /// since. Null when the last test is clean and current, or when it was never tested (nothing to say then).
    /// </summary>
    public static string? Warning(CleanTestResult? last, string currentHash)
    {
        if (last == null) return null;
        if (last.Hash != currentHash) return $"changed since its clean-colony test on {last.At:d MMM}; test it again (right-click > Test in a clean colony)";
        if (last.Errors > 0) return $"its clean-colony test on {last.At:d MMM} had {last.Errors} error{(last.Errors == 1 ? "" : "s")}: {string.Join("; ", last.Titles.Take(2))}";
        return null;
    }
}

/// <summary>The last clean-colony test of each mod, by mod folder.</summary>
public sealed class CleanTestRecord
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    readonly string _file;

    public CleanTestRecord(string file) => _file = file;

    Dictionary<string, CleanTestResult> Load()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<Dictionary<string, CleanTestResult>>(File.ReadAllText(_file), Opts) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    static string Key(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

    public CleanTestResult? Get(string modFolder) => Load().GetValueOrDefault(Key(modFolder));

    /// <summary>Results by game version for a mod, for the compatibility grid (keyed "folder|version").</summary>
    public CleanTestResult? Get(string modFolder, string gameVersion) => Load().GetValueOrDefault(Key(modFolder) + "|" + GameVersion.Short(gameVersion));

    public void Set(string modFolder, CleanTestResult r)
    {
        var all = Load();
        all[Key(modFolder)] = r;
        if (r.GameVersion.Length > 0) all[Key(modFolder) + "|" + GameVersion.Short(r.GameVersion)] = r;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        SafeFiles.WriteWithBackup(_file, JsonSerializer.Serialize(all, Opts), keep: 0);
    }
}

/// <summary>
/// Read a player's log: a log a player sent (pasted, a file, or a link to a paste site), with only the problems that
/// point at your own mods, and the mod list the log shows when it has one (HugsLib and the Drop Pod's report list them).
/// </summary>
public static class PlayerLog
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Regex Pastebin = new(@"^https?://(www\.)?pastebin\.com/(raw/)?(?<id>\w+)/?$", RegexOptions.IgnoreCase);
    static readonly Regex Gist = new(@"^https?://gist\.github\.com/(?<user>[\w-]+)/(?<id>\w+)/?$", RegexOptions.IgnoreCase);
    static readonly Regex HasteBin = new(@"^https?://(?<host>(www\.)?(hastebin\.com|toptal\.com/developers/hastebin))/(raw/)?(?<id>\w+)(\.\w+)?/?$", RegexOptions.IgnoreCase);
    static readonly Regex GistFile = new(@"^https?://gist\.githubusercontent\.com/", RegexOptions.IgnoreCase);

    /// <summary>The raw-text address for a paste link (pastebin, gist, hastebin); other http(s) links as they are. Null when it isn't a link.</summary>
    public static string? RawUrl(string link)
    {
        var t = link.Trim();
        if (Pastebin.Match(t) is { Success: true } p) return "https://pastebin.com/raw/" + p.Groups["id"].Value;
        if (Gist.Match(t) is { Success: true } g) return $"https://gist.githubusercontent.com/{g.Groups["user"].Value}/{g.Groups["id"].Value}/raw";
        if (HasteBin.Match(t) is { Success: true } h) return $"https://{h.Groups["host"].Value}/raw/{h.Groups["id"].Value}";
        if (GistFile.IsMatch(t)) return t;
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && !t.Contains('\n') ? u.ToString() : null;
    }

    /// <summary>The log text: fetched when <paramref name="input"/> is a link, the text itself otherwise. Throws <see cref="IOException"/> when a link can't be read.</summary>
    public static async Task<string> TextAsync(string input, CancellationToken ct = default)
    {
        if (RawUrl(input) is not { } url) return input;
        try
        {
            var text = await Http.GetStringAsync(url, ct);
            if (text.Length > 64_000_000) text = text[..64_000_000];
            return text;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new IOException("Could not read the link: " + e.Message, e);
        }
    }

    /// <summary>Only the problems that point at one of <paramref name="mine"/> (mod keys).</summary>
    public static List<LogIssue> Mine(LogReport report, IReadOnlySet<string> mine) =>
        report.Issues.Where(i => i.Mods.Any(b => mine.Contains(b.Mod))).ToList();

    static readonly Regex HugsMods = new(@"^\s*(?:Mods|Loaded mods|Active mods)\s*[:(]", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// The mod list a log or report carries, when it has one: HugsLib's "Loaded mods:" block (one "Name(id)[version]" per
    /// line) or a Drop Pod share code anywhere in it. Empty when there is none.
    /// </summary>
    public static List<string> ModList(string text)
    {
        var code = Regex.Match(text, @"DROPPOD1:[A-Za-z0-9_\-]+");
        if (code.Success)
            try { return Modlist.FromShareCode(code.Value).Mods.Select(m => m.Name ?? m.Id).ToList(); }
            catch (FormatException) { }
        var lines = text.Replace("\r", "").Split('\n');
        var start = Array.FindIndex(lines, l => HugsMods.IsMatch(l));
        if (start < 0) return new();
        var list = new List<string>();
        for (var i = start + 1; i < lines.Length && list.Count < 1000; i++)
        {
            var l = lines[i].Trim();
            if (l.Length == 0) break;
            if (l.Length > 200) continue;
            list.Add(l);
        }
        return list;
    }
}
