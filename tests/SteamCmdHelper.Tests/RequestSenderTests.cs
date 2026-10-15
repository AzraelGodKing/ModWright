using System.Net;
using System.Text;
using System.Text.Json;
using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public class RequestSenderTests
{
    const string Team = "11111111-2222-3333-4444-555555555555";
    const string Project = "22222222-3333-4444-5555-666666666666";
    static readonly string[] Labels = { "33333333-4444-5555-6666-777777777777", "44444444-5555-6666-7777-888888888888" };
    static readonly RequestSender.Route Routed = new(Project, Labels);

    static JsonElement Input(string body) => JsonDocument.Parse(body).RootElement.GetProperty("variables").GetProperty("input");

    [Fact]
    public void BuildBody_routes_into_the_project_with_its_labels()
    {
        var input = Input(RequestSender.BuildBody(Team, "Game layout request: X", "details", Routed));
        Assert.Equal(Team, input.GetProperty("teamId").GetString());
        Assert.Equal(Project, input.GetProperty("projectId").GetString());
        Assert.Equal(Labels, input.GetProperty("labelIds").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void BuildBody_without_a_route_sends_only_team_title_and_description()
    {
        foreach (var route in new[] { null, RequestSender.Route.None })
        {
            var input = Input(RequestSender.BuildBody(Team, "t", "d", route));
            Assert.Equal(new[] { "teamId", "title", "description" }, input.EnumerateObject().Select(p => p.Name).ToArray());
        }
    }

    [Fact]
    public void BuildBody_sends_labels_without_a_project()
    {
        var input = Input(RequestSender.BuildBody(Team, "t", "d", new RequestSender.Route(null, Labels)));
        Assert.False(input.TryGetProperty("projectId", out _));
        Assert.Equal(2, input.GetProperty("labelIds").GetArrayLength());
    }

    [Theory]
    [InlineData("33333333-4444-5555-6666-777777777777,44444444-5555-6666-7777-888888888888")]
    [InlineData(" 33333333-4444-5555-6666-777777777777 ; 44444444-5555-6666-7777-888888888888 ")]
    [InlineData("33333333-4444-5555-6666-777777777777\n44444444-5555-6666-7777-888888888888, not-a-guid")]
    [InlineData("\"33333333-4444-5555-6666-777777777777\", 44444444-5555-6666-7777-888888888888, 33333333-4444-5555-6666-777777777777")]
    [InlineData("[\"33333333-4444-5555-6666-777777777777\",\"44444444-5555-6666-7777-888888888888\"]")]
    [InlineData("Layout Request: 33333333-4444-5555-6666-777777777777\r\nSteamCMD Helper: 44444444-5555-6666-7777-888888888888")]
    [InlineData("'33333333-4444-5555-6666-777777777777' '44444444-5555-6666-7777-888888888888'")]
    [InlineData("33333333-4444-5555-6666-777777777777,44444444-5555-6666-7777-888888888888,")]
    public void Route_Parse_reads_label_lists(string labels)
    {
        var route = RequestSender.Route.Parse(" " + Project + " ", labels);
        Assert.Equal(Project, route.ProjectId);
        Assert.Equal(Labels, route.LabelIds);
    }

    [Fact]
    public void Route_Parse_ignores_missing_or_bad_values()
    {
        Assert.True(RequestSender.Route.Parse(null, null).IsEmpty);
        Assert.True(RequestSender.Route.Parse("", "").IsEmpty);
        Assert.True(RequestSender.Route.Parse("nope", "x, y").IsEmpty);
        Assert.True(RequestSender.Route.Parse(null, "Layout Request, SteamCMD Helper").IsEmpty);
        Assert.Equal(Project, RequestSender.Route.Parse("\"" + Project.ToUpperInvariant() + "\"\n", null).ProjectId);
    }

    const string Created = "{\"data\":{\"issueCreate\":{\"success\":true,\"issue\":{\"identifier\":\"AZR-9\"}}}}";
    const string Refused = "{\"errors\":[{\"message\":\"Entity not found: Project\"}]}";

    /// <summary>Serves the given (status, body) answers in order and records each request body.</summary>
    static Task<(bool ok, string message, List<string> bodies)> Send(params (int status, string body)[] answers) => Send(Routed, answers);

    static async Task<(bool ok, string message, List<string> bodies)> Send(RequestSender.Route route, params (int status, string body)[] answers)
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
        var (ok, message) = await RequestSender.SendAsync("lin_api_test", Team, route, "t", "d", prefix);
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
    public async Task Refusal_without_a_route_is_not_retried()
    {
        var (ok, message, bodies) = await Send(RequestSender.Route.None, (200, Refused));
        Assert.False(ok);
        Assert.Contains("Entity not found", message);
        Assert.Single(bodies);
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
