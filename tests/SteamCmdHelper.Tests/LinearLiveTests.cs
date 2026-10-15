using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>
/// Checks the real Linear request key end to end. Does nothing unless LINEAR_LIVE_TEST=1, so ordinary test runs never
/// touch Linear; the "Linear key check" workflow sets it together with the repository secrets.
/// Each live run creates one issue in the SteamCMD Helper project, which can be deleted afterwards.
/// </summary>
public class LinearLiveTests
{
    [Fact]
    public async Task Repository_key_files_a_request_into_the_project_with_its_labels()
    {
        if (Environment.GetEnvironmentVariable("LINEAR_LIVE_TEST") != "1") return;
        var key = Environment.GetEnvironmentVariable("LINEAR_CREATE_API_KEY");
        var team = Environment.GetEnvironmentVariable("LINEAR_TEAM_UUID");
        Assert.False(string.IsNullOrWhiteSpace(key), "LINEAR_CREATE_API_KEY is not set.");
        Assert.False(string.IsNullOrWhiteSpace(team), "LINEAR_TEAM_UUID is not set.");
        Assert.StartsWith("lin_api_", key!.Trim());
        Assert.True(Guid.TryParse(team!.Trim(), out _), "LINEAR_TEAM_UUID is not a team ID (GUID).");

        var run = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local";
        var body = RequestSender.BuildBody(team.Trim(), "Linear key check (safe to delete)",
            $"Created by the Linear key check workflow (run {run}) to prove the repository secrets can file requests. Safe to delete.");
        // Straight to PostAsync, not SendAsync: SendAsync would fall back to the team alone and hide a broken project or label.
        var (ok, message, _) = await RequestSender.PostAsync(key.Trim(), RequestSender.Endpoint, body, CancellationToken.None);
        Assert.True(ok, "Linear did not file the request into the project with its labels: " + message);
        Console.WriteLine("Linear key check created " + message);
    }
}
