namespace SteamCmdHelper.Tests;

/// <summary>The tests are written against the RimWorld layout (About/About.xml, About/changelog.txt, app 294100).</summary>
static class TestConfig
{
    /// <summary>
    /// Shell lines for a fake steamcmd that drop SteamCMD's leading "+@Setting value" pairs (<see cref="SteamCmd.FailFast"/>),
    /// so the rest of the fake can read its arguments by position: $1 +login, $2 the account, and so on.
    /// </summary>
    public const string SkipSteamFlags = "while [ \"${1#+@}\" != \"$1\" ]; do shift 2; done\n";

    public static ProjectConfig RimWorld(Action<ProjectConfig>? change = null)
    {
        var c = ProjectConfig.RimWorld();
        change?.Invoke(c);
        return c;
    }
}
