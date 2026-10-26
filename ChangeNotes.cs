using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Shapes a change note before it goes to Steam: an optional template around it (per mods folder). It does not touch
/// the changelog file, the upload history or the Discord announcement.
/// </summary>
public static class ChangeNotes
{
    /// <summary>The placeholders a template may use.</summary>
    public static readonly string[] Placeholders = { "{note}", "{name}", "{version}", "{date}", "{game}" };

    /// <summary>
    /// Fills a template: {note} is the change note, {name}, {version} and {game} the mod's, {date} today's date
    /// (yyyy-MM-dd). A template without {note} gets the note after it. An empty template leaves the note as it is.
    /// </summary>
    public static string Apply(string? template, string note, string name, string version, string game, DateTime? today = null)
    {
        if (string.IsNullOrWhiteSpace(template)) return note;
        var t = template.Replace("\r\n", "\n").Trim('\n');
        if (!t.Contains("{note}", StringComparison.OrdinalIgnoreCase)) t += "\n\n{note}";
        var date = (today ?? DateTime.Now).ToString("yyyy-MM-dd");
        // One pass, so a note that itself contains "{name}" is left as typed.
        return Regex.Replace(t, @"\{(note|name|version|date|game)\}", m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "note" => note,
            "name" => name,
            "version" => version,
            "date" => date,
            _ => game,
        }, RegexOptions.IgnoreCase).Trim();
    }
}
