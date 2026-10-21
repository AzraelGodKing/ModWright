using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public class DisplayTests
{
    [Theory]
    [InlineData(null, "Dark")]
    [InlineData("", "Dark")]
    [InlineData("dark", "Dark")]
    [InlineData("LIGHT", "Light")]
    [InlineData("system", "System")]
    [InlineData("kats", "Kats")]
    [InlineData("HighContrast", "Dark")]
    public void Theme_choice_is_normalized_with_dark_as_default(string? saved, string expected) =>
        Assert.Equal(expected, Display.NormalizeTheme(saved));

    [Theory]
    [InlineData("System", true, "Light")]
    [InlineData("System", false, "Dark")]
    [InlineData("System", null, "Dark")]
    [InlineData("Light", false, "Light")]
    [InlineData("Dark", true, "Dark")]
    [InlineData("Kats", true, "Kats")]
    public void Match_Windows_follows_the_app_mode(string choice, bool? appsUseLight, string expected) =>
        Assert.Equal(expected, Display.ResolveTheme(choice, appsUseLight));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(100, 100)]
    [InlineData(125, 125)]
    [InlineData(130, 125)]
    [InlineData(400, 175)]
    public void Text_size_snaps_to_an_offered_size(int saved, int expected) =>
        Assert.Equal(expected, Display.NormalizeTextSize(saved));

    [Fact]
    public void Log_filters_keep_the_right_lines()
    {
        foreach (var k in Enum.GetValues<LogKind>()) Assert.True(Display.Shows(LogFilter.All, k));
        Assert.True(Display.Shows(LogFilter.WarningsAndErrors, LogKind.Warn));
        Assert.True(Display.Shows(LogFilter.WarningsAndErrors, LogKind.Error));
        Assert.False(Display.Shows(LogFilter.WarningsAndErrors, LogKind.Ok));
        Assert.False(Display.Shows(LogFilter.WarningsAndErrors, LogKind.Note));
        Assert.True(Display.Shows(LogFilter.ErrorsOnly, LogKind.Error));
        Assert.False(Display.Shows(LogFilter.ErrorsOnly, LogKind.Warn));
    }

    [Theory]
    [InlineData("SKIPPED Better Storage: no changelog entry", LogKind.Error)]
    [InlineData("steamcmd: FAILED (Timeout)", LogKind.Error)]
    [InlineData("WARNING: has source art files", LogKind.Warn)]
    [InlineData("Uploaded Better Storage 1.4.0", LogKind.Ok)]
    [InlineData("--- Better Storage ---", LogKind.Note)]
    [InlineData("Packaging 12 files", LogKind.Plain)]
    public void Engine_lines_get_a_kind(string line, LogKind expected) => Assert.Equal(expected, Display.KindOf(line));

    [Theory]
    [InlineData("Mods re&po", "Mods repo")]
    [InlineData("&Browse...", "Browse...")]
    [InlineData("Salt && Pepper", "Salt & Pepper")]
    [InlineData("Plain", "Plain")]
    public void Mnemonic_markers_are_not_read_out(string text, string expected) => Assert.Equal(expected, Display.StripMnemonic(text));

    [Fact]
    public void Steps_done_reads_naturally() => Assert.Equal("3 of 4 done", Display.StepsDone(3, 4));

    [Fact]
    public void Older_settings_get_the_new_defaults()
    {
        var s = new AppSettings();
        Assert.Equal("Dark", s.Theme);
        Assert.Equal(100, s.TextSize);
        Assert.Equal(0, s.LogHeight);
    }

    [Theory]
    [InlineData(null, "Both")]
    [InlineData("", "Both")]
    [InlineData("manager", "Manager")]
    [InlineData("UPLOADER", "Uploader")]
    [InlineData("Everything", "Both")]
    public void Sides_choice_is_normalized_with_both_as_default(string? saved, string expected) =>
        Assert.Equal(expected, Display.NormalizeSides(saved));

    [Theory]
    [InlineData("Both", true, true)]
    [InlineData("Manager", false, true)]
    [InlineData("Uploader", true, false)]
    [InlineData("", true, true)]
    public void Sides_choice_decides_which_tabs_show(string saved, bool uploader, bool dropPod)
    {
        Assert.Equal(uploader, Display.ShowsUploader(saved));
        Assert.Equal(dropPod, Display.ShowsDropPod(saved));
    }

    [Fact]
    public void First_start_asks_for_the_sides_but_existing_settings_keep_both()
    {
        Assert.Equal("", Display.SidesOnLoad(false, null)); // no settings file: a first start, so the choice is asked
        Assert.Equal("Both", Display.SidesOnLoad(true, null)); // settings from before the choice existed
        Assert.Equal("Both", Display.SidesOnLoad(true, ""));
        Assert.Equal("Manager", Display.SidesOnLoad(true, "manager"));
        Assert.Equal("Both", Display.SidesOnLoad(true, "nonsense"));
    }

    [Fact]
    public void Sides_setting_survives_a_save_and_load_round_trip()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings { Sides = Display.Uploader });
        Assert.Equal("Uploader", System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!.Sides);
    }
}
