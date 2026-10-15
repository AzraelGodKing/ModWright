using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Live item details from Steam, the upload history, and how the "Uploaded version" column picks its text.</summary>
public sealed class WorkshopStatusTests : IDisposable
{
    readonly TempRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    // Trimmed from a real GetPublishedFileDetails answer; the second id is a private item.
    const string Answer = """
        {"response":{"result":1,"resultcount":2,"publishedfiledetails":[
          {"publishedfileid":"2001112223","result":1,"creator":"7656","creator_app_id":294100,"consumer_app_id":294100,
           "file_size":"2621440","title":"My Mod","time_created":1700000000,"time_updated":1760000000,
           "visibility":0,"banned":0,"subscriptions":1234,"favorited":5,"tags":[{"tag":"Mod"}]},
          {"publishedfileid":"3004445556","result":9}
        ]}}
        """;

    [Fact]
    public void Public_item_details_are_read()
    {
        var item = WorkshopStatus.Parse(Answer)["2001112223"];
        Assert.True(item.Public);
        Assert.Equal("My Mod", item.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), item.Updated);
        Assert.Equal(1234, item.Subscribers);
        Assert.Equal(2621440, item.Size); // sent as a string
        Assert.Equal("public", item.VisibilityText);
        Assert.False(item.Banned);
    }

    [Fact]
    public void Private_item_reads_as_not_public()
    {
        var item = WorkshopStatus.Parse(Answer)["3004445556"];
        Assert.False(item.Public);
        Assert.Equal("not public", item.UpdatedText);
        Assert.Contains("not publicly visible", item.Summary);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"response":{"result":1,"resultcount":0}}""")]
    public void Empty_answer_has_no_items(string json)
    {
        Assert.Empty(WorkshopStatus.Parse(json));
    }

    [Fact]
    public void Banned_item_says_so()
    {
        var item = WorkshopStatus.Parse("""{"response":{"publishedfiledetails":[{"publishedfileid":"5","result":1,"banned":1,"time_updated":1}]}}""")["5"];
        Assert.Equal("banned", item.UpdatedText);
        Assert.Contains("BANNED", item.Summary);
    }

    [Fact]
    public async Task Request_lists_every_id()
    {
        var body = await WorkshopStatus.Body(new[] { "11", "22" }).ReadAsStringAsync();
        Assert.Equal("itemcount=2&publishedfileids%5B0%5D=11&publishedfileids%5B1%5D=22", body);
    }

    // ------------------------------------------------------------ upload history

    string HistoryFile => Path.Combine(_repo.Root, "history.json");

    static UploadEntry Entry(string id, string version, int minutesAgo, string note = "Fixed things.") =>
        new(DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), id, "My Mod", version, note);

    [Fact]
    public void History_keeps_every_upload_newest_first_per_item()
    {
        UploadHistory.Add(Entry("1", "1.0.0", 30), HistoryFile);
        UploadHistory.Add(Entry("2", "0.1.0", 20), HistoryFile);
        UploadHistory.Add(Entry("1", "1.1.0", 10, "Line one\nLine two"), HistoryFile);

        var all = UploadHistory.Load(HistoryFile);
        Assert.Equal(3, all.Count);
        var one = UploadHistory.For(all, "1");
        Assert.Equal(new[] { "1.1.0", "1.0.0" }, one.Select(e => e.Version));
        Assert.Equal("Line one\nLine two", one[0].ChangeNote);
        Assert.Equal("1.1.0", UploadHistory.Latest(all)["1"].Version);
        Assert.Equal("0.1.0", UploadHistory.Latest(all)["2"].Version);
    }

    [Fact]
    public void History_drops_the_oldest_past_the_limit()
    {
        var many = Enumerable.Range(0, UploadHistory.Keep).Select(i => Entry("1", "0.0." + i, UploadHistory.Keep + 10 - i)).ToList();
        File.WriteAllText(HistoryFile, System.Text.Json.JsonSerializer.Serialize(many));
        UploadHistory.Add(Entry("1", "9.9.9", 0), HistoryFile);

        var all = UploadHistory.Load(HistoryFile);
        Assert.Equal(UploadHistory.Keep, all.Count);
        Assert.Equal("0.0.1", all[0].Version);
        Assert.Equal("9.9.9", all[^1].Version);
    }

    [Fact]
    public void Damaged_history_reads_as_empty()
    {
        File.WriteAllText(HistoryFile, "{ not json");
        Assert.Empty(UploadHistory.Load(HistoryFile));
    }

    // ------------------------------------------------------------ what "Uploaded version" shows

    static WorkshopItem Live(int minutesAgo) => new("1", true, "My Mod", DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

    [Fact]
    public void Steam_change_note_version_wins()
    {
        Assert.Equal("2.0.0", UploadedVersions.Resolve("2.0.0", Live(1), Entry("1", "1.0.0", 60), "0.9.0").text);
    }

    [Fact]
    public void This_pcs_last_upload_is_used_when_steam_has_nothing_newer()
    {
        // Steam stamps the update a little after the upload is recorded.
        var (text, note) = UploadedVersions.Resolve(null, Live(55), Entry("1", "1.0.0", 60), null);
        Assert.Equal("1.0.0", text);
        Assert.Contains("uploaded from this PC", note);
    }

    [Fact]
    public void A_newer_update_on_steam_makes_the_version_unknown()
    {
        var (text, note) = UploadedVersions.Resolve(null, Live(5), Entry("1", "1.0.0", 60 * 24), null);
        Assert.Equal("?", text);
        Assert.Contains("after this PC uploaded 1.0.0", note);
    }

    [Fact]
    public void Older_record_is_used_when_there_is_no_history()
    {
        Assert.Equal("0.9.0", UploadedVersions.Resolve(null, Live(5), null, "0.9.0").text);
    }

    [Fact]
    public void Unknown_without_any_source()
    {
        Assert.Equal("?", UploadedVersions.Resolve(null, null, null, null).text);
    }
}
