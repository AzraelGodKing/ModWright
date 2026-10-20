using System.Diagnostics;
using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>
/// What to change on an item's Workshop page through the Steam client, beyond what SteamCMD's build file can carry.
/// A null list leaves that part of the page alone.
/// </summary>
public sealed class ExtrasRequest
{
    public string AppId { get; set; } = "";
    public string ItemId { get; set; } = "";
    /// <summary>The Workshop items players should be told to subscribe to as well ("Required items" on the page).</summary>
    public string[]? Required { get; set; }
    /// <summary>Required items this app set before, so ones the mod no longer lists can be taken off again.</summary>
    public string[] Managed { get; set; } = Array.Empty<string>();
    /// <summary>Items to count comments on (the Steam client's statistics); null counts nothing. Needs no ownership.</summary>
    public string[]? CountComments { get; set; }
}

/// <summary>What the Steam client did with an <see cref="ExtrasRequest"/>.</summary>
public sealed class ExtrasResult
{
    public bool Ok { get; set; }
    /// <summary>Why it failed, when it did.</summary>
    public string Error { get; set; } = "";
    /// <summary>One line per change made, for the log.</summary>
    public List<string> Done { get; set; } = new();
    /// <summary>The required items this app now looks after (the wanted ones that are on the page).</summary>
    public string[]? RequiredNow { get; set; }
    /// <summary>Comment counts by item id, for <see cref="ExtrasRequest.CountComments"/>.</summary>
    public Dictionary<string, long>? Comments { get; set; }

    public static ExtrasResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Page changes SteamCMD cannot make (required items) and counts only the Steam client gives (comments), sent through the Steam client in a child copy of this
/// program, like Workshop tags (<see cref="WorkshopTags"/>). The planning is here so it can be tested; the Steam
/// calls are in SteamworksExtras.cs.
/// </summary>
public static class WorkshopExtras
{
    /// <summary>The argument that makes the program apply one request file and exit (run in a child process).</summary>
    public const string ChildArg = "--steam-page-extras";

    const string ResultPrefix = "RESULT ";

    public static string DefaultRecord => Path.Combine(AppSettings.Folder, "required-items.json");

    static readonly object Gate = new();

    /// <summary>
    /// Which required items to add and which to take off: every wanted item that is missing is added; an item on the
    /// page is only taken off when this app put it there (it is in <paramref name="managed"/>) and the mod no longer
    /// lists it, so items added by hand on the website stay.
    /// </summary>
    public static (List<string> Add, List<string> Remove) RequiredPlan(IEnumerable<string> current, IEnumerable<string> wanted, IEnumerable<string> managed)
    {
        var have = current.ToHashSet();
        var want = wanted.Distinct().ToList();
        var add = want.Where(id => !have.Contains(id)).ToList();
        var remove = managed.Distinct().Where(id => have.Contains(id) && !want.Contains(id)).ToList();
        return (add, remove);
    }

    /// <summary>The required items this app set on each Workshop item, by id.</summary>
    public static Dictionary<string, string[]> LoadRecord(string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultRecord;
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }
    }

    /// <summary>Remembers the required items this app looks after for an item. Never throws.</summary>
    public static void Remember(string itemId, string[] required, string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultRecord;
                var all = LoadRecord(p);
                if (required.Length == 0) all.Remove(itemId); else all[itemId] = required;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(all));
            }
            catch { }
        }
    }

    /// <summary>
    /// Builds the request for a mod's required items: the Workshop links in its metadata (<paramref name="dir"/> is the
    /// mod folder or its staged package), or null when there is nothing to do (no links and none set before).
    /// </summary>
    public static ExtrasRequest? RequiredRequest(string appId, string itemId, IEnumerable<string> wanted, string? record = null)
    {
        var want = wanted.Where(id => id != itemId).Distinct().ToArray();
        var managed = LoadRecord(record).GetValueOrDefault(itemId) ?? Array.Empty<string>();
        if (want.Length == 0 && managed.Length == 0) return null;
        return new ExtrasRequest { AppId = appId, ItemId = itemId, Required = want, Managed = managed };
    }

    /// <summary>
    /// Applies a request through the Steam client, in a child copy of this program (SteamworksExtras.cs), for the same
    /// reasons as tags: the Steam client is told which game is asking once per process, and a crash in Valve's library
    /// must not take the window down. The caller keeps the record (<see cref="Remember"/>).
    /// </summary>
    public static async Task<ExtrasResult> ApplyOnSteamAsync(ExtrasRequest req, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return ExtrasResult.Fail("This needs the Steam client, which only works on Windows here.");
        var file = Path.Combine(Path.GetTempPath(), $"steamcmdhelper-extras-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(req), ct);
            var (output, error) = await SteamClientChild.RunAsync(new[] { ChildArg, file }, TimeSpan.FromMinutes(5), ct);
            return error != null ? ExtrasResult.Fail(error) : ParseChild(output!);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static string ChildLine(ExtrasResult r) => ResultPrefix + JsonSerializer.Serialize(r);

    public static ExtrasResult ParseChild(string output)
    {
        var line = output.Replace("\r", "").Split('\n').LastOrDefault(l => l.StartsWith(ResultPrefix, StringComparison.Ordinal));
        if (line == null) return ExtrasResult.Fail("The Steam client step stopped without saying why" + (output.Trim().Length > 0 ? ": " + output.Trim() : "."));
        try { return JsonSerializer.Deserialize<ExtrasResult>(line[ResultPrefix.Length..]) ?? ExtrasResult.Fail("The Steam client step gave no answer."); }
        catch (JsonException e) { return ExtrasResult.Fail("The Steam client step's answer could not be read: " + e.Message); }
    }
}

/// <summary>Starts a child copy of this program that talks to the Steam client, and collects what it prints.</summary>
public static class SteamClientChild
{
    /// <summary>The child's standard output, or an error saying why there is none.</summary>
    public static async Task<(string? Output, string? Error)> RunAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return (null, "Could not find this program's own exe to run.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            var read = p.StandardOutput.ReadToEndAsync(limit.Token);
            _ = p.StandardError.ReadToEndAsync(limit.Token);
            try { await p.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                if (ct.IsCancellationRequested) throw;
                return (null, $"Steam did not answer within {(timeout.TotalMinutes >= 1 ? $"{timeout.TotalMinutes:0} minutes" : $"{timeout.TotalSeconds:0} seconds")}.");
            }
            return (await read, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { return (null, e.Message); }
    }
}
