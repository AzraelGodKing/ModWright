namespace SteamCmdHelper;

/// <summary>
/// Changes the version to upload of several mods at once: "patch", "minor" or "major" raise each mod's own version,
/// a number sets them all to it. One changelog line goes to each mod whose layout needs an entry for the new version.
/// Used by "Change the version of ticked mods..." in the window and the "version" command.
/// </summary>
public static class VersionBatch
{
    /// <summary>"patch", "minor" or "major" as a <see cref="ModScanner.Bump"/>; null for a version number.</summary>
    public static ModScanner.Bump? PartOf(string spec) => spec.Trim().ToLowerInvariant() switch
    {
        "patch" => ModScanner.Bump.Patch, "minor" => ModScanner.Bump.Minor, "major" => ModScanner.Bump.Major, _ => null,
    };

    /// <summary>The version <paramref name="m"/> gets from <paramref name="spec"/>, or null when its version is not a number to raise.</summary>
    public static string? NewVersion(ModInfo m, string spec) =>
        PartOf(spec) is { } p ? ModScanner.NextVersion(m.Version, p) : spec.Trim().TrimStart('v', 'V');

    /// <summary>
    /// Sets each mod's version from <paramref name="spec"/>, saying what happened to each. With a
    /// <paramref name="note"/>, mods that need a changelog entry for the new version get it. Returns false when any
    /// mod could not be changed.
    /// </summary>
    public static bool Apply(IEnumerable<ModInfo> mods, ProjectConfig cfg, string spec, string? note, Action<string> say)
    {
        var ok = true;
        foreach (var m in mods)
        {
            var version = NewVersion(m, spec);
            if (string.IsNullOrEmpty(version)) { say($"{m.Folder}: its version \"{m.Version}\" is not a number, so it cannot be raised. Give the new version instead."); ok = false; continue; }
            if (version == m.Version) { say($"{m.Folder}: already {version}."); continue; }
            var error = ModScanner.SetVersion(m, cfg, version);
            if (error != null) { say($"{m.Folder}: {error}"); ok = false; continue; }
            var old = m.Version;
            m.Version = version;
            say($"{m.Folder}: version {(old.Length > 0 ? old : "?")} -> {version}.");
            if (!ModScanner.NeedsChangelogEntry(m, cfg)) continue;
            if (string.IsNullOrWhiteSpace(note))
            {
                say($"  {cfg.Changelog} has no {version} entry yet, and the upload needs one.");
                continue;
            }
            var noteError = ModScanner.AddChangelogEntry(m, cfg, version, note);
            if (noteError != null) { say("  " + noteError); ok = false; }
            else say($"  Added the {version} entry to {cfg.Changelog}.");
        }
        return ok;
    }
}
