using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

public static class SteamLogin
{
    public static bool SteamCmdInstalled(string steamCmd) => File.Exists(steamCmd);

    /// <summary>Fast hint only: is this account named in SteamCMD's saved config? The real test is <see cref="VerifyAsync"/>.</summary>
    public static bool LooksCached(string steamCmd, string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return false;
        var cfg = Path.Combine(Path.GetDirectoryName(steamCmd) ?? "", "config");
        foreach (var f in new[] { "config.vdf", "loginusers.vdf" })
        {
            var p = Path.Combine(cfg, f);
            try
            {
                if (File.Exists(p) && Regex.IsMatch(File.ReadAllText(p), "\"" + Regex.Escape(account) + "\"", RegexOptions.IgnoreCase))
                    return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// Real check: run "+login account +quit" with no keyboard attached. A cached login succeeds;
    /// otherwise SteamCMD asks for a password it can never get and gives up.
    /// </summary>
    public static async Task<(bool ok, string output)> VerifyAsync(string steamCmd, string account, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(steamCmd, $"+login {account} +quit")
        {
            WorkingDirectory = Path.GetDirectoryName(steamCmd) ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            ct.ThrowIfCancellationRequested();
        }
        var text = (await outTask) + (await errTask);
        var ok = Regex.IsMatch(text, @"Logged in OK|Waiting for user info\.\.\.OK", RegexOptions.IgnoreCase);
        return (ok, text);
    }

    /// <summary>
    /// First-time login: opens SteamCMD in its own console so the password and Steam Guard code are typed
    /// there (this app never sees them). Returns when that window closes.
    /// </summary>
    public static async Task InteractiveLoginAsync(string steamCmd, string account, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(steamCmd, $"+login {account} +quit")
        {
            WorkingDirectory = Path.GetDirectoryName(steamCmd) ?? "",
            UseShellExecute = true
        };
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
    }

    public static async Task InstallAsync(string steamCmd, Action<string> log, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(steamCmd)!;
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "steamcmd.zip");
        log("Downloading SteamCMD from Valve...");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
        {
            var bytes = await http.GetByteArrayAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", ct);
            await File.WriteAllBytesAsync(zip, bytes, ct);
        }
        ZipFile.ExtractToDirectory(zip, dir, true);
        File.Delete(zip);
        log("Running SteamCMD once so it can update itself (about a minute)...");
        var psi = new ProcessStartInfo(steamCmd, "+quit")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync(ct);
        log("SteamCMD is installed.");
    }
}
