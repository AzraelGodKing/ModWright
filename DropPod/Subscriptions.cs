namespace SteamCmdHelper.DropPod;

/// <summary>
/// Workshop mods through the Steam client's subscriptions rather than SteamCMD copies in the Mods folder: SteamCMD
/// copies never update by themselves and Steam doesn't know about them, while subscribed items are kept current by
/// Steam. The Steam calls are in the child process (<see cref="WorkshopExtras"/>); the planning is here.
/// </summary>
public static class Subscriptions
{
    /// <summary>How long to wait for Steam to download newly subscribed items: half a minute plus 15 seconds each, at most 10 minutes.</summary>
    public static int WaitSeconds(int items) => Math.Min(600, 30 + 15 * Math.Max(0, items));

    /// <summary>One line for the player about a subscribe run.</summary>
    public static string Describe(int asked, int subscribed, int installed)
    {
        var s = $"Subscribed to {subscribed} of {asked} Workshop item{(asked == 1 ? "" : "s")} through Steam, which keeps them up to date. ";
        s += installed == subscribed
            ? "All of them are downloaded and ticked."
            : $"{installed} {(installed == 1 ? "is" : "are")} downloaded and ticked; Steam is still downloading the rest, so press Refresh in a moment to tick them too.";
        return s + " Press Save to game to use the list.";
    }

    /// <summary>
    /// Copies in the game's Mods folder that came from the Workshop (they carry a Workshop id) and have no subscribed
    /// copy yet: what "Turn downloaded copies into subscriptions" offers. Linked mods (the Launch Pad's live play) and
    /// folders that are someone's own work in progress (a .git folder) are left out.
    /// </summary>
    public static List<ModManifest> DownloadedCopies(LibraryScan scan)
    {
        var subscribed = scan.Mods.Where(m => m.Source == ModSource.Workshop && m.WorkshopId != null).Select(m => m.WorkshopId!).ToHashSet();
        return scan.Mods.Where(m => m.Source == ModSource.Local && m.WorkshopId is { Length: > 0 } id && id.All(char.IsDigit) && !subscribed.Contains(id)
                                    && !FolderLinks.IsLink(m.Folder) && !Directory.Exists(Path.Combine(m.Folder, ".git")))
            .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The local copies whose Workshop item is now subscribed and downloaded, so the copy can go.</summary>
    public static List<ModManifest> Replaced(IEnumerable<ModManifest> copies, IEnumerable<string> installed)
    {
        var done = installed.ToHashSet();
        return copies.Where(m => m.WorkshopId != null && done.Contains(m.WorkshopId)).ToList();
    }
}
