using System.Globalization;
using System.Text;

namespace SteamCmdHelper;

/// <summary>
/// The mod list and the upload history as text for elsewhere: a Markdown table for a README, Steam BBCode for a
/// collection or profile page, and CSV for a spreadsheet.
/// </summary>
public static class Reports
{
    public const string Markdown = "markdown", BBCode = "bbcode", Csv = "csv";

    static string Url(string id) => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;

    /// <summary>The mods in <paramref name="format"/> (<see cref="Markdown"/>, <see cref="BBCode"/> or <see cref="Csv"/>). Mods not on Steam are left out of Markdown and BBCode.</summary>
    public static string ModList(IEnumerable<ModInfo> mods, string format)
    {
        var list = mods.ToList();
        var sb = new StringBuilder();
        switch (format)
        {
            case Markdown:
                sb.Append("| Mod | Version | Steam Workshop |\n|---|---|---|\n");
                foreach (var m in list.Where(m => m.OnSteam))
                    sb.Append($"| {Md(m.Name)} | {Md(m.Version)} | [{m.WorkshopId}]({Url(m.WorkshopId!)}) |\n");
                break;
            case BBCode:
                sb.Append("[list]\n");
                foreach (var m in list.Where(m => m.OnSteam))
                    sb.Append($"[*][url={Url(m.WorkshopId!)}]{m.Name.Replace("[", "(").Replace("]", ")")}[/url]" + (m.Version.Length > 0 ? $" (v{m.Version})" : "") + "\n");
                sb.Append("[/list]\n");
                break;
            default:
                sb.Append("folder,name,version,workshop_id,workshop_url,status\n");
                foreach (var m in list)
                    sb.Append(string.Join(",", new[] { m.RelPath, m.Name, m.Version, m.WorkshopId ?? "", m.OnSteam ? Url(m.WorkshopId!) : "", m.StatusText }.Select(CsvCell))).Append('\n');
                break;
        }
        return sb.ToString();
    }

    /// <summary>The upload history as CSV, newest first: time (UTC, ISO 8601), mod, version, item, created, size, seconds, change note.</summary>
    public static string HistoryCsv(IEnumerable<UploadEntry> entries)
    {
        var sb = new StringBuilder("time_utc,mod,version,workshop_id,created,app_id,bytes,seconds,change_note\n");
        foreach (var e in entries.OrderByDescending(e => e.Time))
            sb.Append(string.Join(",", new[]
            {
                e.Time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), e.Mod, e.Version, e.WorkshopId,
                e.Created ? "yes" : "no", e.AppId, e.Bytes.ToString(CultureInfo.InvariantCulture),
                e.Seconds.ToString("0.#", CultureInfo.InvariantCulture), e.ChangeNote,
            }.Select(CsvCell))).Append('\n');
        return sb.ToString();
    }

    /// <summary>One CSV cell: quoted when it holds a comma, quote or line break. A leading = or @ (or + or - not followed by a space) is escaped so spreadsheets don't run it as a formula.</summary>
    public static string CsvCell(string value)
    {
        var v = value.Replace("\r\n", "\n");
        // "- Fixed it" (a list line) is safe; "-cmd|..." or "=HYPERLINK(...)" is not.
        if (v.Length > 0 && ("=@".Contains(v[0]) || ("+-".Contains(v[0]) && v.Length > 1 && v[1] != ' ' && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))) v = "'" + v;
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    static string Md(string s) => s.Replace("|", "\\|");
}
