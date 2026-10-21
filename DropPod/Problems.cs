namespace SteamCmdHelper.DropPod;

public enum ProblemKind
{
    /// <summary>An active mod needs a mod that is not installed (or installed but not active).</summary>
    MissingDependency,
    /// <summary>An active entry in the mod list is not installed.</summary>
    NotInstalled,
    /// <summary>The mod does not list the game's version as supported.</summary>
    WrongVersion,
    /// <summary>Two installed folders claim the same id; the game loads only one.</summary>
    Duplicate,
    /// <summary>Two active mods say they don't work together.</summary>
    Incompatible,
    /// <summary>The order breaks a rule (A should load after B but loads before it).</summary>
    Order,
    /// <summary>The mod's manifest could not be read properly.</summary>
    BrokenManifest,
    /// <summary>A note from the player's rules file about an active mod.</summary>
    RulesNote,
}

public enum ProblemLevel { Info, Warning, Error }

/// <param name="Mod">The id (key) of the mod the problem is about.</param>
/// <param name="Other">The other mod involved (the missing dependency, the incompatible mod, ...), when there is one.</param>
/// <param name="WorkshopId">A Workshop item that would fix it (the missing dependency's), when known.</param>
public sealed record ModProblem(ProblemKind Kind, ProblemLevel Level, string Mod, string Message, string? Other = null, string? WorkshopId = null);

/// <summary>
/// Finds what will go wrong with the active mods before the game is started: missing or inactive dependencies,
/// entries that aren't installed, mods for another game version, ids installed twice, incompatible pairs, rules the
/// order breaks, broken manifests and notes from the player's rules file.
/// </summary>
public static class Problems
{
    public static List<ModProblem> Find(LibraryScan scan, IReadOnlyList<string> active, IGameModule game, SortRules? rules = null)
    {
        var list = new List<ModProblem>();
        var version = scan.Install.Version;
        var byId = scan.ById();
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < active.Count; i++) position.TryAdd(game.ActiveKey(active[i]), i);
        string Name(string key) => byId.TryGetValue(key, out var m) ? m.DisplayName : key;

        foreach (var (key, pos) in position.OrderBy(p => p.Value))
        {
            if (!byId.TryGetValue(key, out var m))
            {
                list.Add(new ModProblem(ProblemKind.NotInstalled, ProblemLevel.Warning, key, $"{key} is in the mod list but not installed; the game skips it."));
                continue;
            }
            foreach (var d in m.Dependencies.For(version).Where(d => d.Id.Length > 0))
            {
                var dk = game.ActiveKey(d.Id);
                var label = d.DisplayName ?? d.Id;
                if (!byId.ContainsKey(dk))
                    list.Add(new ModProblem(ProblemKind.MissingDependency, ProblemLevel.Error, key, $"{m.DisplayName} needs {label}, which is not installed.", dk, d.WorkshopId));
                else if (!position.ContainsKey(dk))
                    list.Add(new ModProblem(ProblemKind.MissingDependency, ProblemLevel.Error, key, $"{m.DisplayName} needs {Name(dk)}, which is installed but not active.", dk));
                else if (position[dk] > pos)
                    list.Add(new ModProblem(ProblemKind.Order, ProblemLevel.Error, key, $"{m.DisplayName} needs {Name(dk)}, but loads before it.", dk));
            }
            foreach (var id in m.LoadAfter.For(version).Concat(m.ForceLoadAfter.For(version)))
                if (position.TryGetValue(game.ActiveKey(id), out var other) && other > pos)
                    list.Add(new ModProblem(ProblemKind.Order, ProblemLevel.Warning, key, $"{m.DisplayName} should load after {Name(game.ActiveKey(id))}.", game.ActiveKey(id)));
            foreach (var id in m.LoadBefore.For(version).Concat(m.ForceLoadBefore.For(version)))
                if (position.TryGetValue(game.ActiveKey(id), out var other) && other < pos)
                    list.Add(new ModProblem(ProblemKind.Order, ProblemLevel.Warning, key, $"{m.DisplayName} should load before {Name(game.ActiveKey(id))}.", game.ActiveKey(id)));
            var incompatible = m.Incompatible.For(version).Concat(rules?.For(key)?.IncompatibleWith ?? new());
            foreach (var id in incompatible.Select(game.ActiveKey).Distinct())
                if (id != key && position.ContainsKey(id))
                    list.Add(new ModProblem(ProblemKind.Incompatible, ProblemLevel.Error, key, $"{m.DisplayName} does not work with {Name(id)}; both are active.", id));
            if (!m.Supports(version))
                list.Add(new ModProblem(ProblemKind.WrongVersion, ProblemLevel.Warning, key,
                    $"{m.DisplayName} lists {string.Join(", ", m.SupportedVersions)}, not {GameVersion.Short(version!)}; it may not work."));
            foreach (var issue in m.Issues)
                list.Add(new ModProblem(ProblemKind.BrokenManifest, ProblemLevel.Warning, key, $"{m.DisplayName}: {issue}"));
            if (rules?.For(key)?.Note is { Length: > 0 } note)
                list.Add(new ModProblem(ProblemKind.RulesNote, ProblemLevel.Info, key, $"{m.DisplayName}: {note}"));
        }

        foreach (var g in scan.Duplicates())
        {
            var used = byId[g.Key];
            var others = g.Where(m => m != used).Select(m => m.Folder);
            list.Add(new ModProblem(ProblemKind.Duplicate, position.ContainsKey(g.Key) ? ProblemLevel.Warning : ProblemLevel.Info, g.Key,
                $"{used.DisplayName} is installed {g.Count()} times; the game uses {used.Folder} and ignores {string.Join(", ", others)}."));
        }

        // The same pair reported from both sides reads twice; keep one.
        return list.DistinctBy(p => p.Kind == ProblemKind.Incompatible ? $"inc|{string.Join("|", new[] { p.Mod, p.Other }.OrderBy(x => x))}" : $"{p.Kind}|{p.Mod}|{p.Other}|{p.Message}")
            .OrderByDescending(p => p.Level).ThenBy(p => position.GetValueOrDefault(p.Mod, int.MaxValue)).ToList();
    }

    /// <summary>The Workshop items that would fill missing dependencies, each once.</summary>
    public static List<string> DownloadableFixes(IEnumerable<ModProblem> problems) =>
        problems.Where(p => p.Kind == ProblemKind.MissingDependency && p.WorkshopId != null).Select(p => p.WorkshopId!).Distinct().ToList();

    /// <summary>
    /// The active list with installed-but-inactive dependencies added (each just before the first mod that needs it),
    /// repeated until nothing more is pulled in.
    /// </summary>
    public static List<string> WithInactiveDependencies(LibraryScan scan, IReadOnlyList<string> active, IGameModule game)
    {
        var byId = scan.ById();
        var list = active.Select(game.ActiveKey).Distinct().ToList();
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 0; i < list.Count; i++)
            {
                if (!byId.TryGetValue(list[i], out var m)) continue;
                foreach (var d in m.Dependencies.For(scan.Install.Version).Where(d => d.Id.Length > 0))
                {
                    var dk = game.ActiveKey(d.Id);
                    if (byId.ContainsKey(dk) && !list.Contains(dk)) { list.Insert(i, dk); changed = true; i++; }
                }
            }
        }
        return list;
    }
}
