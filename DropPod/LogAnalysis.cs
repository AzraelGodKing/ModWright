using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod;

public enum LogLevel { Info, Warning, Error }

/// <summary>Why a log entry is put on a mod.</summary>
public enum Blame
{
    /// <summary>The mod's own code is in the stack trace.</summary>
    Code,
    /// <summary>The mod has a Harmony patch on a method in the stack trace.</summary>
    Patch,
    /// <summary>The message names a file in the mod's folder.</summary>
    File,
    /// <summary>The message names the mod's package ID or "[Mod name]".</summary>
    Named,
}

/// <param name="Mod">The mod's key.</param>
public sealed record ModBlame(string Mod, Blame Why);

/// <summary>One kind of message in the log, with how often it came up.</summary>
/// <param name="Title">The first line of the first one, shortened.</param>
/// <param name="Text">The whole first one (capped), stack trace included.</param>
/// <param name="Mods">The mods most likely at fault, most likely first.</param>
/// <param name="Line">The line number of the first one.</param>
public sealed record LogIssue(LogLevel Level, string Title, string Text, int Count, IReadOnlyList<ModBlame> Mods, int Line);

/// <summary>What the log says: the game version, the problems grouped, and how many errors point at each mod.</summary>
public sealed record LogReport(string? GameVersion, IReadOnlyList<LogIssue> Issues, IReadOnlyDictionary<string, int> ErrorsByMod, int Lines, bool Truncated)
{
    public int Errors => Issues.Where(i => i.Level == LogLevel.Error).Sum(i => i.Count);
    public int Warnings => Issues.Where(i => i.Level == LogLevel.Warning).Sum(i => i.Count);
}

/// <summary>What is known about the installed mods, for putting log entries on them.</summary>
public sealed class ModIndex
{
    readonly Dictionary<string, string> _namespaces = new(StringComparer.Ordinal);
    readonly List<(string Folder, string Mod)> _folders = new();
    readonly Dictionary<string, string> _ids = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> DisplayNames { get; }

    /// <param name="assemblies">From <see cref="ModAssemblies.ForMods"/>; namespaces two mods both define are not used.</param>
    public ModIndex(IEnumerable<ModManifest> mods, IReadOnlyDictionary<string, List<ModAssembly>> assemblies)
    {
        var names = new Dictionary<string, string>();
        var clash = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (mod, list) in assemblies)
            foreach (var ns in list.SelectMany(a => a.Namespaces).Distinct())
            {
                if (IsGameNamespace(ns)) continue;
                if (_namespaces.TryGetValue(ns, out var other) && other != mod) clash.Add(ns);
                else _namespaces[ns] = mod;
            }
        foreach (var ns in clash) _namespaces.Remove(ns);
        foreach (var m in mods.Where(m => m.Source != ModSource.Official).DistinctBy(m => m.Key))
        {
            names[m.Key] = m.DisplayName;
            _folders.Add((Normalize(m.Folder) + "/", m.Key));
            if (m.Id.Length > 0) _ids[m.Id] = m.Key;
            if (m.Name is { Length: >= 4 } n) _names.TryAdd(n, m.Key);
        }
        _folders.Sort((a, b) => b.Folder.Length.CompareTo(a.Folder.Length));
        DisplayNames = names;
    }

    static readonly string[] GameRoots = { "Verse", "RimWorld", "System", "UnityEngine", "Unity", "Mono", "HarmonyLib", "LudeonTK", "Microsoft", "TMPro", "Steamworks", "Ionic", "NVorbis", "<Module>" };

    static bool IsGameNamespace(string ns) => GameRoots.Any(r => ns == r || ns.StartsWith(r + ".", StringComparison.Ordinal));

    static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

    /// <summary>The mod whose code defines the type <paramref name="dotted"/> ("Ns.Sub.Type.Method"), by the longest namespace that fits.</summary>
    public string? ModOfCode(string dotted)
    {
        var parts = dotted.Split('.');
        for (var n = parts.Length - 1; n >= 1; n--)
            if (_namespaces.TryGetValue(string.Join('.', parts, 0, n), out var mod)) return mod;
        return null;
    }

    public string? ModOfId(string id) => _ids.TryGetValue(id.Trim(), out var m) ? m : _ids.TryGetValue(RimWorld.RimWorldIds.WithoutSteamSuffix(id.Trim()), out m) ? m : null;

    public string? ModOfName(string name) => _names.TryGetValue(name.Trim(), out var m) ? m : null;

    /// <summary>The mod whose folder <paramref name="text"/> names a file in (folder names can hold spaces, so the folders are looked for in the text).</summary>
    public string? ModOfPathIn(string text)
    {
        var t = text.Replace('\\', '/').ToLowerInvariant();
        foreach (var (folder, mod) in _folders) if (t.Contains(folder, StringComparison.Ordinal)) return mod; // longest folders first
        return null;
    }

    public IEnumerable<string> Ids => _ids.Keys;
}

/// <summary>
/// Reads the game's log (Unity's Player.log) a line at a time, so logs of any size are fine: splits it into messages,
/// tells errors and warnings apart, groups repeats, and names the mods most likely at fault from the stack trace (their
/// code, their Harmony patches), file paths and package IDs in the text.
/// </summary>
public static class LogAnalysis
{
    const int MaxEntryLines = 300;
    const int MaxIssues = 2000;
    const int MaxText = 20_000;

    static readonly Regex Frame = new(@"^\s*(?:at\s+)?(?:\(wrapper [^)]*\)\s*)?(?<type>[A-Za-z_][\w`]*(?:[.+][A-Za-z_<][\w`<>|]*)+)[.:](?<method>[\w<>`|.]+)\s*[(\[]", RegexOptions.Compiled);
    static readonly Regex Patch = new(@"^\s*-\s*(?:PREFIX|POSTFIX|TRANSPILER|FINALIZER|ILMANIPULATOR)\s+(?<id>[^:\s]+)\s*:\s*(?<method>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex PatchMethod = new(@"(?<type>[A-Za-z_][\w.`+<>]*):[\w<>`]+\s*\(", RegexOptions.Compiled);
    static readonly Regex Bracket = new(@"^\s*\[(?<name>[^\]\r\n]{3,60})\]", RegexOptions.Compiled);
    static readonly Regex Version = new(@"^RimWorld (?<v>\d+\.\d+\.\d+ rev\d+)", RegexOptions.Compiled);
    static readonly Regex Noise = new(@"0x[0-9a-fA-F]+|\[Ref [0-9A-F]+\]|\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b|\d+", RegexOptions.Compiled);
    static readonly Regex ExceptionName = new(@"\b[\w.]*Exception\b", RegexOptions.Compiled);
    static readonly Regex IdToken = new(@"\b[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+\b", RegexOptions.Compiled);

    public static LogReport Read(string path, ModIndex index, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(r, index, ct);
    }

    public static LogReport Read(TextReader reader, ModIndex index, CancellationToken ct = default)
    {
        string? version = null;
        var groups = new Dictionary<string, (LogIssue Issue, int Count)>();
        var order = new List<string>();
        var entry = new List<string>();
        var entryStart = 1;
        var lineNo = 0;
        var truncated = false;

        void Close()
        {
            if (entry.Count == 0) return;
            var lines = entry;
            entry = new List<string>();
            var level = LevelOf(lines);
            if (level == LogLevel.Info) return;
            var title = lines.First(l => l.Trim().Length > 0).Trim();
            if (title.Length > 300) title = title[..300] + "...";
            // The same failure in the same code is one issue, even when the text names another pawn or thing each time.
            var frames = lines.Where(l => Frame.IsMatch(l) && !l.TrimStart().StartsWith("Verse.Log:", StringComparison.Ordinal) && !l.TrimStart().StartsWith("UnityEngine.", StringComparison.Ordinal))
                .Take(3).Select(l => Noise.Replace(l.Trim(), "#")).ToList();
            var key = level + "|" + (frames.Count > 0 && ExceptionName.Match(title) is { Success: true } ex
                ? ex.Value + "|" + string.Join("|", frames)
                : Noise.Replace(title, "#"));
            if (groups.TryGetValue(key, out var g)) { groups[key] = (g.Issue, g.Count + 1); return; }
            if (groups.Count >= MaxIssues) { truncated = true; return; }
            var text = string.Join("\n", lines);
            if (text.Length > MaxText) text = text[..MaxText] + "\n...";
            groups[key] = (new LogIssue(level, title, text, 1, Blamed(lines, index), entryStart), 1);
            order.Add(key);
        }

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            if ((lineNo & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            if (version == null && lineNo < 50 && Version.Match(line) is { Success: true } vm) version = vm.Groups["v"].Value;
            if (line.StartsWith("(Filename:", StringComparison.Ordinal)) { Close(); entryStart = lineNo + 1; continue; }
            if (line.Trim().Length == 0)
            {
                // Unity puts a blank line between a message and its "(Filename:" line, and between messages without one.
                Close();
                entryStart = lineNo + 1;
                continue;
            }
            if (entry.Count == 0) entryStart = lineNo;
            if (entry.Count < MaxEntryLines) entry.Add(line);
        }
        Close();

        var issues = order.Select(k => groups[k].Issue with { Count = groups[k].Count })
            .OrderByDescending(i => i.Level).ThenBy(i => i.Line).ToList();
        var byMod = new Dictionary<string, int>();
        foreach (var i in issues.Where(i => i.Level == LogLevel.Error))
            if (i.Mods.FirstOrDefault() is { } top) byMod[top.Mod] = byMod.GetValueOrDefault(top.Mod) + i.Count;
        return new LogReport(version, issues, byMod, lineNo, truncated);
    }

    static LogLevel LevelOf(List<string> lines)
    {
        var error = false;
        var warning = false;
        foreach (var l in lines)
        {
            var t = l.TrimStart();
            if (t.StartsWith("Verse.Log:Error", StringComparison.Ordinal) || t.StartsWith("UnityEngine.Debug:LogError", StringComparison.Ordinal)
                || t.StartsWith("Verse.Log:ErrorOnce", StringComparison.Ordinal) || t.StartsWith("UnityEngine.Debug:LogException", StringComparison.Ordinal)) error = true;
            else if (t.StartsWith("Verse.Log:Warning", StringComparison.Ordinal) || t.StartsWith("UnityEngine.Debug:LogWarning", StringComparison.Ordinal)) warning = true;
        }
        var first = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        if (!error && (Regex.IsMatch(first, @"(?:^|\s)(?:\w+\.)*\w*Exception\b(?::|\s|$)") || first.StartsWith("Exception ", StringComparison.Ordinal)
            || first.StartsWith("Error ", StringComparison.OrdinalIgnoreCase) || first.StartsWith("XML error", StringComparison.OrdinalIgnoreCase)
            || first.StartsWith("Could not ", StringComparison.Ordinal) || first.StartsWith("Config error", StringComparison.Ordinal))) error = true;
        return error ? LogLevel.Error : warning ? LogLevel.Warning : LogLevel.Info;
    }

    /// <summary>The mods an entry points at, strongest reason first, each once.</summary>
    public static List<ModBlame> Blamed(IReadOnlyList<string> lines, ModIndex index)
    {
        var found = new List<ModBlame>();
        void Add(string? mod, Blame why) { if (mod != null && found.All(b => b.Mod != mod)) found.Add(new ModBlame(mod, why)); }

        var code = new List<string?>();
        var patches = new List<string?>();
        foreach (var l in lines)
        {
            if (Patch.Match(l) is { Success: true } p)
            {
                var byId = index.ModOfId(p.Groups["id"].Value);
                var byCode = PatchMethod.Match(p.Groups["method"].Value) is { Success: true } pm ? index.ModOfCode(pm.Groups["type"].Value) : null;
                patches.Add(byId ?? byCode);
                continue;
            }
            if (Frame.Match(l) is { Success: true } f) code.Add(index.ModOfCode(f.Groups["type"].Value.Replace('+', '.')));
        }
        foreach (var m in code) Add(m, Blame.Code);
        foreach (var m in patches) Add(m, Blame.Patch);
        var head = string.Join("\n", lines.TakeWhile(l => !Frame.IsMatch(l)).Take(20));
        Add(index.ModOfPathIn(head), Blame.File);
        foreach (Match m in IdToken.Matches(head)) Add(index.ModOfId(m.Value), Blame.Named);
        if (Bracket.Match(head) is { Success: true } b) Add(index.ModOfName(b.Groups["name"].Value), Blame.Named);
        return found;
    }
}
