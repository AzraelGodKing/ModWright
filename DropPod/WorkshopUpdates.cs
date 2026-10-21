using System.Text.Json;

namespace SteamCmdHelper.DropPod;

/// <param name="Local">When the installed copy was last updated on Steam, as far as this PC knows.</param>
/// <param name="Remote">When the item was last updated on Steam now.</param>
public sealed record ModUpdate(ModManifest Mod, DateTimeOffset Local, DateTimeOffset Remote);

/// <summary>
/// Which installed Workshop mods have a newer version on Steam, and getting Workshop items with SteamCMD into the game's
/// Mods folder (for missing dependencies, and for players who don't subscribe through the Steam client).
/// </summary>
public static class WorkshopUpdates
{
    /// <summary>
    /// What the Steam client says it installed: steamapps/workshop/appworkshop_&lt;appid&gt;.acf lists each item with the
    /// "timeupdated" of the copy on disk. Empty when there is no such file.
    /// </summary>
    public static Dictionary<string, DateTimeOffset> SteamClientTimes(string? workshopFolder, string appId)
    {
        var times = new Dictionary<string, DateTimeOffset>();
        if (workshopFolder == null) return times;
        var acf = Path.Combine(workshopFolder, "..", "..", $"appworkshop_{appId}.acf");
        try
        {
            if (!File.Exists(acf)) return times;
            var installed = KeyValues.Parse(File.ReadAllText(acf)).Block("AppWorkshop")?.Block("WorkshopItemsInstalled");
            if (installed == null) return times;
            foreach (var (id, v) in installed)
                if (v is KeyValues item && long.TryParse(item.Value("timeupdated"), out var t) && t > 0)
                    times[id] = DateTimeOffset.FromUnixTimeSeconds(t);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return times;
    }

    /// <summary>The mods whose Steam copy is newer than the installed one (by more than a minute, so clock noise is ignored).</summary>
    public static List<ModUpdate> Outdated(IEnumerable<ModManifest> mods, IReadOnlyDictionary<string, WorkshopItem> remote, IReadOnlyDictionary<string, DateTimeOffset> local)
    {
        var list = new List<ModUpdate>();
        foreach (var m in mods)
        {
            if (m.WorkshopId is not { } id || !remote.TryGetValue(id, out var r) || r.Updated is not { } now || !local.TryGetValue(id, out var have)) continue;
            if (now - have > TimeSpan.FromMinutes(1)) list.Add(new ModUpdate(m, have, now));
        }
        return list.OrderBy(u => u.Mod.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Downloads a Workshop item with SteamCMD and puts it in <paramref name="modsFolder"/>/&lt;id&gt;, replacing an older
    /// copy there only once the new one is complete. Returns the folder, or throws with SteamCMD's reason.
    /// </summary>
    public static async Task<string> InstallAsync(string steamCmd, string account, string appId, string itemId, string modsFolder,
        DownloadRecord record, Action<string>? onLine, CancellationToken ct, Func<string, string, string, string, Action<string>?, CancellationToken, Task<DownloadResult>>? download = null)
    {
        download ??= WorkshopDownload.DownloadAsync;
        var r = await download(steamCmd, account, appId, itemId, onLine, ct);
        if (!r.Ok || r.Folder == null) throw new IOException(r.Message);
        var dest = Path.Combine(modsFolder, itemId);
        var incoming = dest + ".incoming";
        var old = dest + ".old";
        try
        {
            if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
            WorkshopDownload.CopyTo(r.Folder, incoming);
            // Remember where it came from, as the Steam client's copies do, so the scan knows its Workshop id.
            var idFile = SafeFiles.Find(incoming, "About/PublishedFileId.txt") ?? Path.Combine(incoming, "About", "PublishedFileId.txt");
            if (Directory.Exists(Path.GetDirectoryName(idFile))) File.WriteAllText(idFile, itemId);
            if (Directory.Exists(dest)) { if (Directory.Exists(old)) Directory.Delete(old, true); Directory.Move(dest, old); }
            Directory.Move(incoming, dest);
            if (Directory.Exists(old)) Directory.Delete(old, true);
        }
        finally
        {
            try { if (Directory.Exists(incoming)) Directory.Delete(incoming, true); } catch { }
            if (!Directory.Exists(dest) && Directory.Exists(old)) Directory.Move(old, dest); // put the old copy back
            WorkshopDownload.Forget(steamCmd, r.Folder);
        }
        record.Set(itemId, DateTimeOffset.UtcNow);
        return dest;
    }
}

/// <summary>When this app last downloaded each Workshop item into a Mods folder, so updates can be told apart.</summary>
public sealed class DownloadRecord
{
    readonly string _file;
    readonly Dictionary<string, DateTimeOffset> _times;

    public DownloadRecord(string file)
    {
        _file = file;
        try { _times = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(file)) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { _times = new(); }
    }

    public IReadOnlyDictionary<string, DateTimeOffset> Times => _times;

    public void Set(string id, DateTimeOffset at)
    {
        _times[id] = at;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        SafeFiles.WriteWithBackup(_file, JsonSerializer.Serialize(_times), keep: 0);
    }
}
