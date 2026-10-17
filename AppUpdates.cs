using Velopack;
using Velopack.Sources;

namespace SteamCmdHelper;

/// <summary>A downloaded update, ready to install once this program closes.</summary>
/// <param name="Install">
/// Installs it as soon as this process exits: (version being replaced, start the new version afterwards).
/// </param>
public sealed record PendingUpdate(string Version, Action<string, bool> Install);

/// <summary>
/// Picks how this copy updates itself. A copy installed with the Setup.exe updates the way most desktop apps do:
/// Velopack's Update.exe, a separate program, swaps the versions once this one has exited, so nothing ever replaces the
/// files of a running program. The standalone exe and the framework-dependent zip have no Update.exe beside them, so
/// they keep the handoff in <see cref="SelfUpdate"/>.
/// </summary>
public static class AppUpdates
{
    /// <summary>
    /// The Velopack package id. Setup installs to %LocalAppData%\&lt;id&gt;, so it must not be "SteamCmdHelper", the
    /// settings folder.
    /// </summary>
    public const string PackId = "AzraelGodKing.SteamCMDHelper";

    static readonly Lazy<UpdateManager?> Manager = new(() =>
    {
        try
        {
            var m = new UpdateManager(new GithubSource("https://github.com/" + UpdateCheck.Repo, null, false));
            return m.IsInstalled ? m : null;
        }
        catch { return null; }
    });

    /// <summary>True when this copy was installed with the Setup.exe (and so updates through Velopack).</summary>
    public static bool IsInstalled => Manager.Value != null;

    /// <summary>
    /// Why this copy cannot update itself to <paramref name="r"/> (shown with the browser download instead), or null
    /// when it can.
    /// </summary>
    public static string? CannotUpdateReason(ReleaseInfo r) =>
        IsInstalled ? null : SelfUpdate.CannotUpdateReason(r, SelfUpdate.AppDir);

    /// <summary>Downloads <paramref name="r"/> and checks it, without changing the installed program.</summary>
    public static async Task<PendingUpdate> DownloadAsync(ReleaseInfo r, IProgress<int>? progress, CancellationToken ct)
    {
        if (Manager.Value is not { } m)
        {
            var u = await SelfUpdate.PrepareAsync(r, SelfUpdate.KindOf(SelfUpdate.AppDir), progress, ct);
            var exe = Path.GetFileName(Environment.ProcessPath ?? SelfUpdate.ExeName);
            return new PendingUpdate(u.Version, (from, restart) => SelfUpdate.HandOff(u, SelfUpdate.AppDir, exe, from, restart));
        }

        var info = await m.CheckForUpdatesAsync()
            ?? throw new InvalidOperationException($"The {r.Version} release has no update package for installed copies. Download it from {r.PageUrl}.");
        ct.ThrowIfCancellationRequested();
        // Velopack checks each package's SHA-1 and SHA-256 against its releases.win.json before using it.
        await m.DownloadUpdatesAsync(info, p => progress?.Report(p), cancelToken: ct);
        var asset = info.TargetFullRelease;
        return new PendingUpdate(asset.Version.ToString(), (from, restart) =>
            m.WaitExitThenApplyUpdates(asset, silent: !restart, restart: restart,
                restartArgs: restart ? new[] { SelfUpdate.UpdatedFromArg, from } : Array.Empty<string>()));
    }
}
