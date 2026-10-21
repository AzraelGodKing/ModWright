using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod.RimWorld;

/// <summary>How much a lint finding matters: Block stops an upload, Warn is shown and lets it through.</summary>
public enum LintLevel { Warn, Block }

/// <param name="Rule">A short stable id ("about-no-author"), for tests and scripts.</param>
public sealed record LintFinding(string Rule, LintLevel Level, string Message);

/// <summary>
/// Checks a mod's About.xml (read by the shared <see cref="RimWorldAbout"/> parser) for what players would trip over:
/// a missing or malformed packageId, missing name, author, description or supportedVersions, dependencies players can't
/// find, and load-order rules that contradict themselves. Used by the upload flow (<see cref="RimWorldLint"/>) and
/// by the Drop Pod.
/// </summary>
public static class AboutLint
{
    static readonly Regex VersionForm = new(@"^\d+\.\d+$");

    public static List<LintFinding> Check(ModManifest m)
    {
        var f = new List<LintFinding>();
        void Warn(string rule, string msg) => f.Add(new LintFinding(rule, LintLevel.Warn, msg));

        var versions = m.SupportedVersions.Select(GameVersion.Short).ToList();
        // Only RimWorld 1.0 loaded mods without one.
        if (m.Id.Length == 0 && versions.Count > 0 && versions.All(v => v == "1.0"))
            Warn("about-no-packageid", "About.xml has no <packageId>; RimWorld 1.1 and later need one (such as YourName.ModName)");
        else if (m.Id.Length == 0)
            f.Add(new LintFinding("about-no-packageid", LintLevel.Block, "About.xml has no <packageId> (such as YourName.ModName): RimWorld refuses to load a mod without one"));
        else if (!RimWorldIds.IsValid(m.Id))
            Warn("about-bad-packageid", $"packageId \"{m.Id}\" is not valid for RimWorld: use letters and digits with a dot, such as YourName.ModName");

        if (m.Name.Length == 0) Warn("about-no-name", "About.xml has no <name>, so players see the folder name in their mod list");
        if (m.Authors.Count == 0) Warn("about-no-author", "About.xml has no <author> (or <authors>), so players can't see who made it");
        if (m.Description.Length == 0) Warn("about-no-description", "About.xml has no <description>, so the in-game mod list shows nothing about it");
        if (m.SupportedVersions.Count == 0)
            Warn("about-no-versions", "About.xml has no <supportedVersions>: RimWorld 1.1 and later mark the mod as made for another version");
        foreach (var v in m.SupportedVersions.Where(v => !VersionForm.IsMatch(v)))
            Warn("about-version-form", $"supportedVersions lists \"{v}\": RimWorld compares versions as major.minor, such as {GameVersion.Short(v)}");

        foreach (var d in m.Dependencies.Everything)
        {
            var name = d.DisplayName is { Length: > 0 } n ? $" ({n})" : "";
            if (d.Id.Length == 0)
                Warn("dep-no-packageid", $"a modDependencies entry{name} has no packageId, so RimWorld can't check it");
            else if (d.Url == null)
                Warn("dep-no-link", $"the dependency {d.Id}{name} has no steamWorkshopUrl or downloadUrl, so players who don't have it get no link to it");
            else if (!RimWorldIds.IsValid(d.Id))
                Warn("dep-bad-packageid", $"the dependency \"{d.Id}\" is not a valid packageId, so RimWorld never finds it");
        }

        var self = m.Id.Length > 0 ? m.Key : null;
        foreach (var (list, label) in Lists(m))
            foreach (var id in list.Everything)
            {
                if (self != null && ModManifest.KeyOf(id) == self) Warn("rule-self", $"{label} names the mod itself ({id})");
                else if (!RimWorldIds.IsValid(id)) Warn("rule-not-packageid", $"{label} lists \"{id}\", which is not a packageId (such as brrainz.harmony), so RimWorld ignores it");
            }
        if (self != null && m.Dependencies.Everything.Any(d => d.Key == self)) Warn("rule-self", $"modDependencies names the mod itself ({m.Id})");

        var after = Keys(m.LoadAfter, m.ForceLoadAfter);
        foreach (var id in Keys(m.LoadBefore, m.ForceLoadBefore).Where(after.Contains))
            Warn("rule-before-and-after", $"{id} is in both loadAfter and loadBefore, so the mod can't be placed");
        var incompatible = Keys(m.Incompatible);
        foreach (var d in m.Dependencies.Everything.Where(d => d.Id.Length > 0 && incompatible.Contains(d.Key)).DistinctBy(d => d.Key))
            Warn("rule-needs-incompatible", $"{d.Id} is both a dependency and in incompatibleWith");

        foreach (var (byVersion, label) in new (IEnumerable<string>, string)[]
        {
            (m.Dependencies.ByVersion.Keys, "modDependenciesByVersion"), (m.LoadAfter.ByVersion.Keys, "loadAfterByVersion"),
            (m.LoadBefore.ByVersion.Keys, "loadBeforeByVersion"), (m.Incompatible.ByVersion.Keys, "incompatibleWithByVersion"),
        })
            foreach (var v in byVersion.Where(v => versions.Count > 0 && !versions.Contains(v)))
                Warn("byversion-unsupported", $"{label} has an entry for {v}, but supportedVersions does not list {v}");
        return f;
    }

    static IEnumerable<(VersionedList<string> List, string Label)> Lists(ModManifest m) => new[]
    {
        (m.LoadAfter, "loadAfter"), (m.LoadBefore, "loadBefore"), (m.ForceLoadAfter, "forceLoadAfter"),
        (m.ForceLoadBefore, "forceLoadBefore"), (m.Incompatible, "incompatibleWith"),
    };

    static HashSet<string> Keys(params VersionedList<string>[] lists) =>
        lists.SelectMany(l => l.Everything).Select(ModManifest.KeyOf).ToHashSet();
}
