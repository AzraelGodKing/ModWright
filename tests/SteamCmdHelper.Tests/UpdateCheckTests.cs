using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Reading GitHub's latest release and deciding whether it is newer than the running program.</summary>
public class UpdateCheckTests
{
    const string Release = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/AzraelGodKing/ModWright/releases/tag/v1.2.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.test/SHA256SUMS.txt" },
            { "name": "SteamCmdHelper-1.2.0-win-x64-framework-dependent.zip", "browser_download_url": "https://example.test/fd.zip" },
            { "name": "SteamCmdHelper-1.2.0-win-x64.exe", "browser_download_url": "https://example.test/app.exe" }
          ]
        }
        """;

    [Fact]
    public void Release_is_read_with_the_standalone_exe_as_the_download()
    {
        var r = UpdateCheck.Parse(Release);

        Assert.NotNull(r);
        Assert.Equal("1.2.0", r!.Version);
        Assert.Equal("https://github.com/AzraelGodKing/ModWright/releases/tag/v1.2.0", r.PageUrl);
        Assert.Equal("https://example.test/app.exe", r.DownloadUrl);
    }

    [Fact]
    public void Zip_is_the_download_when_there_is_no_exe()
    {
        var r = UpdateCheck.Parse("""{ "tag_name": "1.3.0", "assets": [ { "name": "x.zip", "browser_download_url": "https://example.test/x.zip" } ] }""");

        Assert.Equal("1.3.0", r!.Version);
        Assert.Equal("https://example.test/x.zip", r.DownloadUrl);
        Assert.Equal(UpdateCheck.ReleasesPage, r.PageUrl);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v2.0.0", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "v2.0.0", "draft": true }""")]
    [InlineData("""{ "tag_name": "nightly" }""")]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("[]")]
    public void Unusable_answers_give_nothing(string json)
    {
        Assert.Null(UpdateCheck.Parse(json));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("1.1.0", "1.0.0-beta", true)]
    [InlineData("garbage", "1.0.0", false)]
    [InlineData("1.1.0", "", false)]
    public void Newer_is_decided_number_by_number(string latest, string current, bool newer)
    {
        Assert.Equal(newer, UpdateCheck.IsNewer(latest, current));
    }
}
