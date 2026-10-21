using System.Runtime.InteropServices;

namespace SteamCmdHelper;

/// <summary>
/// Writes down an unexpected error so it can be reported: the program version, Windows and .NET versions, and the full
/// exception. Reports go to %LocalAppData%\SteamCmdHelper\crashes (the newest <see cref="Keep"/> are kept); nothing is
/// sent anywhere unless the user presses Report it and sends the request form.
/// </summary>
public static class CrashReport
{
    public const int Keep = 20;

    public static string Folder => Path.Combine(AppSettings.Folder, "crashes");

    /// <summary>The report text for <paramref name="e"/>.</summary>
    public static string Build(Exception e, string version, DateTime now) =>
        $"Modwright {version}\n" +
        $"Time: {now:yyyy-MM-dd HH:mm:ss zzz}\n" +
        $"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n" +
        $".NET: {RuntimeInformation.FrameworkDescription}\n\n" +
        e;

    /// <summary>A one-line title for a bug report: the error's type and the start of its message.</summary>
    public static string Summary(Exception e)
    {
        var inner = e is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerExceptions[0] : e;
        var msg = inner.Message.Split('\n')[0].Trim();
        var line = $"Crash: {inner.GetType().Name}" + (msg.Length > 0 ? ": " + msg : "");
        return line.Length > 120 ? line[..117] + "..." : line;
    }

    /// <summary>Saves the report and returns its path, or null when it could not be written.</summary>
    public static string? Save(string text, string? folder = null, DateTime? now = null)
    {
        try
        {
            folder ??= Folder;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"crash-{now ?? DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            foreach (var old in new DirectoryInfo(folder).GetFiles("crash-*.txt").OrderByDescending(f => f.Name).Skip(Keep))
                try { old.Delete(); } catch { }
            return path;
        }
        catch { return null; }
    }
}
