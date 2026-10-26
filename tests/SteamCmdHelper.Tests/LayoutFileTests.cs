using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The layout file: Modwright is for RimWorld, so the file only names the layout and nothing in it changes it.</summary>
public sealed class LayoutFileTests : IDisposable
{
    readonly string _repo = Path.Combine(Path.GetTempPath(), "layoutfile_" + Guid.NewGuid().ToString("N"));

    public LayoutFileTests() => Directory.CreateDirectory(_repo);
    public void Dispose() { try { Directory.Delete(_repo, true); } catch { } }

    void Write(string json) => File.WriteAllText(ProjectConfig.PathFor(_repo), json);

    [Fact]
    public void No_file_is_the_RimWorld_layout()
    {
        var c = ProjectConfig.Load(_repo);
        Assert.Equal("294100", c.AppId);
        Assert.Equal("About/About.xml", c.ModMarker);
        Assert.Null(c.Error);
    }

    [Fact]
    public void Saving_writes_only_the_name()
    {
        ProjectConfig.RimWorld().Save(_repo);
        var text = File.ReadAllText(ProjectConfig.PathFor(_repo));
        Assert.Contains("\"Preset\": \"RimWorld\"", text);
        Assert.DoesNotContain("ModMarker", text);
    }

    [Fact]
    public void Layout_fields_in_the_file_are_ignored()
    {
        Write("{ \"Preset\": \"RimWorld\", \"AppId\": \"1\", \"ModMarker\": \"hacked.txt\", \"Changelog\": \"nope.md\" }");
        var c = ProjectConfig.Load(_repo);
        Assert.Equal("294100", c.AppId);
        Assert.Equal("About/About.xml", c.ModMarker);
        Assert.Null(c.Error);
    }

    [Theory]
    [InlineData("{ \"Preset\": \"Generic\", \"AppId\": \"123456\" }", "Generic")]
    [InlineData("{ \"Preset\": \"Custom\", \"AppId\": \"294100\", \"ModMarker\": \"mod.json\" }", "Custom")]
    [InlineData("{ \"Preset\": \"Paradox (descriptor.mod)\", \"AppId\": \"281990\" }", "Paradox")]
    [InlineData("{ \"AppId\": \"431240\", \"ModMarker\": \"Map\" }", "431240")]
    public void A_file_for_another_game_falls_back_to_RimWorld_and_says_so(string json, string named)
    {
        Write(json);
        var c = ProjectConfig.Load(_repo);
        Assert.Equal("294100", c.AppId);
        Assert.Equal("About/About.xml", c.ModMarker);
        Assert.Contains(named, c.Error);
        Assert.Contains("RimWorld", c.Error);
    }

    [Fact]
    public void An_old_RimWorld_file_without_a_name_is_fine()
    {
        Write("{ \"AppId\": \"294100\", \"ModMarker\": \"About/About.xml\", \"NameXPath\": \"ModMetaData/name\" }");
        Assert.Null(ProjectConfig.Load(_repo).Error);
    }

    [Fact]
    public void Leave_out_patterns_are_kept()
    {
        Write("{ \"Preset\": \"RimWorld\", \"LeaveOut\": [\"*.psd\"] }");
        Assert.Equal(new[] { "*.psd" }, ProjectConfig.Load(_repo).LeaveOut);
    }
}
