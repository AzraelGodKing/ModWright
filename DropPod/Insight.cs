namespace SteamCmdHelper.DropPod;

public enum InsightKind
{
    /// <summary>Two active mods change the same game method in ways that can clash.</summary>
    PatchConflict,
    /// <summary>A mod changes methods the game runs every tick, which can cost speed.</summary>
    TickPatches,
    /// <summary>A mod with a lot of textures or definitions, which makes the game load slower.</summary>
    LoadWeight,
    /// <summary>A note from the player's rules file.</summary>
    RulesNote,
    /// <summary>Two mods change the same game data (defs or XML patches); the one loaded later wins.</summary>
    XmlClash,
    /// <summary>XML patches that find nothing, or XML files that can't be read: the game logs red errors for them.</summary>
    XmlError,
}

/// <param name="Mods">The mods involved (keys).</param>
public sealed record InsightItem(InsightKind Kind, ProblemLevel Level, IReadOnlyList<string> Mods, string Message);

/// <summary>How much a mod gives the game to load.</summary>
public sealed record ModWeight(string Mod, long TextureBytes, int TextureFiles, int DefFiles, long CodeBytes);

/// <summary>
/// Hints about speed and clashes among the active mods: Harmony patches two mods put on the same method, mods that
/// patch per-tick methods, mods heavy on textures and definitions, and the player's own notes. These are hints from
/// what the files declare, not measurements.
/// </summary>
public static class Insight
{
    /// <summary>Methods the game calls every tick (or nearly), for every pawn or thing; patches there cost the most.</summary>
    static readonly HashSet<string> HotMethods = new(StringComparer.Ordinal)
    {
        "DoSingleTick", "GetStatValue", "GetValueUnfinalized", "GetValue", "FindPath", "TryIssueJobPackage", "DetermineNextJob",
        "CheckForStateChange", "TickInterval", "DrawAt", "DynamicDrawPhaseAt", "Notify_Tick",
    };

    public static bool IsHot(string target)
    {
        var method = target[(target.LastIndexOf('.') + 1)..];
        return method.EndsWith("Tick", StringComparison.Ordinal) || method.StartsWith("Tick", StringComparison.Ordinal) || HotMethods.Contains(method);
    }

    /// <summary>
    /// Methods several active mods patch in ways that can clash: two or more transpilers (each rewrites the method's code
    /// and may not find what it expects after the other), or a prefix that can skip the original next to other mods'
    /// patches (when it skips, theirs may not run).
    /// </summary>
    public static List<InsightItem> Conflicts(IReadOnlyDictionary<string, List<HarmonyPatchInfo>> patches, IReadOnlyCollection<string> active, Func<string, string> name)
    {
        var list = new List<InsightItem>();
        var on = active.ToHashSet();
        var byTarget = patches.Where(p => on.Contains(p.Key))
            .SelectMany(p => p.Value.Select(x => (Mod: p.Key, Patch: x)))
            .GroupBy(x => x.Patch.Target, StringComparer.Ordinal);
        foreach (var g in byTarget.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var mods = g.Select(x => x.Mod).Distinct().ToList();
            if (mods.Count < 2) continue;
            var transpilers = g.Where(x => x.Patch.Kind == PatchKind.Transpiler).Select(x => x.Mod).Distinct().ToList();
            var skippers = g.Where(x => x.Patch.CanSkip).Select(x => x.Mod).Distinct().ToList();
            var method = Short(g.Key);
            if (transpilers.Count >= 2)
                list.Add(new InsightItem(InsightKind.PatchConflict, ProblemLevel.Warning, transpilers,
                    $"{Names(transpilers, name)} all rewrite {method}. If errors name it, try them in another order or without one of them."));
            else if (skippers.Count > 0)
                list.Add(new InsightItem(InsightKind.PatchConflict, ProblemLevel.Info, mods,
                    $"{Names(skippers, name)} can stop {method} from running, which also stops what {Names(mods.Except(skippers).ToList(), name)} add{(mods.Except(skippers).Count() == 1 ? "s" : "")} to it."));
        }
        return list;
    }

    /// <summary>Active mods that patch per-tick methods, most such patches first.</summary>
    public static List<InsightItem> TickPatches(IReadOnlyDictionary<string, List<HarmonyPatchInfo>> patches, IReadOnlyCollection<string> active, Func<string, string> name)
    {
        var on = active.ToHashSet();
        return patches.Where(p => on.Contains(p.Key))
            .Select(p => (Mod: p.Key, Hot: p.Value.Where(x => IsHot(x.Target)).Select(x => x.Target).Distinct().ToList()))
            .Where(x => x.Hot.Count > 0)
            .OrderByDescending(x => x.Hot.Count).ThenBy(x => name(x.Mod), StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new InsightItem(InsightKind.TickPatches, x.Hot.Count >= 5 ? ProblemLevel.Warning : ProblemLevel.Info, new[] { x.Mod },
                $"{name(x.Mod)} changes {x.Hot.Count} method{(x.Hot.Count == 1 ? "" : "s")} the game runs every tick: {string.Join(", ", x.Hot.Take(5).Select(Short))}{(x.Hot.Count > 5 ? ", ..." : "")}."))
            .ToList();
    }

    /// <summary>What a mod folder holds that the game loads: textures (all versions' folders, as a rough upper bound), Defs files and DLLs.</summary>
    public static ModWeight Weigh(ModManifest mod)
    {
        long tex = 0, code = 0;
        int texFiles = 0, defs = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(mod.Folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 12 }))
            {
                var rel = Path.GetRelativePath(mod.Folder, f).Replace('\\', '/');
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (rel.Contains("Textures/", StringComparison.OrdinalIgnoreCase) && ext is ".png" or ".jpg" or ".jpeg" or ".dds" or ".psd")
                {
                    texFiles++;
                    tex += new FileInfo(f).Length;
                }
                else if (ext == ".xml" && (rel.Contains("Defs/", StringComparison.OrdinalIgnoreCase) || rel.Contains("Patches/", StringComparison.OrdinalIgnoreCase))) defs++;
                else if (ext == ".dll" && rel.Contains("Assemblies/", StringComparison.OrdinalIgnoreCase)) code += new FileInfo(f).Length;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new ModWeight(mod.Key, tex, texFiles, defs, code);
    }

    /// <summary>The active mods that add most to loading, when they add a noticeable amount (over 100 MB of textures or 300 definition files).</summary>
    public static List<InsightItem> Heavy(IEnumerable<ModWeight> weights, IReadOnlyCollection<string> active, Func<string, string> name)
    {
        var on = active.ToHashSet();
        return weights.Where(w => on.Contains(w.Mod) && (w.TextureBytes > 100L << 20 || w.DefFiles > 300))
            .OrderByDescending(w => w.TextureBytes + w.DefFiles * (256L << 10))
            .Take(15)
            .Select(w => new InsightItem(InsightKind.LoadWeight, w.TextureBytes > 500L << 20 ? ProblemLevel.Warning : ProblemLevel.Info, new[] { w.Mod },
                $"{name(w.Mod)}: {w.TextureBytes / (1 << 20):N0} MB in {w.TextureFiles:N0} textures and {w.DefFiles:N0} definition files, so it adds to loading time."))
            .ToList();
    }

    /// <summary>The notes the player's rules file has for active mods.</summary>
    public static List<InsightItem> Notes(SortRules? rules, IReadOnlyCollection<string> active, Func<string, string> name) =>
        rules == null ? new() : active.Distinct()
            .Select(k => (k, Note: rules.For(k)?.Note))
            .Where(x => !string.IsNullOrWhiteSpace(x.Note))
            .Select(x => new InsightItem(InsightKind.RulesNote, ProblemLevel.Info, new[] { x.k }, $"{name(x.k)}: {x.Note!.Trim()}"))
            .ToList();

    /// <summary>
    /// The XML simulation's findings as hints: defs one mod replaces of another's, patches that undo another mod's
    /// patch, patches that find nothing and unreadable files. Overrides of the game's own defs are only listed when a
    /// mod replaces many of them, since replacing a few is a normal way to change the game.
    /// </summary>
    public static List<InsightItem> Xml(IEnumerable<RimWorld.XmlFinding> findings, Func<string, string> name, IReadOnlyCollection<string> official)
    {
        var list = new List<InsightItem>();
        foreach (var f in findings)
        {
            var targets = Sample(f.Targets);
            var n = f.Targets.Count;
            switch (f.Kind)
            {
                case RimWorld.XmlFindingKind.DefOverride when official.Contains(f.Mods[0]):
                    if (n >= 20)
                        list.Add(new InsightItem(InsightKind.XmlClash, ProblemLevel.Info, new[] { f.Mods[1] },
                            $"{name(f.Mods[1])} replaces {n} of {name(f.Mods[0])}'s defs outright ({targets}), so other mods' changes to those may not stick."));
                    break;
                case RimWorld.XmlFindingKind.DefOverride:
                    list.Add(new InsightItem(InsightKind.XmlClash, ProblemLevel.Warning, f.Mods,
                        $"{name(f.Mods[1])} replaces {n} def{(n == 1 ? "" : "s")} {name(f.Mods[0])} also defines ({targets}). {name(f.Mods[1])} loads later, so {name(f.Mods[0])}'s version{(n == 1 ? " is" : "s are")} ignored."));
                    break;
                case RimWorld.XmlFindingKind.PatchOverwrite:
                    list.Add(new InsightItem(InsightKind.XmlClash, ProblemLevel.Warning, f.Mods,
                        $"{name(f.Mods[1])} {f.Detail} what {name(f.Mods[0])} changed in {n} place{(n == 1 ? "" : "s")} ({targets}). {name(f.Mods[1])} loads later, so its change wins."));
                    break;
                case RimWorld.XmlFindingKind.PatchFails:
                    list.Add(new InsightItem(InsightKind.XmlError, ProblemLevel.Warning, f.Mods,
                        $"{name(f.Mods[0])} has {n} patch{(n == 1 ? "" : "es")} in {f.Detail} that find{(n == 1 ? "s" : "")} nothing to change, so the game logs a red error: {targets}. Often it's meant for a mod or version you don't have."));
                    break;
                case RimWorld.XmlFindingKind.BrokenFile:
                    list.Add(new InsightItem(InsightKind.XmlError, ProblemLevel.Warning, f.Mods,
                        $"{name(f.Mods[0])}: {f.Targets[0]} is not valid XML ({f.Detail}), so the game skips it with an error."));
                    break;
                case RimWorld.XmlFindingKind.CustomPatches when !official.Contains(f.Mods[0]):
                    list.Add(new InsightItem(InsightKind.XmlClash, ProblemLevel.Info, f.Mods,
                        $"{name(f.Mods[0])} uses patch operations from a mod's own code ({targets}); those can't be checked here."));
                    break;
            }
        }
        return list;
    }

    static string Sample(IReadOnlyList<string> items) => string.Join("; ", items.Take(3)) + (items.Count > 3 ? $"; and {items.Count - 3} more" : "");

    /// <summary>"Pawn.Tick" from "Verse.Pawn.Tick".</summary>
    static string Short(string target)
    {
        var parts = target.Replace("..ctor", ".#ctor").Replace("..cctor", ".#cctor").Split('.');
        return (parts.Length >= 2 ? parts[^2] + "." + parts[^1] : target).Replace('#', '.');
    }

    static string Names(IReadOnlyList<string> mods, Func<string, string> name)
    {
        var n = mods.Select(name).ToList();
        return n.Count <= 1 ? string.Concat(n) : string.Join(", ", n.Take(n.Count - 1)) + " and " + n[^1];
    }
}
