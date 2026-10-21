namespace SteamCmdHelper.DropPod;

/// <summary>One save game: when it was made, on which game version, and the mods it was played with (in load order).</summary>
/// <param name="Mods">The save's mods as a modlist; empty when it could not be read.</param>
/// <param name="Error">Why the mod list could not be read (a damaged or unknown file), or null.</param>
public sealed record SaveInfo(string Path, string Name, DateTime Modified, long Size, string GameVersion, Modlist Mods, string? Error = null);

/// <summary>How the active mods differ from a save's.</summary>
/// <param name="Missing">Mods the save used that are not installed.</param>
/// <param name="Inactive">Mods the save used that are installed but not active.</param>
/// <param name="Extra">Active mods (keys) the save did not use.</param>
/// <param name="OrderDiffers">The mods both share are in another order.</param>
public sealed record SaveDiff(List<ModlistEntry> Missing, List<ModlistEntry> Inactive, List<string> Extra, bool OrderDiffers)
{
    public bool Matches => Missing.Count == 0 && Inactive.Count == 0 && Extra.Count == 0 && !OrderDiffers;
}

/// <summary>Compares saves with the active mods, and lists a folder's saves newest first.</summary>
public static class Saves
{
    public static List<SaveInfo> List(IGameModule game, GameInstall install, CancellationToken ct = default)
    {
        var list = new List<SaveInfo>();
        foreach (var f in game.SaveFiles(install))
        {
            ct.ThrowIfCancellationRequested();
            list.Add(game.ReadSave(f));
        }
        return list.OrderByDescending(s => s.Modified).ToList();
    }

    public static SaveDiff Compare(SaveInfo save, IReadOnlyList<string> active, LibraryScan scan, IGameModule game)
    {
        var byId = scan.ById();
        var activeKeys = active.Select(game.ActiveKey).Distinct().ToList();
        var activeSet = activeKeys.ToHashSet();
        var saveKeys = new List<string>();
        var missing = new List<ModlistEntry>();
        var inactive = new List<ModlistEntry>();
        foreach (var e in save.Mods.Mods)
        {
            var k = game.ActiveKey(e.Id);
            if (saveKeys.Contains(k)) continue;
            saveKeys.Add(k);
            if (!byId.ContainsKey(k)) missing.Add(e);
            else if (!activeSet.Contains(k)) inactive.Add(e);
        }
        var saveSet = saveKeys.ToHashSet();
        var extra = activeKeys.Where(k => !saveSet.Contains(k)).ToList();
        var shared = saveKeys.Where(activeSet.Contains).ToList();
        var order = !shared.SequenceEqual(activeKeys.Where(saveSet.Contains));
        return new SaveDiff(missing, inactive, extra, order);
    }
}
