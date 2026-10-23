using System.Globalization;

namespace SteamCmdHelper;

/// <summary>One automatic backup of the settings.</summary>
public sealed record SettingsBackup(string Path, DateTime Made, long Bytes);

/// <summary>
/// A copy of the settings, upload history and the Drop Pod's modlists, notes and history, made once a day when
/// Modwright starts (the newest 14 are kept), so a bad change or a broken file can be undone from Options > About >
/// Your data. It holds what an export holds (<see cref="SettingsTransfer"/>), so restoring is an import.
/// </summary>
public static class SettingsBackups
{
    public const int Keep = 14;
    const string Prefix = "settings-";

    public static string DefaultDir => Path.Combine(AppSettings.Folder, "backups", "daily");

    /// <summary>The backups, newest first.</summary>
    public static List<SettingsBackup> List(string dir)
    {
        if (!Directory.Exists(dir)) return new();
        return Directory.GetFiles(dir, Prefix + "*.zip")
            .Select(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f)[Prefix.Length..], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? new SettingsBackup(f, t, new FileInfo(f).Length) : null)
            .OfType<SettingsBackup>()
            .OrderByDescending(b => b.Made).ToList();
    }

    /// <summary>
    /// Makes today's backup unless one was made in the last 20 hours or there are no settings yet, and drops all but the
    /// newest <see cref="Keep"/>. Returns the new backup's path, or null when none was due.
    /// </summary>
    public static string? MakeIfDue(string dataFolder, string dir, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        if (!File.Exists(Path.Combine(dataFolder, "settings.json"))) return null;
        var list = List(dir);
        if (list.Count > 0 && at - list[0].Made < TimeSpan.FromHours(20)) return null;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Prefix}{at:yyyyMMdd-HHmmss}.zip");
        var tmp = path + ".tmp";
        SettingsTransfer.Export(tmp, dataFolder, dropPod: true);
        File.Move(tmp, path, overwrite: true);
        foreach (var old in List(dir).Skip(Keep))
            try { File.Delete(old.Path); } catch (IOException) { }
        return path;
    }
}
