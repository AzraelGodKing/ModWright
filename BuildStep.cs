using System.Diagnostics;
using System.Text;

namespace SteamCmdHelper;

/// <summary>
/// A command run in the mods folder before anything is packaged (for example "dotnet build -c Release"), set per mods
/// folder under Options > Upload. Nothing is uploaded when it fails, so a broken build never reaches subscribers.
/// It comes from this PC's settings, never from the repo, so opening someone else's folder runs nothing.
/// </summary>
public static class BuildStep
{
    /// <summary>
    /// Runs <paramref name="command"/> through the system shell (cmd on Windows, sh elsewhere) in <paramref name="repo"/>,
    /// passing each line it prints to <paramref name="onLine"/>. Returns its exit code. Cancelling stops it.
    /// </summary>
    public static async Task<int> RunAsync(string repo, string command, Action<string> onLine, CancellationToken ct)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/s", "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        psi.WorkingDirectory = repo;
        psi.RedirectStandardOutput = psi.RedirectStandardError = psi.RedirectStandardInput = true;
        psi.StandardOutputEncoding = psi.StandardErrorEncoding = Encoding.UTF8;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.StandardInput.Close(); // nothing to type into: a command that asks fails instead of hanging
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }

    /// <summary>
    /// Runs the build step when there is one and says how it went. True when there is none or it succeeded; false
    /// (after saying why) when it failed or could not start.
    /// </summary>
    public static async Task<bool> RunCheckedAsync(string repo, string? command, Action<string> say, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) return true;
        say("Build step: " + command);
        int code;
        try { code = await RunAsync(repo, command, l => say("    " + l), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { say("The build step could not start: " + e.Message + ". Nothing was uploaded."); return false; }
        if (code == 0) { say("Build step finished."); return true; }
        say($"The build step failed (exit code {code}), so nothing was uploaded. Its output is above.");
        return false;
    }
}
