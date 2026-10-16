using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

public enum BatchStatus { Uploaded, Created, Checked, Skipped, Failed, NotRun }

/// <summary>What happened to one mod in a batch.</summary>
public sealed record BatchItem(ModInfo Mod, BatchStatus Status, string Message, string? WorkshopId)
{
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
    /// Runs every mod in order. Cancelling stops after the current mod; it and the rest are reported as not run.
    /// Never throws for a single mod's failure.
    /// </summary>
    public async Task<List<BatchItem>> RunAsync(IReadOnlyList<ModInfo> mods, bool upload, CancellationToken ct)
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
                var id = m.WorkshopId ?? res.NewId;
                if (!res.Ok)
                {
                    item = new BatchItem(m, BatchStatus.Failed, res.Message, id);
                    if (upload && IsLoginFailure(res.Message)) stopReason = "Steam refused the login";
                }
                else if (!upload) item = new BatchItem(m, BatchStatus.Checked, res.Message, id);
                else if (res.Skipped) item = new BatchItem(m, BatchStatus.Skipped, res.Message, id);
                else
                {
                    if (id != null) UploadedVersions.Remember(id, m.Version, _recordPath);
                    item = new BatchItem(m, m.OnSteam ? BatchStatus.Uploaded : BatchStatus.Created, res.Message, id);
                }
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
            var version = r.Mod.Version.Length > 0 ? " v" + r.Mod.Version : "";
            var id = r.WorkshopId != null ? $" (Workshop id {r.WorkshopId})" : "";
            var detail = r.Ok && r.Status != BatchStatus.Skipped ? "" : ": " + r.Message;
            sb.AppendLine($"  {label} {r.Mod.Name}{version}{id}{detail}");
            if (r.Status == BatchStatus.Failed && SteamCmdOutput.Advice(r.Message) is { } advice) sb.AppendLine("           What to do: " + advice);
        }
        return sb.ToString().TrimEnd();
    }
}
