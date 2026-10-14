namespace SteamCmdHelper.Tests;

/// <summary>The tests are written against the RimWorld layout (About/About.xml, About/changelog.txt, app 294100).</summary>
static class TestConfig
{
    public static ProjectConfig RimWorld(Action<ProjectConfig>? change = null)
    {
        var c = ProjectConfig.GetPreset("RimWorld");
        change?.Invoke(c);
        return c;
    }
}
