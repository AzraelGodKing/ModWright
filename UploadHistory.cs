using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>One upload made from this PC.</summary>
public sealed record UploadEntry(
    DateTimeOffset Time,
    string WorkshopId,
    string Mod,
    string Version,
    string ChangeNote,
    bool Created = false,
    string AppId = "");

/// <summary>
/// A log of every upload made from this PC: when, which version and the change note sent. Kept in
/// %LocalAppData%\SteamCmdHelper\history.json, newest last, trimmed to the most recent <see cref="Keep"/> entries.
/// </summary>
public static class UploadHistory
{
    public const int Keep = 1000;

    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamCmdHelper", "history.json");

    static readonly object Gate = new();
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static List<UploadEntry> Load(string? file = null)
    {
        lock (Gate)
        {
            try
            {
                var p = file ?? DefaultFile;
                if (File.Exists(p)) return JsonSerializer.Deserialize<List<UploadEntry>>(File.ReadAllText(p)) ?? new();
            }
            catch { /* a damaged file reads as empty rather than stopping the app */ }
            return new();
        }
    }

    public static void Add(UploadEntry entry, string? file = null)
    {
        lock (Gate)
        {
            try
            {
                var p = file ?? DefaultFile;
                var all = Load(p);
                all.Add(entry);
                if (all.Count > Keep) all.RemoveRange(0, all.Count - Keep);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                var tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(all, Opts));
                File.Move(tmp, p, true);
            }
            catch { /* a convenience; never fail an upload over it */ }
        }
    }

    /// <summary>Uploads of one Workshop item, newest first.</summary>
    public static List<UploadEntry> For(IEnumerable<UploadEntry> all, string workshopId) =>
        all.Where(e => e.WorkshopId == workshopId).OrderByDescending(e => e.Time).ToList();

    /// <summary>The newest upload of each Workshop item.</summary>
    public static Dictionary<string, UploadEntry> Latest(IEnumerable<UploadEntry> all) =>
        all.GroupBy(e => e.WorkshopId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.Time)!);
}
