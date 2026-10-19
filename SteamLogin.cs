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
        var psi = new ProcessStartInfo(steamCmd, $"{string.Join(" ", SteamCmd.FailFast)} +login {account} +quit")
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

    public const string DownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

    public static async Task InstallAsync(string steamCmd, Action<string> log, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(steamCmd)!;
        Directory.CreateDirectory(dir);

        log("Downloading SteamCMD from Valve...");
        byte[] bytes;
        long? length;
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
        using (var resp = await http.GetAsync(DownloadUrl, ct))
        {
            resp.EnsureSuccessStatusCode();
            length = resp.Content.Headers.ContentLength;
            bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        }

        log("Checking the download...");
        SteamCmdIntegrity.VerifyDownload(bytes, length);

        // Unpack beside the install first so nothing lands in the SteamCMD folder until the exe's signature checks out.
        var staging = Path.Combine(dir, ".download_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            using (var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read))
                zip.ExtractToDirectory(staging, true);
            SteamCmdIntegrity.VerifyValveSignature(Path.Combine(staging, SteamCmdIntegrity.ExeName));
            foreach (var f in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(dir, Path.GetRelativePath(staging, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }

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
