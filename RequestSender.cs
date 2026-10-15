using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>
/// Creates a "game layout request" issue in Linear straight from the app, using a key that can ONLY create issues
/// in one team. The key is never stored in git: it is baked into the program at build time from the
/// LINEAR_CREATE_API_KEY / LINEAR_TEAM_UUID secrets, or a git-ignored request.key for local builds (see RequestKey.targets).
/// The optional PROJECTID / LABELIDS secrets (or request.key lines) pick the project and labels the request is filed under.
/// Without it the request form falls back to a pre-filled link / copy-to-clipboard.
/// </summary>
public static class RequestSender
{
    public const string Endpoint = "https://api.linear.app/graphql";
    public const int MaxTitle = 120;
    public const int MaxDescription = 8000;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Where requests are filed inside the team: a project and labels, all optional.</summary>
    public record Route(string? ProjectId, string[] LabelIds)
    {
        public static readonly Route None = new(null, Array.Empty<string>());
        public bool IsEmpty => ProjectId == null && LabelIds.Length == 0;

        /// <summary>Reads a project id and a list of label ids. Any text around the ids is ignored (separators,
        /// brackets, quotes, JSON, names), so only GUID-shaped values count.</summary>
        public static Route Parse(string? projectId, string? labelIds)
        {
            var project = Guids(projectId).FirstOrDefault();
            return new Route(project, Guids(labelIds).Distinct().ToArray());
        }

        static readonly System.Text.RegularExpressions.Regex GuidPattern =
            new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");

        static IEnumerable<string> Guids(string? text) =>
            GuidPattern.Matches(text ?? "").Select(m => m.Value.ToLowerInvariant());
    }

    static string[] BakedLines()
    {
        var text = BakedRequestKey.Read();
        return text == null ? Array.Empty<string>() : text.Split('\n').Select(l => l.Trim()).ToArray();
    }

    /// <summary>The key and team id, or null when this build has none.</summary>
    public static (string key, string teamId)? Credentials()
    {
        var lines = BakedLines();
        if (lines.Length < 2 || !lines[0].StartsWith("lin_api_") || !Guid.TryParse(lines[1], out _)) return null;
        return (lines[0], lines[1]);
    }

    /// <summary>The project and labels baked in from the PROJECTID / LABELIDS secrets (lines 3 and 4), if any.</summary>
    public static Route BakedRoute()
    {
        var lines = BakedLines();
        return Route.Parse(lines.Length > 2 ? lines[2] : null, lines.Length > 3 ? lines[3] : null);
    }

    public static bool Available => Credentials() != null;

    /// <summary>The GraphQL request body (separate so it can be checked without sending). The project and labels of
    /// <paramref name="route"/> are only sent when set.</summary>
    public static string BuildBody(string teamId, string title, string description, Route? route = null)
    {
        if (title.Length > MaxTitle) title = title.Substring(0, MaxTitle);
        if (description.Length > MaxDescription) description = description.Substring(0, MaxDescription) + "\n\n[trimmed]";
        var input = new Dictionary<string, object> { ["teamId"] = teamId, ["title"] = title, ["description"] = description };
        if (route?.ProjectId != null) input["projectId"] = route.ProjectId;
        if (route is { LabelIds.Length: > 0 }) input["labelIds"] = route.LabelIds;
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
        return SendAsync(creds.Value.key, creds.Value.teamId, BakedRoute(), title, description, endpointOverride ?? Endpoint, ct);
    }

    /// <summary>Files the issue into the route's project with its labels. If Linear refuses that (say the project or a
    /// label was removed, or the key may not use them), files it into the team alone so a request is never lost.</summary>
    public static async Task<(bool ok, string message)> SendAsync(string key, string teamId, Route route, string title, string description, string endpoint, CancellationToken ct = default)
    {
        try
        {
            var (ok, message, refused) = await PostAsync(key, endpoint, BuildBody(teamId, title, description, route), ct);
            if (ok || !refused || route.IsEmpty) return (ok, message);
            (ok, message, _) = await PostAsync(key, endpoint, BuildBody(teamId, title, description), ct);
            return (ok, message);
        }
        catch (TaskCanceledException) { return (false, "Linear did not answer in time. Check your internet connection."); }
        catch (HttpRequestException e) { return (false, "Could not reach Linear (" + e.Message + ")."); }
        catch (Exception e) { return (false, e.Message); }
    }

    /// <summary>One issueCreate call. <c>refused</c> is true when Linear answered but rejected the input.</summary>
    internal static async Task<(bool ok, string message, bool refused)> PostAsync(string key, string endpoint, string body, CancellationToken ct)
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
