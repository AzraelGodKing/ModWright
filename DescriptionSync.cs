using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>
/// Keeps a Workshop item's description in step with the mod's description file (the layout's SteamDescriptionFile).
/// A file differs when Steam's public page shows other text, or, for items Steam won't show (private, friends only),
/// when the file changed since this PC last sent it.
/// </summary>
public static class DescriptionSync
{
    /// <summary>What to do when a mod's description file differs from Steam: ask, always send it, or never.</summary>
    public const string Ask = "Ask", Always = "Always", Never = "Never";

    public static string DefaultFile => Path.Combine(AppSettings.Folder, "descriptions.json");

    static readonly object Gate = new();

    /// <summary>Text compared the way Steam keeps it: one kind of line break, no surrounding blank space.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text))));

    /// <summary>
    /// True when <paramref name="file"/> should be sent: it differs from <paramref name="onSteam"/> when Steam's text is
    /// known, else from what was last sent (<paramref name="lastSentHash"/>). False when neither is known, since there
    /// is then nothing to say it changed.
    /// </summary>
    public static bool Differs(string file, string? onSteam, string? lastSentHash)
    {
        if (Normalize(file).Length == 0) return false;
        if (onSteam != null) return Normalize(file) != Normalize(onSteam);
        return lastSentHash != null && !lastSentHash.Equals(Hash(file), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The hash of the description last sent to each item, by Workshop id.</summary>
    public static Dictionary<string, string> Load(string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultFile;
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }
    }

    /// <summary>Remembers the description sent to an item. Never throws.</summary>
    public static void Remember(string workshopId, string description, string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultFile;
                var all = Load(p);
                all[workshopId] = Hash(description);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(all));
            }
            catch { }
        }
    }

    /// <summary>
    /// The mods (already on Steam, with a description file and no description change waiting) whose file differs from
    /// Steam, with the file's text. <paramref name="fetch"/> reads the description on Steam (null when it can't).
    /// </summary>
    public static async Task<List<(ModInfo Mod, string Text)>> FindChangedAsync(IEnumerable<ModInfo> mods, ProjectConfig cfg,
        Func<string, CancellationToken, Task<string?>> fetch, string? path, CancellationToken ct, string? pagesPath = null)
    {
        var sent = Load(path);
        var changed = new List<(ModInfo, string)>();
        foreach (var m in mods.Where(m => m.OnSteam))
        {
            if (WorkshopPage.Get(m.Dir, pagesPath)?.Description != null) continue;
            var file = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.SteamDescriptionFile));
            if (string.IsNullOrWhiteSpace(cfg.SteamDescriptionFile) || !File.Exists(file)) continue;
            string text;
            try { text = await File.ReadAllTextAsync(file, ct); } catch (IOException) { continue; }
            var onSteam = await fetch(m.WorkshopId!, ct);
            if (Differs(text, onSteam, sent.GetValueOrDefault(m.WorkshopId!))) changed.Add((m, Normalize(text)));
        }
        return changed;
    }

    /// <summary>The standard way to read an item's description: Steam's public details.</summary>
    public static async Task<string?> FetchFromSteam(string id, CancellationToken ct) => (await WorkshopPage.FetchAsync(id, ct))?.Description;

    /// <summary>Puts each mod's description file into its pending Workshop page changes, so it goes out with the upload.</summary>
    public static void Stage(IEnumerable<(ModInfo Mod, string Text)> changed, string? pagesPath = null)
    {
        foreach (var (m, text) in changed)
        {
            var edit = WorkshopPage.Get(m.Dir, pagesPath) ?? new WorkshopPageEdit();
            edit.Description = text;
            WorkshopPage.Set(m.Dir, edit, pagesPath);
        }
    }
}
