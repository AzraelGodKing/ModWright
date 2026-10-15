using System.Net;
using System.Text;
using System.Text.Json;
using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public class RequestSenderTests
{
    const string Team = "11111111-2222-3333-4444-555555555555";

    static JsonElement Input(string body) => JsonDocument.Parse(body).RootElement.GetProperty("variables").GetProperty("input");

    [Fact]
    public void BuildBody_routes_into_the_project_with_its_labels()
    {
        var input = Input(RequestSender.BuildBody(Team, "Game layout request: X", "details"));
        Assert.Equal(Team, input.GetProperty("teamId").GetString());
        Assert.Equal(RequestSender.ProjectId, input.GetProperty("projectId").GetString());
        Assert.Equal(RequestSender.LabelIds, input.GetProperty("labelIds").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void BuildBody_unrouted_sends_only_team_title_and_description()
    {
        var input = Input(RequestSender.BuildBody(Team, "t", "d", routed: false));
        Assert.Equal(new[] { "teamId", "title", "description" }, input.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Ids_are_guids()
    {
        Assert.True(Guid.TryParse(RequestSender.ProjectId, out _));
        Assert.All(RequestSender.LabelIds, id => Assert.True(Guid.TryParse(id, out _)));
    }

    const string Created = "{\"data\":{\"issueCreate\":{\"success\":true,\"issue\":{\"identifier\":\"AZR-9\"}}}}";
    const string Refused = "{\"errors\":[{\"message\":\"Entity not found: Project\"}]}";

    /// <summary>Serves the given (status, body) answers in order and records each request body.</summary>
    static async Task<(bool ok, string message, List<string> bodies)> Send(params (int status, string body)[] answers)
    {
        var port = 47900 + Random.Shared.Next(500);
        var prefix = $"http://localhost:{port}/graphql/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        var bodies = new List<string>();
        var served = Task.Run(async () =>
        {
            foreach (var (status, body) in answers)
            {
                var ctx = await listener.GetContextAsync();
                using (var rd = new StreamReader(ctx.Request.InputStream)) bodies.Add(await rd.ReadToEndAsync());
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
                ctx.Response.Close();
            }
        });
        var (ok, message) = await RequestSender.SendAsync("lin_api_test", Team, "t", "d", prefix);
        await Task.WhenAny(served, Task.Delay(5000));
        return (ok, message, bodies);
    }

    [Fact]
    public async Task Routed_request_is_sent_once_when_Linear_accepts_it()
    {
        var (ok, message, bodies) = await Send((200, Created));
        Assert.True(ok);
        Assert.Equal("AZR-9", message);
        Assert.Single(bodies);
        Assert.True(Input(bodies[0]).TryGetProperty("projectId", out _));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    public async Task Refused_routing_falls_back_to_the_team_alone(int status)
    {
        var (ok, message, bodies) = await Send((status, Refused), (200, Created));
        Assert.True(ok);
        Assert.Equal("AZR-9", message);
        Assert.Equal(2, bodies.Count);
        Assert.False(Input(bodies[1]).TryGetProperty("projectId", out _));
        Assert.False(Input(bodies[1]).TryGetProperty("labelIds", out _));
    }

    [Fact]
    public async Task Server_errors_are_not_retried()
    {
        var (ok, message, bodies) = await Send((500, "oops"));
        Assert.False(ok);
        Assert.Equal("Linear answered 500.", message);
        Assert.Single(bodies);
    }
}
