using System.Diagnostics;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>
/// Brings the Steam client back online after SteamCMD has logged in. Steam keeps one session per account, so SteamCMD's
/// login takes the client's place and the client sits disconnected until it signs in again. Steam has no command that
/// reconnects it, so it is closed ("steam.exe -shutdown") and started again in the tray ("-silent"), where its saved
/// login ("Remember me") signs it straight back in.
/// </summary>
public static class SteamRestart
{
    /// <summary>What the Steam client is doing, as Steam records it in the registry.</summary>
    /// <param name="Running">A steam.exe process is running.</param>
    /// <param name="Exe">steam.exe's path; null when Steam is not installed.</param>
    /// <param name="RememberedAccount">The account Steam signs in as by itself ("Remember me"); empty when none.</param>
    /// <param name="SignedIn">Steam says an account is signed in.</param>
    /// <param name="RunningGame">The app id of a game Steam is running; 0 when none.</param>
    public sealed record State(bool Running, string? Exe, string RememberedAccount, bool SignedIn, int RunningGame);

    /// <summary>What a restart looks at and does, so tests can stand in for Steam.</summary>
    public sealed class Machine
    {
        public Func<State> Read { get; init; } = ReadState;
        /// <summary>Starts steam.exe with these arguments.</summary>
        public Action<string, string> Start { get; init; } = StartSteam;
        public Func<TimeSpan, CancellationToken, Task> Wait { get; init; } = Task.Delay;
        public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(60);
        public TimeSpan SignInTimeout { get; init; } = TimeSpan.FromSeconds(90);
        public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(1);
        /// <summary>After Steam says it is signed in, the time it gets to finish starting before anything talks to it.</summary>
        public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(8);
    }

    /// <summary>
    /// Looks at Steam before an upload. When it is running, returns what brings it back afterwards (for
    /// <see cref="Publisher.SteamClientBack"/>); null when there is nothing to bring back (Steam closed, not Windows).
    /// </summary>
    public static Func<CancellationToken, Task<string?>>? Prepare(string account, Machine? machine = null)
    {
        if (machine == null && !OperatingSystem.IsWindows()) return null;
        machine ??= new Machine();
        try { if (!machine.Read().Running) return null; }
        catch { return null; }
        return async ct => await RestartAsync(account, machine, ct);
    }

    /// <summary>
    /// Why Steam should be left as it is now, or null to restart it. <paramref name="account"/> is the account SteamCMD
    /// used: Steam remembering another one was never logged out. Null skips that check (Restart Steam now).
    /// </summary>
    public static string? SkipReason(State now, string? account)
    {
        if (!now.Running) return "Steam is not running now, so it was left closed.";
        if (now.Exe == null) return "Steam's install could not be found, so it was not restarted. Click Go Online in Steam, or restart it yourself.";
        if (account is { Length: > 0 } && now.RememberedAccount.Length > 0 && !now.RememberedAccount.Equals(account, StringComparison.OrdinalIgnoreCase))
            return $"Steam signs in as {now.RememberedAccount}, not {account}, so SteamCMD did not take its place and it was left as it is.";
        if (now.RunningGame != 0)
            return $"A game (app {now.RunningGame}) is running through Steam, so Steam was not restarted. After closing it, " +
                   "Options > Uploading > Restart Steam now brings Steam back online.";
        return null;
    }

    /// <summary>
    /// Closes Steam, starts it again in the tray and waits for it to sign in. Returns a line for the log saying what
    /// happened; never throws except for cancellation.
    /// </summary>
    /// <param name="account">The account SteamCMD used; null restarts whichever account Steam has (Restart Steam now).</param>
    public static async Task<string> RestartAsync(string? account, Machine? machine = null, CancellationToken ct = default)
    {
        machine ??= new Machine();
        State now;
        try { now = machine.Read(); }
        catch (Exception e) { return "Warning: could not look at the Steam client: " + e.Message; }
        if (SkipReason(now, account) is { } skip) return skip;

        try { machine.Start(now.Exe!, "-shutdown"); }
        catch (Exception e) { return "Warning: could not ask Steam to close: " + e.Message; }
        if (!await UntilAsync(machine, s => !s.Running, machine.CloseTimeout, ct))
            return $"Warning: Steam did not close within {machine.CloseTimeout.TotalSeconds:0} seconds, so it was not started again. Click Go Online in Steam, or restart it yourself.";

        try { machine.Start(now.Exe!, "-silent"); }
        catch (Exception e) { return "Warning: Steam closed but could not be started again: " + e.Message; }
        if (!await UntilAsync(machine, s => s.Running && s.SignedIn, machine.SignInTimeout, ct))
            return now.RememberedAccount.Length == 0
                ? "Warning: Steam was restarted, but it has no saved login, so it is waiting for you to sign in (tick \"Remember me\" to skip this next time)."
                : "Warning: Steam was restarted but has not signed in yet. It may still be connecting; if not, sign in from its window.";
        await machine.Wait(machine.Settle, ct);
        return "Steam is back online" + (now.RememberedAccount.Length > 0 ? " as " + now.RememberedAccount : "") + ".";
    }

    static async Task<bool> UntilAsync(Machine machine, Func<State, bool> done, TimeSpan timeout, CancellationToken ct)
    {
        for (var waited = TimeSpan.Zero; ; waited += machine.Poll)
        {
            try { if (done(machine.Read())) return true; }
            catch { }
            if (waited >= timeout) return false;
            await machine.Wait(machine.Poll, ct);
        }
    }

    const string Key = @"HKEY_CURRENT_USER\Software\Valve\Steam";

    /// <summary>The Steam client's state on this PC; "not running" off Windows.</summary>
    public static State ReadState()
    {
        if (!OperatingSystem.IsWindows()) return new State(false, null, "", false, 0);
        var procs = Process.GetProcessesByName("steam");
        var running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        // Steam stores its path with forward slashes.
        var exe = (Registry.GetValue(Key, "SteamExe", null) as string)?.Replace('/', '\\');
        if (exe != null && !File.Exists(exe)) exe = null;
        return new State(running, exe,
            Registry.GetValue(Key, "AutoLoginUser", null) as string ?? "",
            Registry.GetValue(Key + @"\ActiveProcess", "ActiveUser", 0) is int user && user != 0,
            Registry.GetValue(Key, "RunningAppID", 0) is int game ? game : 0);
    }

    static void StartSteam(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" })?.Dispose();
}
