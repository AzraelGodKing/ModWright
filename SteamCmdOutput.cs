using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>What one "+workshop_build_item" run actually achieved.</summary>
/// <param name="PublishedFileId">The item's Workshop id when Steam reported one, even if the run failed afterwards.</param>
/// <param name="Unconfirmed">The run looked clean (exit 0, no failure line) but never said "Success", so Steam itself may
/// still show that the update arrived.</param>
public sealed record UploadOutcome(bool Ok, string Reason, string? PublishedFileId, bool Unconfirmed = false);

/// <summary>
/// Decides whether a SteamCMD Workshop upload really succeeded. SteamCMD can exit 0 after a failure and prints
/// harmless lines that contain words like "error", so success needs all of: exit code 0, no SteamCMD failure
/// line, an explicit "Success." line, and for a new item the Workshop id Steam assigned.
/// </summary>
public static class SteamCmdOutput
{
    const RegexOptions Ic = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]");
    /// <summary>
    /// "Success." on its own line, or run on after a step: SteamCMD on Windows prints
    /// "Committing update...Success.Unloading Steam API...OK" as one line.
    /// </summary>
    static readonly Regex SuccessLine = new(@"(?:^|\.\.\.)\s*Success\b", Ic);

    /// <summary>
    /// SteamCMD's own failure forms: "ERROR! Failed to update workshop item (Access Denied).",
    /// "Logging in user 'x' to Steam Public...FAILED (Invalid Password)", "FAILED login with result code ...".
    /// The upper-case markers are matched case-sensitively, so file names such as "Textures/Error.png" or benign
    /// notices like "Failed to init SDL priority manager" do not match.
    /// </summary>
    static readonly Regex FailureLine = new(
        @"(?-i:ERROR!|(?:^|\.\.\.)\s*FAILED\b)|\bFailed to (?:update|upload|commit|create|publish|prepare)\b|\bLogin Failure\b", Ic);

    static readonly Regex IdInOutput = new(@"\bPublish(?:ed)?\s*File\s*ID\b\D{0,3}(\d{6,})", Ic);
    static readonly Regex IdInVdf = new("\"publishedfileid\"\\s+\"(\\d+)\"", Ic);

    /// <param name="lines">Everything SteamCMD printed, stdout and stderr.</param>
    /// <param name="exitCode">SteamCMD's exit code.</param>
    /// <param name="expectedId">The item's Workshop id for an update; null when creating a new item.</param>
    /// <param name="vdfText">The build file after the run. SteamCMD writes a new item's id back into it.</param>
    public static UploadOutcome Evaluate(IEnumerable<string> lines, int exitCode, string? expectedId, string? vdfText)
    {
        var clean = lines.Select(Clean).Where(l => l.Length > 0).ToList();

        var vdfId = vdfText == null ? null : IdInVdf.Match(vdfText) is { Success: true } v && v.Groups[1].Value != "0" ? v.Groups[1].Value : null;
        var outId = clean.Select(l => IdInOutput.Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        var id = expectedId ?? vdfId ?? outId;

        UploadOutcome Fail(string reason) => new(false, reason, id);

        var failure = clean.FirstOrDefault(l => FailureLine.IsMatch(l));
        if (failure != null) return Fail("SteamCMD reported a failure: " + failure);
        if (exitCode != 0) return Fail($"SteamCMD exited with code {exitCode}.");
        if (!clean.Any(l => SuccessLine.IsMatch(l)))
            return new UploadOutcome(false, "SteamCMD finished without confirming the upload (no \"Success\" line).", id, Unconfirmed: true);

        if (expectedId != null)
        {
            if (outId != null && outId != expectedId) return Fail($"Steam reported Workshop id {outId}, but this mod's id is {expectedId}.");
        }
        else
        {
            if (id == null) return Fail("Steam accepted the upload but did not return the new Workshop id.");
            if (vdfId != null && outId != null && vdfId != outId) return Fail($"Steam returned two different Workshop ids ({vdfId} and {outId}).");
        }

        return new UploadOutcome(true, expectedId != null ? "Uploaded" : "Created", id);
    }

    /// <summary>
    /// True for a line that ends one workshop_build_item: its "Success" line or a SteamCMD failure line. Used to split one
    /// SteamCMD run that uploads several mods into one part per mod.
    /// </summary>
    public static bool EndsItem(string line)
    {
        var l = Clean(line);
        return l.Length > 0 && (SuccessLine.IsMatch(l) || FailureLine.IsMatch(l));
    }

    static readonly (Regex match, string advice)[] Advices =
    {
        (new(@"Rate ?Limit", Ic), "Steam is limiting logins from this PC after too many tries. Wait about half an hour, then try again."),
        (new(@"Two.?factor|Steam ?Guard|AccountLogonDenied|need.*code", Ic), "Steam wants a Steam Guard code. Use Options > Log in... and type the code in the SteamCMD window."),
        (new(@"Invalid ?Password", Ic), "Steam refused the saved login (the password may have changed). Use Options > Log in... to log in again."),
        (new(@"Login Failure|FAILED login|not logged on|Cached credentials|No cached|Expired", Ic), "The saved Steam login has expired or is gone. Use Options > Log in... to log in again."),
        (new(@"legal agreement|Workshop Legal", Ic), "This account has not accepted the Steam Workshop legal agreement. Open steamcommunity.com/sharedfiles/workshoplegalagreement while signed in and accept it."),
        (new(@"Access Denied", Ic), "Steam says this account may not change the item. It must own the item (or be a contributor to it) and own the game."),
        (new(@"File ?Not ?Found", Ic), "Steam has no item with this Workshop ID. Check the mod's id file; it may belong to another game or have been deleted."),
        (new(@"Limit ?Exceeded", Ic), "Steam says a limit was exceeded, usually the preview image size (1 MB) or the account's Workshop storage."),
        (new(@"Timeout|No ?Connection|Service ?Unavailable|Busy|ConnectFailed", Ic), "Steam could not be reached or was busy. Check the connection and try again in a few minutes."),
    };

    /// <summary>What to do about a failed upload, in plain words, or null when the reason is not one Steam commonly gives.</summary>
    public static string? Advice(string reason) => Advices.FirstOrDefault(a => a.match.IsMatch(reason)).advice;

    static string Clean(string line) => Ansi.Replace(line, "").Trim();
}
