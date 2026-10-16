using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>A saved upload log: its file, when the run started, and what it was ("upload Better Storage").</summary>
public sealed record RunLog(string Path, DateTime Time, string Label);

/// <summary>
/// Every upload run's log, saved on its own so a failure can be read back later even after the window was closed.
/// One file per run, named by its start time and what it uploaded; only the newest <see cref="Keep"/> are kept.
/// </summary>
public static class RunLogs
{
    /// <summary>How many run logs are kept; older ones are deleted as new ones are saved.</summary>
    public const int Keep = 50;

    const string TimeFormat = "yyyy-MM-dd_HH-mm-ss";

    public static string DefaultDir => Path.Combine(AppSettings.Folder, "logs");

    static readonly Regex Unsafe = new(@"[\\/:*?""<>|\x00-\x1F]+");

    /// <summary>The file name for a run: its start time, then the label made safe for a file name and kept short.</summary>
    public static string FileName(DateTime time, string label)
    {
        var safe = Regex.Replace(Unsafe.Replace(label, " "), @"\s{2,}", " ").Trim().TrimEnd('.');
        if (safe.Length > 80) safe = safe[..80].TrimEnd() + "...";
        return time.ToString(TimeFormat, CultureInfo.InvariantCulture) + (safe.Length > 0 ? " " + safe : "") + ".log";
    }

    /// <summary>A label for a run of these mods: "upload Better Storage, Faster Hauling and 3 more".</summary>
    public static string Label(string what, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return what;
        var shown = string.Join(", ", names.Take(3));
        return $"{what} {shown}{(names.Count > 3 ? $" and {names.Count - 3} more" : "")}";
    }

    /// <summary>
    /// A new, unused path for a run's log in <paramref name="dir"/> (made when missing), after deleting the oldest logs
    /// so that, with this one, at most <see cref="Keep"/> remain.
    /// </summary>
    public static string NewPath(string dir, string label, DateTime time)
    {
        Directory.CreateDirectory(dir);
        Prune(dir, Keep - 1);
        var name = FileName(time, label);
        var path = Path.Combine(dir, name);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(dir, name[..^4] + $" ({n}).log");
        return path;
    }

    /// <summary>Saves a run's log. Returns its path, or null when it could not be written (a log is never worth an error).</summary>
    public static string? Save(string dir, string label, DateTime time, string text)
    {
        try
        {
            var path = NewPath(dir, label, time);
            File.WriteAllText(path, text.EndsWith('\n') ? text : text + Environment.NewLine);
            return path;
        }
        catch { return null; }
    }

    /// <summary>The saved logs, newest first. Files this class did not name are left out.</summary>
    public static List<RunLog> List(string dir)
    {
        var logs = new List<RunLog>();
        if (!Directory.Exists(dir)) return logs;
        foreach (var file in Directory.EnumerateFiles(dir, "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length < TimeFormat.Length) continue;
            if (!DateTime.TryParseExact(name[..TimeFormat.Length], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;
            logs.Add(new RunLog(file, time, name[TimeFormat.Length..].Trim()));
        }
        // Same-second runs keep their order through the " (2)" suffix.
        return logs.OrderByDescending(l => l.Time).ThenByDescending(l => Path.GetFileName(l.Path), StringComparer.Ordinal).ToList();
    }

    /// <summary>Deletes the oldest logs until at most <paramref name="keep"/> remain.</summary>
    public static void Prune(string dir, int keep = Keep)
    {
        foreach (var old in List(dir).Skip(Math.Max(0, keep)))
            try { File.Delete(old.Path); } catch { }
    }
}
