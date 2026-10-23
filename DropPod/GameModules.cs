using SteamCmdHelper.DropPod.RimWorld;
using SteamCmdHelper.DropPod.SunHaven;

namespace SteamCmdHelper.DropPod;

/// <summary>The games The Drop Pod knows, in the order the game picker shows them.</summary>
public static class DropPodGames
{
    public static IReadOnlyList<IGameModule> All { get; } = new IGameModule[] { new RimWorldModule(), new SunHavenModule() };

    /// <summary>The game with this Steam app id, else RimWorld.</summary>
    public static IGameModule For(string? appId) => All.FirstOrDefault(g => g.AppId == appId) ?? All[0];
}
