using System.Net;
using System.Text.Json;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Discord announcements, dependency checks, subscriber trends and profiles.</summary>
public sealed class PublishingTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_publishing_" + Guid.NewGuid().ToString("N"));
    public PublishingTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789/abcDEF_-123", true)]
    [InlineData("https://ptb.discord.com/api/webhooks/1/x", true)]
    [InlineData("https://discordapp.com/api/webhooks/1/x/", true)]
    [InlineData("http://discord.com/api/webhooks/1/x", false)]
    [InlineData("https://discord.com.evil.test/api/webhooks/1/x", false)]
    [InlineData("https://example.com/api/webhooks/1/x", false)]
    public void Only_discord_webhook_links_are_accepted(string url, bool ok) => Assert.Equal(ok, Announce.IsDiscordWebhook(url));

    [Fact]
    public void Discord_message_links_the_item_and_never_pings()
    {
        var json = Announce.DiscordPayload("Better Storage", "1.4.0", "2000000001", "[b]New[/b]\n[list][*]Bigger shelves[/list] @everyone", "RimWorld");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Empty(root.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
        var embed = root.GetProperty("embeds")[0];
        Assert.Equal("Better Storage 1.4.0 is out", embed.GetProperty("title").GetString());
        Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/?id=2000000001", embed.GetProperty("url").GetString());
        Assert.Equal("**New**\n- Bigger shelves @everyone", embed.GetProperty("description").GetString());
    }

    [Fact]
    public void Steam_bbcode_becomes_markdown()
    {
        Assert.Equal("[site](https://x.test) *it* __u__ ~~s~~", Announce.SteamToMarkdown("[url=https://x.test]site[/url] [i]it[/i] [u]u[/u] [strike]s[/strike]"));
    }

    sealed class Answer(HttpStatusCode code, List<string> seen) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            seen.Add(await r.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(code) { Content = new StringContent(code == HttpStatusCode.NoContent ? "" : "{\"message\": \"Unknown Webhook\"}") };
        }
    }

    [Fact]
    public async Task Discord_post_reports_what_went_wrong()
    {
        var seen = new List<string>();
        const string hook = "https://discord.com/api/webhooks/1/x";
        Assert.Null(await Announce.PostDiscordAsync(hook, "{}", new HttpClient(new Answer(HttpStatusCode.NoContent, seen))));
        Assert.Equal("{}", seen[0]);
        var e = await Announce.PostDiscordAsync(hook, "{}", new HttpClient(new Answer(HttpStatusCode.NotFound, seen)));
        Assert.Contains("404", e);
        Assert.Contains("Unknown Webhook", e);
        Assert.NotNull(await Announce.PostDiscordAsync("https://example.com/", "{}"));
    }

    [Fact]
    public void Dependencies_are_read_from_workshop_links_and_checked_against_steam()
    {
        var dir = Path.Combine(_root, "Mod", "About");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "About.xml"), """
            <ModMetaData><modDependencies>
              <li><packageId>brrainz.harmony</packageId><steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl></li>
              <li><steamWorkshopUrl>https://steamcommunity.com/sharedfiles/filedetails/?id=1111111111</steamWorkshopUrl></li>
              <li><steamWorkshopUrl>https://steamcommunity.com/workshop/filedetails/?l=english&amp;id=2222222222</steamWorkshopUrl></li>
              <li><steamWorkshopUrl>https://steamcommunity.com/sharedfiles/filedetails/?id=3333333333</steamWorkshopUrl></li>
            </modDependencies></ModMetaData>
            """);
        var mod = new ModInfo { Dir = Path.Combine(_root, "Mod"), WorkshopId = "3333333333" };

        var ids = Dependencies.Find(mod, TestConfig.RimWorld());

        Assert.Equal(new[] { "2009463077", "1111111111", "2222222222" }, ids); // its own id is left out
        var live = new Dictionary<string, WorkshopItem>
        {
            ["2009463077"] = new("2009463077", true, "Harmony"),
            ["1111111111"] = new("1111111111", false),
            ["2222222222"] = new("2222222222", true, "Gone", Banned: true),
        };
        var w = Dependencies.Problems(ids, live);
        Assert.Equal(2, w.Count);
        Assert.Contains("1111111111, which is not public", w[0]);
        Assert.Contains("banned", w[1]);
    }

    [Fact]
    public void Subscriber_counts_are_kept_once_a_day_and_show_growth()
    {
        var file = Path.Combine(_root, "stats.json");
        var day = new DateOnly(2026, 10, 1);
        SubscriberStats.Record(new[] { new WorkshopItem("1", true, Subscribers: 100), new WorkshopItem("2", false) }, day, file);
        SubscriberStats.Record(new[] { new WorkshopItem("1", true, Subscribers: 105) }, day, file); // same day: replaced
        var all = SubscriberStats.Record(new[] { new WorkshopItem("1", true, Subscribers: 160) }, day.AddDays(8), file);

        Assert.False(all.ContainsKey("2"));
        Assert.Equal(new long[] { 105, 160 }, all["1"].Select(p => p.Subscribers));
        Assert.Equal("Subscribers: 160 (+55 this week, +55 since the last upload on 3 Oct)",
            SubscriberStats.Trend(all["1"], day.AddDays(8), day.AddDays(2)));
        Assert.Equal("Subscribers: 105 (growth shows from tomorrow)", SubscriberStats.Trend(all["1"].Take(1).ToList(), day, null));
        Assert.Null(SubscriberStats.Trend(null, day, null));
    }

    [Fact]
    public void Profiles_switch_folder_account_and_steamcmd_together()
    {
        var s = new AppSettings { RepoPath = @"C:\mods\rim", Account = "alice" };
        s.SaveProfile("RimWorld");
        s.RepoPath = @"C:\mods\golf"; s.Account = "bob"; s.SteamCmdPath = @"D:\steamcmd\steamcmd.exe";
        s.SaveProfile("Golf");
        Assert.Equal("Golf", s.CurrentProfile()!.Name);

        s.UseProfile(s.Profiles.First(p => p.Name == "RimWorld"));

        Assert.Equal(@"C:\mods\rim", s.RepoPath);
        Assert.Equal("alice", s.Account);
        Assert.Equal("", s.SteamCmdPath);
        Assert.Equal("RimWorld", s.CurrentProfile()!.Name);
        Assert.Equal(new[] { "Golf", "RimWorld" }, s.Profiles.Select(p => p.Name));
        s.SaveProfile("golf"); // same name, any case: replaced
        Assert.Equal(2, s.Profiles.Count);
    }

    [Fact]
    public void Announcement_settings_are_per_mods_folder()
    {
        var s = new AppSettings();
        s.SetWebhook(@"C:\Mods\", "https://discord.com/api/webhooks/1/x");
        s.SetGitHubReleases(@"C:\Mods", true);
        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;

        Assert.Equal("https://discord.com/api/webhooks/1/x", reloaded.WebhookFor(@"c:\mods"));
        Assert.True(reloaded.GitHubReleasesFor(@"c:\mods\"));
        Assert.Equal("", reloaded.WebhookFor(@"C:\Other"));
        reloaded.SetWebhook(@"C:\mods", "");
        Assert.Equal("", reloaded.WebhookFor(@"C:\Mods"));
    }
}
