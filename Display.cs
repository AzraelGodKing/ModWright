namespace SteamCmdHelper;

/// <summary>What kind of line the log shows; decides its colour and which log filters keep it.</summary>
public enum LogKind { Plain, Note, Ok, Warn, Error }

/// <summary>The log's filter drop-down.</summary>
public enum LogFilter { All, WarningsAndErrors, ErrorsOnly }

/// <summary>Display choices that need no WinForms (theme, text size, log filtering), so the tests can check them.</summary>
public static class Display
{
    public const string Dark = "Dark", Light = "Light", Kats = "Kats", System = "System";

    /// <summary>The themes in Options > Appearance, in the drop-down's order: saved name and the name shown.</summary>
    public static readonly (string Name, string Label)[] Themes =
    {
        (Dark, "Dark"), (Light, "Light"), (Kats, "Kats (orange and gunmetal)"), (System, "Match Windows"),
    };

    /// <summary>The saved theme choice: one of <see cref="Themes"/> ("System" follows Windows). Anything else is dark, the default.</summary>
    public static string NormalizeTheme(string? name) =>
        Themes.Select(t => t.Name).FirstOrDefault(t => string.Equals(name, t, StringComparison.OrdinalIgnoreCase)) ?? Dark;

    /// <summary>
    /// The palette to use for a choice. "System" follows Windows' app mode (<paramref name="windowsAppsUseLight"/>, null
    /// when it could not be read, which counts as dark).
    /// </summary>
    public static string ResolveTheme(string? choice, bool? windowsAppsUseLight) => NormalizeTheme(choice) switch
    {
        System => windowsAppsUseLight == true ? Light : Dark,
        var c => c,
    };

    /// <summary>Text sizes offered under Options > Appearance, in percent.</summary>
    public static readonly int[] TextSizes = { 100, 115, 125, 150, 175 };

    /// <summary>A saved text size made safe: one of <see cref="TextSizes"/>, else the nearest (100 when unset).</summary>
    public static int NormalizeTextSize(int percent) =>
        percent <= 0 ? 100 : TextSizes.OrderBy(s => Math.Abs(s - percent)).First();

    /// <summary>Whether the log filter shows a line of this kind.</summary>
    public static bool Shows(LogFilter filter, LogKind kind) => filter switch
    {
        LogFilter.ErrorsOnly => kind == LogKind.Error,
        LogFilter.WarningsAndErrors => kind is LogKind.Warn or LogKind.Error,
        _ => true,
    };

    /// <summary>Colours a line from the upload engine by what it says.</summary>
    public static LogKind KindOf(string line)
    {
        if (line.StartsWith("SKIPPED") || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || line.Contains("FAILED", StringComparison.OrdinalIgnoreCase)) return LogKind.Error;
        if (line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)) return LogKind.Warn;
        if (line.StartsWith("Uploaded ") || line.StartsWith("Created ") || line.Contains("Success", StringComparison.OrdinalIgnoreCase)) return LogKind.Ok;
        if (line.StartsWith("===") || line.StartsWith("---")) return LogKind.Note;
        return LogKind.Plain;
    }

    /// <summary>"2 of 4 done" for the Get set up bar.</summary>
    public static string StepsDone(int done, int total) => $"{done} of {total} done";

    /// <summary>A label's text without its keyboard-shortcut marker ("Mods &amp;repo" is read as "Mods repo").</summary>
    public static string StripMnemonic(string text) => text.Replace("&&", "\0").Replace("&", "").Replace("\0", "&");
}
