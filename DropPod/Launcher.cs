using System.Diagnostics;

namespace SteamCmdHelper.DropPod;

/// <summary>
/// Starting the game the way the Drop Pod does, shared by the Play button, the taskbar jump list and
/// "SteamCmdHelper.exe --play": the picked instance's folders, its launch options, and its frozen mods linked in for as
/// long as the game runs.
/// </summary>
public static class Launcher
{
    /// <summary>The program, arguments and folder to start, or null when the game's program isn't found.</summary>
    public static ProcessStartInfo? StartInfo(IGameModule game, GameInstall install, GameInstance? inst, string extraArgs)
    {
        var launch = game.Launch(install, inst != null, inst?.LogFile);
        if (launch == null) return null;
        var psi = new ProcessStartInfo(launch.Exe) { WorkingDirectory = launch.WorkingDirectory, UseShellExecute = false };
        foreach (var a in launch.Args) psi.ArgumentList.Add(a);
        var extra = LaunchArgs.Split(extraArgs);
        foreach (var a in extra.Except(LaunchArgs.Clashes(extra))) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>The game's Mods folder, where links go.</summary>
    public static string ModsFolder(GameInstall install) =>
        SafeFiles.Child(install.GameFolder, "Mods", directory: true) ?? Path.Combine(install.GameFolder, "Mods");

    /// <summary>
    /// Starts the game. A frozen instance gets its copies linked into the Mods folder first, and <paramref name="exited"/>
    /// runs (on a worker thread) once the game closes, after the links are taken out again.
    /// </summary>
    public static Process Start(ProcessStartInfo psi, IGameModule game, GameInstall install, GameInstance? inst, Action<string>? exited = null)
    {
        var mods = ModsFolder(install);
        var linked = 0;
        if (inst != null && Freezer.IsFrozen(inst))
        {
            Freezer.Unlink(mods);
            linked = Freezer.LinkIn(inst, mods).Count;
        }
        Process p;
        try { p = Process.Start(psi) ?? throw new IOException("The game did not start."); }
        catch
        {
            if (linked > 0) Freezer.Unlink(mods);
            throw;
        }
        if (linked > 0 || exited != null)
            _ = Task.Run(async () =>
            {
                try { await p.WaitForExitAsync(); } catch { }
                var removed = linked > 0 ? Freezer.Unlink(mods) : 0;
                exited?.Invoke(removed > 0 ? $"Took the {removed} frozen mod link{(removed == 1 ? "" : "s")} out of the Mods folder again." : "");
            });
        return p;
    }
}
