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

    /// <summary>The GraphQL request body (separate so it can be checked without sending).</summary>
    public static string BuildBody(string teamId, string title, string description)
    {
        if (title.Length > MaxTitle) title = title.Substring(0, MaxTitle);
        if (description.Length > MaxDescription) description = description.Substring(0, MaxDescription) + "\n\n[trimmed]";
        return JsonSerializer.Serialize(new
        {
            query = "mutation($input: IssueCreateInput!) { issueCreate(input: $input) { success issue { identifier } } }",
            variables = new { input = new { teamId, title, description } }
        });
    }

    /// <summary>Creates the issue. Returns (true, "REQ-12") on success or (false, reason).</summary>
    public static async Task<(bool ok, string message)> SendAsync(string title, string description, string? endpointOverride = null, CancellationToken ct = default)
    {
        var creds = Credentials();
        if (creds == null) return (false, "This build has no request key.");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpointOverride ?? Endpoint)
            {
                Content = new StringContent(BuildBody(creds.Value.teamId, title, description), Encoding.UTF8, "application/json")
            };
            // Linear personal API keys go in the header as-is (no "Bearer").
            req.Headers.TryAddWithoutValidation("Authorization", creds.Value.key);
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return (false, $"Linear answered {(int)resp.StatusCode}.");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("errors", out var errs) && errs.GetArrayLength() > 0)
                return (false, "Linear refused the request: " + (errs[0].TryGetProperty("message", out var m) ? m.GetString() : "unknown error"));
            var created = doc.RootElement.GetProperty("data").GetProperty("issueCreate");
            if (!created.GetProperty("success").GetBoolean()) return (false, "Linear did not create the issue.");
            return (true, created.GetProperty("issue").GetProperty("identifier").GetString() ?? "created");
        }
        catch (TaskCanceledException) { return (false, "Linear did not answer in time. Check your internet connection."); }
        catch (HttpRequestException e) { return (false, "Could not reach Linear (" + e.Message + ")."); }
        catch (Exception e) { return (false, e.Message); }
    }
}
