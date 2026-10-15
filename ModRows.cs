using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>The badge in the mod list's Status column.</summary>
public enum RowState { Fix, Failed, Changed, New, Ready, UpToDate }

/// <summary>What the mod list shows for each mod (its status badge) and how it filters and sorts. No UI code.</summary>
public static class ModRows
{
    /// <summary>
    /// Fix beats everything (it can't be uploaded); then a failure in the last batch; then a mod not on Steam yet.
    /// A mod on Steam is Up to date when the version on Steam is the one to upload, Changed when it differs, and
    /// just Ready while the version on Steam is not known (still loading, offline, or not in the change notes).
    /// </summary>
    public static RowState State(ModInfo m, string? uploadedVersion, bool failedLastRun)
    {
        if (!m.Ready) return RowState.Fix;
        if (failedLastRun) return RowState.Failed;
        if (!m.OnSteam) return RowState.New;
        var up = Clean(uploadedVersion);
        if (up.Length == 0 || Clean(m.Version).Length == 0) return RowState.Ready;
        return string.Equals(up, Clean(m.Version), StringComparison.OrdinalIgnoreCase) ? RowState.UpToDate : RowState.Changed;
    }

    /// <summary>The list shows placeholders ("...", "?", "-") while a version is unknown; those are not versions.</summary>
    static string Clean(string? v)
    {
        v = (v ?? "").Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        return Regex.IsMatch(v, @"\d") ? v : "";
    }

    public static string Label(RowState s) => s switch
    {
        RowState.Fix => "Fix",
        RowState.Failed => "Failed",
        RowState.Changed => "Changed",
        RowState.New => "New",
        RowState.UpToDate => "Up to date",
        _ => "Ready",
    };

    /// <summary>The filter box matches the mod's name, folder or Workshop ID; "Hide up to date" drops those rows.</summary>
    public static bool Show(ModInfo m, RowState s, string filter, bool hideUpToDate)
    {
        if (hideUpToDate && s == RowState.UpToDate) return false;
        filter = filter.Trim();
        if (filter.Length == 0) return true;
        return m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || m.Folder.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || (m.WorkshopId ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    static readonly Regex Chunks = new(@"\d+|\D+");

    /// <summary>Compares text the way people read it: "Mod 2" before "Mod 10", "1.9" before "1.10". Case is ignored.</summary>
    public static int CompareNatural(string? a, string? b)
    {
        var x = Chunks.Matches(a ?? "");
        var y = Chunks.Matches(b ?? "");
        for (var i = 0; i < Math.Min(x.Count, y.Count); i++)
        {
            var p = x[i].Value; var q = y[i].Value;
            int c;
            if (char.IsDigit(p[0]) && char.IsDigit(q[0]))
            {
                var pt = p.TrimStart('0'); var qt = q.TrimStart('0');
                c = pt.Length != qt.Length ? pt.Length.CompareTo(qt.Length) : string.CompareOrdinal(pt, qt);
            }
            else c = string.Compare(p, q, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return x.Count.CompareTo(y.Count);
    }

    /// <summary>Sorting by Status puts what needs attention first.</summary>
    public static int CompareState(RowState a, RowState b) => ((int)a).CompareTo((int)b);
}
