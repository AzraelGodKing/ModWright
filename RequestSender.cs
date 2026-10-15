using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>
/// Creates a "game layout request" issue in Linear straight from the app, using a key that can ONLY create issues
/// in one team. The key is never stored in git: it is baked into the program at build time from the
/// LINEAR_CREATE_API_KEY / LINEAR_TEAM_UUID secrets, or a git-ignored request.key for local builds (see RequestKey.targets).
/// Without it the request form falls back to a pre-filled link / copy-to-clipboard.
/// </summary>
public static class RequestSender
{
    public const string Endpoint = "https://api.linear.app/graphql";
    public const int MaxTitle = 120;
    public const int MaxDescription = 8000;

    /// <summary>The "SteamCMD Helper" project in Linear; requests are filed there.</summary>
    public const string ProjectId = "d2489647-591c-41b8-a2ef-ed5925ef1a12";

    /// <summary>The "Layout Request" and "SteamCMD Helper" labels, put on every request.</summary>
    public static readonly string[] LabelIds = { "e1d2ed12-1294-48eb-a3ec-27d49fa9e53c", "cf33e973-3742-438e-b47e-0a32c56f8736" };

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>The key and team id, or null when this build has none.</summary>
    public static (string key, string teamId)? Credentials()
    {
        var text = BakedRequestKey.Read();
        if (text == null) return null;
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (lines.Length < 2 || !lines[0].StartsWith("lin_api_") || !Guid.TryParse(lines[1], out _)) return null;
        return (lines[0], lines[1]);
    }

    public static bool Available => Credentials() != null;

    /// <summary>The GraphQL request body (separate so it can be checked without sending). With <paramref name="routed"/>
    /// the issue goes into the SteamCMD Helper project with its labels; without it, just into the team.</summary>
    public static string BuildBody(string teamId, string title, string description, bool routed = true)
    {
        if (title.Length > MaxTitle) title = title.Substring(0, MaxTitle);
        if (description.Length > MaxDescription) description = description.Substring(0, MaxDescription) + "\n\n[trimmed]";
        object input = routed
            ? new { teamId, title, description, projectId = ProjectId, labelIds = LabelIds }
            : new { teamId, title, description };
        return JsonSerializer.Serialize(new
        {
            query = "mutation($input: IssueCreateInput!) { issueCreate(input: $input) { success issue { identifier } } }",
            variables = new { input }
        });
    }

    /// <summary>Creates the issue. Returns (true, "REQ-12") on success or (false, reason).</summary>
    public static Task<(bool ok, string message)> SendAsync(string title, string description, string? endpointOverride = null, CancellationToken ct = default)
    {
        var creds = Credentials();
        if (creds == null) return Task.FromResult((false, "This build has no request key."));
        return SendAsync(creds.Value.key, creds.Value.teamId, title, description, endpointOverride ?? Endpoint, ct);
    }

    /// <summary>Files the issue into the project with its labels. If Linear refuses that (say the project or a label
    /// was removed, or the key may not use them), files it into the team alone so a request is never lost.</summary>
    public static async Task<(bool ok, string message)> SendAsync(string key, string teamId, string title, string description, string endpoint, CancellationToken ct = default)
    {
        try
        {
            var (ok, message, refused) = await PostAsync(key, endpoint, BuildBody(teamId, title, description, routed: true), ct);
            if (ok || !refused) return (ok, message);
            (ok, message, _) = await PostAsync(key, endpoint, BuildBody(teamId, title, description, routed: false), ct);
            return (ok, message);
        }
        catch (TaskCanceledException) { return (false, "Linear did not answer in time. Check your internet connection."); }
        catch (HttpRequestException e) { return (false, "Could not reach Linear (" + e.Message + ")."); }
        catch (Exception e) { return (false, e.Message); }
    }

    /// <summary>One issueCreate call. <c>refused</c> is true when Linear answered but rejected the input.</summary>
    static async Task<(bool ok, string message, bool refused)> PostAsync(string key, string endpoint, string body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        // Linear personal API keys go in the header as-is (no "Bearer").
        req.Headers.TryAddWithoutValidation("Authorization", key);
        using var resp = await Http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { }
        using (doc)
        {
            var root = doc?.RootElement;
            if (root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
                return (false, "Linear refused the request: " + (errs[0].TryGetProperty("message", out var m) ? m.GetString() : "unknown error"), true);
            if (!resp.IsSuccessStatusCode) return (false, $"Linear answered {(int)resp.StatusCode}.", (int)resp.StatusCode == 400);
            if (root is not { ValueKind: JsonValueKind.Object } ok) return (false, "Linear sent an answer the app could not read.", false);
            var created = ok.GetProperty("data").GetProperty("issueCreate");
            if (!created.GetProperty("success").GetBoolean()) return (false, "Linear did not create the issue.", true);
            return (true, created.GetProperty("issue").GetProperty("identifier").GetString() ?? "created", false);
        }
    }
}
