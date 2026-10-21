namespace SteamCmdHelper.DropPod;

/// <summary>What the player said after trying one round's mods.</summary>
public enum BisectAnswer { Broken, Works }

/// <summary>
/// Finds the mod that breaks a game by halving: each round tries half of the suspects (with what they need), the player
/// says whether the problem is still there, and the suspects shrink to the half that has it. About log2(n) rounds, so
/// 7 for 100 mods. The base game and DLC always stay on. When neither half alone shows the problem it takes mods from
/// both, so one half is kept on and the search goes on in the other; the result then says so.
/// </summary>
public sealed class Bisect
{
    readonly IReadOnlyList<string> _all;
    readonly Dictionary<string, IReadOnlyList<string>> _needs;
    readonly HashSet<string> _always;
    List<string> _suspects;
    readonly List<string> _kept = new();
    bool _triedSecond;

    /// <summary>The round number, from 1.</summary>
    public int Round { get; private set; } = 1;
    /// <summary>True once the culprit is found (or nothing is left to try).</summary>
    public bool Done => _suspects.Count <= 1;
    /// <summary>The mod at fault once <see cref="Done"/>.</summary>
    public string? Culprit => Done ? _suspects.FirstOrDefault() : null;
    /// <summary>Mods that had to stay on for the problem to show: the culprit clashes with one of these.</summary>
    public IReadOnlyList<string> AlsoNeeded => _kept;
    public IReadOnlyList<string> Suspects => _suspects;

    /// <param name="active">The active mods (keys) in load order, as they were when the problem showed.</param>
    /// <param name="always">Keys that always stay on (the base game and DLC).</param>
    /// <param name="needs">What each mod needs (keys of other active mods).</param>
    public Bisect(IReadOnlyList<string> active, IEnumerable<string> always, IReadOnlyDictionary<string, IReadOnlyList<string>> needs)
    {
        _all = active.Distinct().ToList();
        _always = always.ToHashSet(StringComparer.Ordinal);
        _needs = needs.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        _suspects = _all.Where(k => !_always.Contains(k)).ToList();
    }

    /// <summary>Builds one from a scan: the game's official content always stays on; dependencies come from each mod's manifest.</summary>
    public static Bisect For(IReadOnlyList<string> active, LibraryScan scan, IGameModule game)
    {
        var byId = scan.ById();
        var keys = active.Select(game.ActiveKey).Distinct().ToList();
        var set = keys.ToHashSet();
        var needs = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var k in keys)
            if (byId.TryGetValue(k, out var m))
                needs[k] = m.Dependencies.For(scan.Install.Version).Select(d => game.ActiveKey(d.Id)).Where(set.Contains).Distinct().ToList();
        var always = keys.Where(k => byId.TryGetValue(k, out var m) && m.Source == ModSource.Official);
        return new Bisect(keys, always, needs);
    }

    IReadOnlyList<string> Half => _triedSecond ? _suspects.Skip(_suspects.Count / 2).ToList() : _suspects.Take(_suspects.Count / 2).ToList();

    /// <summary>The mods to try this round, in the original load order: the base game and DLC, the kept mods, half the suspects, and what they need.</summary>
    public List<string> Trial()
    {
        var on = new HashSet<string>(_always, StringComparer.Ordinal);
        var stack = new Stack<string>(_kept.Concat(Done ? _suspects : Half));
        while (stack.Count > 0)
        {
            var k = stack.Pop();
            if (!on.Add(k)) continue;
            if (_needs.TryGetValue(k, out var deps)) foreach (var d in deps) stack.Push(d);
        }
        return _all.Where(on.Contains).ToList();
    }

    /// <summary>How many suspects this round tries, of how many are left.</summary>
    public (int Trying, int Left) Size => (Done ? _suspects.Count : Half.Count, _suspects.Count);

    /// <summary>Takes the player's answer about <see cref="Trial"/> and moves to the next round.</summary>
    public void Answer(BisectAnswer answer)
    {
        if (Done) return;
        var half = Half.ToList();
        if (answer == BisectAnswer.Broken)
        {
            _suspects = half;
            _triedSecond = false;
        }
        else if (!_triedSecond)
        {
            _triedSecond = true; // the first half is clean: try the second half on its own
        }
        else
        {
            // Neither half alone breaks it: it needs mods from both. Keep the second half on and look in the first.
            var first = _suspects.Take(_suspects.Count / 2).ToList();
            _kept.AddRange(half);
            _suspects = first;
            _triedSecond = false;
        }
        Round++;
    }

    /// <summary>Rounds left at most, for the progress line.</summary>
    public int RoundsLeft => _suspects.Count <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(_suspects.Count));
}
