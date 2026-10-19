using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

public enum BatchStatus { Uploaded, Created, Checked, Skipped, Failed, NotRun }

/// <summary>What happened to one mod in a batch.</summary>
/// <param name="SentVersion">The version that went out when it differs from the mod's (an upload from an older git ref).</param>
public sealed record BatchItem(ModInfo Mod, BatchStatus Status, string Message, string? WorkshopId, string? SentVersion = null)
{
    /// <summary>The version this item was about.</summary>
    public string Version => SentVersion ?? Mod.Version;

    public bool Ok => Status is BatchStatus.Uploaded or BatchStatus.Created or BatchStatus.Checked or BatchStatus.Skipped;
}

/// <summary>
/// Uploads (or dry-runs) several mods one after another with one <see cref="Publisher"/>. The Steam login is
/// checked once before the batch by the caller; every upload then reuses SteamCMD's saved login. If Steam turns
/// the login down part-way, the remaining mods are not tried, since they would all fail the same way.
/// Used by the window's Upload button and by the command line.
/// </summary>
public sealed class BatchUpload
{
    readonly Publisher _pub;
    readonly string? _recordPath;

    /// <summary>A mod is starting: (mod, index from 0, count).</summary>
    public event Action<ModInfo, int, int>? Starting;
    /// <summary>A mod finished: (result, index from 0, count).</summary>
    public event Action<BatchItem, int, int>? Finished;
    /// <summary>A mod is tried again after Steam was busy or unreachable: (mod, why it failed, the wait).</summary>
    public event Action<ModInfo, string, TimeSpan>? Retrying;

    /// <summary>
    /// How long to wait before trying an update once more when Steam was busy, timed out or could not be reached; null
    /// never retries. New items are not retried, since Steam may have created one without saying so.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    static readonly Regex Transient = new(@"Timeout|Timed out|No ?Connection|Service ?Unavailable|\bBusy\b|ConnectFailed|Connection (?:reset|lost|closed)|Try ?Again", RegexOptions.IgnoreCase);

    /// <summary>True when a failure looks like Steam being busy or unreachable, which a second try often gets past.</summary>
    public static bool IsTransient(string reason) => Transient.IsMatch(reason) && !IsLoginFailure(reason);

    /// <param name="recordPath">Where uploaded versions are remembered; null for the per-user file (tests pass their own).</param>
    public BatchUpload(Publisher pub, string? recordPath = null)
    {
        _pub = pub;
        _recordPath = recordPath;
    }

    static readonly Regex LoginFailed = new(@"Login Failure|FAILED login|Logging in user\b.*FAILED|Invalid Password|not logged on|Rate ?Limit|Two.?factor|Steam ?Guard|AccountLogonDenied|Cached credentials", RegexOptions.IgnoreCase);

    /// <summary>True when a failed upload's reason says Steam refused the login rather than this one item.</summary>
    public static bool IsLoginFailure(string reason) => LoginFailed.IsMatch(reason);

    /// <summary>
    /// Upload a batch of several mods with one SteamCMD run (<see cref="Publisher.RunSessionAsync"/>): one login, then each
    /// mod in turn. Off: each mod gets its own SteamCMD run. Dry runs and single mods always run one at a time.
    /// </summary>
    public bool OneSession { get; init; }

    /// <summary>
    /// Runs every mod in order. Cancelling stops after the current mod; it and the rest are reported as not run.
    /// Never throws for a single mod's failure.
    /// </summary>
    public async Task<List<BatchItem>> RunAsync(IReadOnlyList<ModInfo> mods, bool upload, CancellationToken ct)
    {
        if (OneSession && upload && mods.Count > 1) return await RunSessionAsync(mods, ct);
        // The next mods are packaged while the current one is checked and uploaded.
        if (mods.Count > 1) _pub.PackageAhead(mods, ct);
        try { return await RunEachAsync(mods, upload, ct); }
        finally { await _pub.DropAheadAsync(); }
    }

    async Task<List<BatchItem>> RunEachAsync(IReadOnlyList<ModInfo> mods, bool upload, CancellationToken ct)
    {
        var results = new List<BatchItem>();
        string? stopReason = null;
        for (var i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            if (stopReason == null && ct.IsCancellationRequested) stopReason = "cancelled";
            if (stopReason != null)
            {
                results.Add(new BatchItem(m, BatchStatus.NotRun, "Not run: " + stopReason + ".", m.WorkshopId));
                continue;
            }

            Starting?.Invoke(m, i, mods.Count);
            BatchItem item;
            try
            {
                var res = await _pub.RunAsync(m, upload, ct);
                res = await RetryAsync(m, res, upload, ct);
                item = ToItem(m, res, upload);
                if (!res.Ok && upload && IsLoginFailure(res.Message)) stopReason = "Steam refused the login";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                item = new BatchItem(m, BatchStatus.NotRun, "Cancelled while running.", m.WorkshopId);
                stopReason = "cancelled";
            }
            catch (Exception e)
            {
                item = new BatchItem(m, BatchStatus.Failed, e.Message, m.WorkshopId);
            }
            results.Add(item);
            Finished?.Invoke(item, i, mods.Count);
        }
        return results;
    }

    /// <summary>
    /// The one-session batch: every mod is packaged and checked, then SteamCMD logs in once and uploads them all. A mod
    /// that failed because Steam was busy is then tried once more on its own (<see cref="RetryAfter"/>). When Steam
    /// refused the login, the first mod reports it and the rest are reported as not run, as in a normal batch.
    /// </summary>
    async Task<List<BatchItem>> RunSessionAsync(IReadOnlyList<ModInfo> mods, CancellationToken ct)
    {
        PublishResult?[] got;
        var cancelled = false;
        try { got = await _pub.RunSessionAsync(mods, i => Starting?.Invoke(mods[i], i, mods.Count), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { got = new PublishResult?[mods.Count]; cancelled = true; }

        var results = new List<BatchItem>();
        string? stopReason = cancelled ? "cancelled" : null;
        for (var i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            BatchItem item;
            if (got[i] is not { } res || stopReason != null)
                item = new BatchItem(m, BatchStatus.NotRun, "Not run: " + (stopReason ?? "cancelled") + ".", m.WorkshopId);
            else
            {
                try
                {
                    if (!ct.IsCancellationRequested) res = await RetryAsync(m, res, true, ct);
                    item = ToItem(m, res, true);
                    if (!res.Ok && IsLoginFailure(res.Message)) stopReason = "Steam refused the login";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    item = new BatchItem(m, BatchStatus.NotRun, "Cancelled while running.", m.WorkshopId);
                    stopReason = "cancelled";
                }
                catch (Exception e) { item = new BatchItem(m, BatchStatus.Failed, e.Message, m.WorkshopId); }
            }
            results.Add(item);
            Finished?.Invoke(item, i, mods.Count);
        }
        return results;
    }

    /// <summary>Tries an update once more after <see cref="RetryAfter"/> when Steam was busy or unreachable.</summary>
    async Task<PublishResult> RetryAsync(ModInfo m, PublishResult res, bool upload, CancellationToken ct)
    {
        var existing = _pub.TestItems ? m.TestWorkshopId : m.WorkshopId;
        if (res.Ok || !upload || existing == null || RetryAfter is not { } wait || !IsTransient(res.Message)) return res;
        Retrying?.Invoke(m, res.Message, wait);
        await Task.Delay(wait, ct);
        return await _pub.RunAsync(m, upload, ct);
    }

    /// <summary>What a publisher result means for the batch, remembering the version Steam now has.</summary>
    BatchItem ToItem(ModInfo m, PublishResult res, bool upload)
    {
        var existing = _pub.TestItems ? m.TestWorkshopId : m.WorkshopId;
        var id = existing ?? res.NewId;
        if (!res.Ok) return new BatchItem(m, BatchStatus.Failed, res.Message, id);
        if (!upload) return new BatchItem(m, BatchStatus.Checked, res.Message, id, res.Version);
        if (res.Skipped) return new BatchItem(m, BatchStatus.Skipped, res.Message, id, res.Version);
        if (id != null && !_pub.TestItems) UploadedVersions.Remember(id, res.Version ?? m.Version, _recordPath);
        return new BatchItem(m, existing != null ? BatchStatus.Uploaded : BatchStatus.Created, res.Message, id, res.Version);
    }

    /// <summary>One line with the counts, e.g. "2 uploaded, 1 failed, 1 not run".</summary>
    public static string Counts(IReadOnlyCollection<BatchItem> results)
    {
        var parts = new List<string>();
        void Add(int n, string what) { if (n > 0) parts.Add($"{n} {what}"); }
        Add(results.Count(r => r.Status == BatchStatus.Uploaded), "uploaded");
        Add(results.Count(r => r.Status == BatchStatus.Created), "created");
        Add(results.Count(r => r.Status == BatchStatus.Checked), "checked");
        Add(results.Count(r => r.Status == BatchStatus.Skipped), "skipped");
        Add(results.Count(r => r.Status == BatchStatus.Failed), "failed");
        Add(results.Count(r => r.Status == BatchStatus.NotRun), "not run");
        return parts.Count == 0 ? "nothing to do" : string.Join(", ", parts);
    }

    /// <summary>The end-of-batch summary: the counts, then one line per mod. Plain ASCII so it reads the same in any console.</summary>
    public static string Summary(IReadOnlyCollection<BatchItem> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Finished: " + Counts(results) + ".");
        foreach (var r in results)
        {
            var label = r.Status switch
            {
                BatchStatus.Uploaded => "UPLOADED",
                BatchStatus.Created => "CREATED ",
                BatchStatus.Checked => "CHECKED ",
                BatchStatus.Skipped => "SKIPPED ",
                BatchStatus.Failed => "FAILED  ",
                _ => "NOT RUN ",
            };
            var version = r.Version.Length > 0 ? " v" + r.Version : "";
            var id = r.WorkshopId != null ? $" (Workshop id {r.WorkshopId})" : "";
            var detail = r.Ok && r.Status != BatchStatus.Skipped ? "" : ": " + r.Message;
            sb.AppendLine($"  {label} {r.Mod.Name}{version}{id}{detail}");
            if (r.Status == BatchStatus.Failed && SteamCmdOutput.Advice(r.Message) is { } advice) sb.AppendLine("           What to do: " + advice);
        }
        return sb.ToString().TrimEnd();
    }
}
