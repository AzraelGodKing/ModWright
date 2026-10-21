using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdHelper.DropPod;

/// <summary>How <see cref="LoadOrder.Sort"/> breaks ties between mods no rule orders.</summary>
public enum SortMode
{
    /// <summary>Keep the current order wherever the rules allow it.</summary>
    Rules,
    /// <summary>Alphabetical by name wherever the rules allow it.</summary>
    Alphabetical,
}

/// <summary>Mods whose rules form a loop, so no order satisfies all of them; they are kept together in the result.</summary>
/// <param name="Edges">The rules inside the loop, in words ("A loads after B").</param>
public sealed record SortCycle(IReadOnlyList<ModManifest> Mods, IReadOnlyList<string> Edges);

public sealed record SortResult(IReadOnlyList<ModManifest> Order, IReadOnlyList<SortCycle> Cycles);

/// <summary>
/// Extra load-order rules from a file the player picks (nothing is bundled). Our own small JSON format, keyed by id:
/// { "rules": { "some.mod": { "loadAfter": ["a.b"], "loadBefore": ["c.d"], "loadFirst": true, "loadLast": true,
/// "incompatibleWith": ["e.f"] } } }
/// </summary>
public sealed class SortRules
{
    public sealed class Rule
    {
        public List<string> LoadAfter { get; set; } = new();
        public List<string> LoadBefore { get; set; } = new();
        public List<string> IncompatibleWith { get; set; } = new();
        public bool LoadFirst { get; set; }
        public bool LoadLast { get; set; }
        /// <summary>Free text shown with problems about this mod ("known to slow down long games").</summary>
        public string? Note { get; set; }
    }

    public Dictionary<string, Rule> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Rule? For(string key) => Rules.TryGetValue(key, out var r) ? r : null;

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>Reads a rules file. Throws <see cref="JsonException"/> with the line when it is not valid.</summary>
    public static SortRules Parse(string json)
    {
        var r = JsonSerializer.Deserialize<SortRules>(json, Opts) ?? new SortRules();
        // Keys compared like ids: lower case.
        r.Rules = r.Rules.Where(kv => kv.Value != null).ToDictionary(kv => ModManifest.KeyOf(kv.Key), kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        return r;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Opts);
}

/// <summary>
/// Sorts the active mods so every rule holds where it can: a mod after what it depends on and its loadAfter list, before
/// its loadBefore list, the player's rules file on top. Ties keep the current order (or go alphabetical), and the game's
/// own packages come first unless a rule says otherwise. Mods caught in a loop of rules are reported, not dropped.
/// </summary>
public static class LoadOrder
{
    /// <param name="mods">The active mods in their current order.</param>
    /// <param name="tier">Lower goes earlier when no rule decides (the game module puts the base game and DLC first).</param>
    public static SortResult Sort(IReadOnlyList<ModManifest> mods, string? gameVersion, SortMode mode = SortMode.Rules,
        SortRules? rules = null, Func<ModManifest, int>? tier = null)
    {
        var n = mods.Count;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++) index.TryAdd(mods[i].Key, i);
        var after = Enumerable.Range(0, n).Select(_ => new HashSet<int>()).ToArray(); // after[i]: what must load before i
        var why = new Dictionary<(int Before, int After), string>();

        void Edge(int before, int afterIdx, string reason)
        {
            if (before == afterIdx) return;
            if (after[afterIdx].Add(before)) why[(before, afterIdx)] = reason;
        }
        int? Find(string id) => index.TryGetValue(ModManifest.KeyOf(id), out var i) ? i : null;

        for (var i = 0; i < n; i++)
        {
            var m = mods[i];
            foreach (var d in m.Dependencies.For(gameVersion))
                if (d.Id.Length > 0 && Find(d.Id) is { } j) Edge(j, i, $"{m.DisplayName} needs {mods[j].DisplayName}");
            foreach (var id in m.LoadAfter.For(gameVersion).Concat(m.ForceLoadAfter.For(gameVersion)))
                if (Find(id) is { } j) Edge(j, i, $"{m.DisplayName} loads after {mods[j].DisplayName}");
            foreach (var id in m.LoadBefore.For(gameVersion).Concat(m.ForceLoadBefore.For(gameVersion)))
                if (Find(id) is { } j) Edge(i, j, $"{m.DisplayName} loads before {mods[j].DisplayName}");
            if (rules?.For(m.Key) is { } r)
            {
                foreach (var id in r.LoadAfter) if (Find(id) is { } j) Edge(j, i, $"your rules: {m.DisplayName} after {mods[j].DisplayName}");
                foreach (var id in r.LoadBefore) if (Find(id) is { } j) Edge(i, j, $"your rules: {m.DisplayName} before {mods[j].DisplayName}");
            }
        }

        // Tie-break key: rules-file first/last, then the game's tier, then current position or name.
        var names = Enumerable.Range(0, n).OrderBy(i => mods[i].DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i).ToArray();
        var alphaRank = new int[n];
        for (var r = 0; r < n; r++) alphaRank[names[r]] = r;
        (int, int, int) Priority(int i)
        {
            var r = rules?.For(mods[i].Key);
            var band = r?.LoadFirst == true ? 0 : r?.LoadLast == true ? 2 : 1;
            return (band, tier?.Invoke(mods[i]) ?? 0, mode == SortMode.Alphabetical ? alphaRank[i] : i);
        }

        // Strongly connected parts (Tarjan): a part of more than one mod is a loop of rules.
        var comp = Components(n, after, out var compCount);
        var members = Enumerable.Range(0, compCount).Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < n; i++) members[comp[i]].Add(i);
        foreach (var list in members) list.Sort((a, b) => Priority(a).CompareTo(Priority(b)));

        // Kahn's algorithm over the parts, always taking the waiting part whose best mod has the best priority.
        var waitingOn = new int[compCount];
        var next = Enumerable.Range(0, compCount).Select(_ => new HashSet<int>()).ToArray();
        for (var i = 0; i < n; i++)
            foreach (var b in after[i])
                if (comp[b] != comp[i] && next[comp[b]].Add(comp[i])) waitingOn[comp[i]]++;
        // A part that something early waits on counts as early itself (Harmony, which loads before the base game, goes
        // before the DLC). Tarjan numbers a part after everything it waits on, so walk them from the highest down.
        var own = members.Select(l => Priority(l[0])).ToArray();
        var key = new ((int, int, int) Effective, (int, int, int) Own)[compCount];
        for (var c = compCount - 1; c >= 0; c--)
        {
            var best = own[c];
            foreach (var d in next[c]) if (key[d].Effective.CompareTo(best) < 0) best = key[d].Effective;
            key[c] = (best, own[c]);
        }
        var ready = new PriorityQueue<int, ((int, int, int), (int, int, int))>();
        for (var c = 0; c < compCount; c++) if (waitingOn[c] == 0) ready.Enqueue(c, key[c]);
        var order = new List<ModManifest>(n);
        while (ready.TryDequeue(out var c, out _))
        {
            order.AddRange(members[c].Select(i => mods[i]));
            foreach (var d in next[c]) if (--waitingOn[d] == 0) ready.Enqueue(d, key[d]);
        }

        var cycles = members.Where(l => l.Count > 1).Select(l =>
        {
            var set = l.ToHashSet();
            var edges = l.SelectMany(i => after[i].Where(set.Contains).Select(b => why[(b, i)])).Distinct().ToList();
            return new SortCycle(l.Select(i => mods[i]).ToList(), edges);
        }).ToList();
        return new SortResult(order, cycles);
    }

    /// <summary>Tarjan's strongly connected components, iterative so a long chain of rules can't overflow the stack.</summary>
    static int[] Components(int n, HashSet<int>[] after, out int count)
    {
        var idx = new int[n]; var low = new int[n]; var comp = new int[n];
        Array.Fill(idx, -1);
        var onStack = new bool[n];
        var stack = new Stack<int>();
        var counter = 0; count = 0;
        var edges = after.Select(s => s.ToArray()).ToArray();
        for (var s = 0; s < n; s++)
        {
            if (idx[s] >= 0) continue;
            var work = new Stack<(int Node, int Edge)>();
            work.Push((s, 0));
            idx[s] = low[s] = counter++; stack.Push(s); onStack[s] = true;
            while (work.Count > 0)
            {
                var (v, e) = work.Pop();
                if (e < edges[v].Length)
                {
                    work.Push((v, e + 1));
                    var w = edges[v][e];
                    if (idx[w] < 0)
                    {
                        idx[w] = low[w] = counter++; stack.Push(w); onStack[w] = true;
                        work.Push((w, 0));
                    }
                    else if (onStack[w]) low[v] = Math.Min(low[v], idx[w]);
                    continue;
                }
                if (low[v] == idx[v])
                {
                    int w;
                    do { w = stack.Pop(); onStack[w] = false; comp[w] = count; } while (w != v);
                    count++;
                }
                if (work.Count > 0) { var parent = work.Peek().Node; low[parent] = Math.Min(low[parent], low[v]); }
            }
        }
        return comp;
    }
}
