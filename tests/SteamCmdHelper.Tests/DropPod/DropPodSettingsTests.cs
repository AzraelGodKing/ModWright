using System.Text.Json;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Settings files from before the Drop Pod still load, and the new fields round-trip.</summary>
public sealed class DropPodSettingsTests
{
    [Fact]
    public void A_1_6_settings_file_loads_with_empty_game_folders()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("""{ "RepoPath": "C:\\mods", "Account": "me", "Theme": "Light", "Profiles": [] }""")!;
        Assert.Equal("me", s.Account);
        Assert.Empty(s.GameFolders);
        Assert.Empty(s.GameDataFolders);
    }

    [Fact]
    public void Game_folders_round_trip()
    {
        var s = new AppSettings { GameFolders = { ["294100"] = "D:\\RimWorld" } };
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.Equal("D:\\RimWorld", back.GameFolders["294100"]);
    }
}
