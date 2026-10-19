using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>One of a game's Steam branches: "public" is what everyone plays, the others are betas.</summary>
/// <param name="Password">The beta needs a password to install.</param>
public sealed record GameBranch(string Name, string BuildId, DateTimeOffset? Updated, string Description, bool Password)
{
    public bool IsPublic => Name.Equals("public", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What was last seen of a game's branches, to notice when the game updates.</summary>
public sealed record SeenBuilds(DateTimeOffset Checked, Dictionary<string, string> Builds);

/// <summary>
/// Reads a game's Steam branches and build ids with SteamCMD's "app_info_print" (anonymous login: no account needed),
/// and notices when the public build or a beta changed since the last look. A new game build is when mods most often
/// break, and when a new game version needs adding to the mods' supported versions.
/// </summary>
public static class GameBuilds
{
    /// <summary>How often the window looks at start-up.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(12);

    public static string DefaultFile => Path.Combine(AppSettings.Folder, "game-builds.json");

    /// <summary>
    /// The arguments: anonymous login, then a fresh copy of the app info (without app_info_update SteamCMD can print a
    /// stale cached copy).
    /// </summary>
    public static string[] Args(string appId) =>
        SteamCmd.FailFast.Concat(SteamCmd.Login(null)).Concat(new[] { "+app_info_update", "1", "+app_info_print", appId, "+quit" }).ToArray();

    /// <summary>The branches in app_info_print output, public first then by name. Empty when the output has none.</summary>
    public static List<GameBranch> Parse(string appId, IEnumerable<string> lines)
    {
        var all = lines.Select(SteamCmd.Clean).ToList();
        var start = all.FindIndex(l => l == $"\"{appId}\"");
        if (start < 0) return new();
        var kv = KeyValues.Parse(string.Join("\n", all.Skip(start)));
        var branches = kv.Block(appId)?.Block("depots")?.Block("branches");
        if (branches == null) return new();
        var list = new List<GameBranch>();
        foreach (var (name, value) in branches)
        {
            if (value is not KeyValues b) continue;
            DateTimeOffset? updated = long.TryParse(b.Value("timeupdated"), out var t) ? DateTimeOffset.FromUnixTimeSeconds(t) : null;
            list.Add(new GameBranch(name, b.Value("buildid") ?? "", updated, b.Value("description") ?? "", b.Value("pwdrequired") == "1"));
        }
        return list.OrderBy(b => b.IsPublic ? 0 : 1).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Asks Steam for the game's branches. Null when SteamCMD could not get them.</summary>
    public static async Task<List<GameBranch>?> FetchAsync(string steamCmd, string appId, CancellationToken ct)
    {
        var run = await SteamCmd.RunAsync(steamCmd, Args(appId), null, ct);
        var list = Parse(appId, run.Lines);
        return list.Count > 0 ? list : null;
    }

    /// <summary>One line per branch, for the log.</summary>
    public static string Describe(GameBranch b) =>
        $"{b.Name}: build {(b.BuildId.Length > 0 ? b.BuildId : "?")}" +
        (b.Updated is { } u ? $", updated {u.ToLocalTime():d MMM yyyy HH:mm}" : "") +
        (b.Description.Length > 0 ? $" ({b.Description})" : "") +
        (b.Password ? ", needs a password" : "");

    /// <summary>
    /// What changed since <paramref name="before"/>, in plain words: a new public build, and new or updated betas.
    /// Nothing the first time (there is nothing to compare with), and nothing for betas that were removed.
    /// </summary>
    public static List<string> Changes(IReadOnlyList<GameBranch> now, SeenBuilds? before, string game)
    {
        var lines = new List<string>();
        if (before == null) return lines;
        var name = game.Length > 0 ? game : "The game";
        foreach (var b in now)
        {
            if (b.BuildId.Length == 0) continue;
            var had = before.Builds.TryGetValue(b.Name, out var old);
            if (had && old == b.BuildId) continue;
            var when = b.Updated is { } u ? $" on {u.ToLocalTime():d MMM yyyy}" : "";
            if (b.IsPublic)
                lines.Add($"{name} has a new public build ({b.BuildId}, updated{when}). Check your mods still load, and if it is a new game version, add it to their supported versions.");
            else if (!had)
                lines.Add($"{name} has a new beta branch \"{b.Name}\"{(b.Description.Length > 0 ? $" ({b.Description})" : "")}, build {b.BuildId}{when}. Try in a game beta... can install it to test your mods.");
            else
                lines.Add($"{name}'s \"{b.Name}\" beta was updated (build {b.BuildId}{when}).");
        }
        return lines;
    }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Dictionary<string, SeenBuilds> Load(string? file = null)
    {
        try
        {
            var p = file ?? DefaultFile;
            return File.Exists(p) ? JsonSerializer.Deserialize<Dictionary<string, SeenBuilds>>(File.ReadAllText(p)) ?? new() : new();
        }
        catch { return new(); }
    }

    /// <summary>Remembers the branches just seen. Never throws.</summary>
    public static void Remember(string appId, IReadOnlyList<GameBranch> now, string? file = null)
    {
        try
        {
            var p = file ?? DefaultFile;
            var all = Load(p);
            all[appId] = new SeenBuilds(DateTimeOffset.UtcNow, now.Where(b => b.BuildId.Length > 0).ToDictionary(b => b.Name, b => b.BuildId, StringComparer.OrdinalIgnoreCase));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(all, Opts));
        }
        catch { }
    }

    /// <summary>True when the game has not been looked at for <see cref="CheckEvery"/>.</summary>
    public static bool Due(string appId, DateTimeOffset now, string? file = null) =>
        !Load(file).TryGetValue(appId, out var seen) || now - seen.Checked >= CheckEvery;
}
