using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Modwright's own updates: checking, downloading and installing them.</summary>
public sealed partial class MainForm
{
    /// <summary>
    /// Looks for a newer release on GitHub. At start-up (<paramref name="manual"/> false) it stays quiet when up to date,
    /// offline, or the release was skipped; "Check now" always says what it found. With automatic updates on, a start-up
    /// check downloads the release in the background and installs it when the window closes, without asking.
    /// </summary>
    async Task CheckForUpdateAsync(bool manual)
    {
        if (_pendingUpdate != null) { if (manual) RestartToUpdate(); return; }
        _checkUpdateNow.Enabled = false;
        ReleaseInfo? r;
        try { r = await UpdateCheck.FindNewerAsync(UpdateCheck.CurrentVersion); }
        finally { _checkUpdateNow.Enabled = !_busy; }
        if (IsDisposed) return;
        if (r == null)
        {
            if (manual) MessageBox.Show(this, $"No newer version found. You have Modwright {UpdateCheck.CurrentVersion} (or GitHub could not be reached).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!manual && r.Version == _s.SkippedUpdate) return;
        Append($"Modwright {r.Version} is available (you have {UpdateCheck.CurrentVersion}): {r.PageUrl}", LogKind.Note);

        var cannot = AppUpdates.CannotUpdateReason(r);
        if (!manual && _s.AutoUpdate && cannot == null)
        {
            try
            {
                _pendingUpdate = await AppUpdates.DownloadAsync(r, null, CancellationToken.None);
                ShowUpdateReady();
            }
            catch (Exception e) { Append($"Could not download {r.Version} automatically: {e.Message}", LogKind.Warn); }
            return;
        }

        var update = new TaskDialogButton("Update now");
        var download = new TaskDialogButton("Download");
        var notes = new TaskDialogButton("Release notes");
        var later = new TaskDialogButton("Later");
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"Modwright {r.Version} is available",
            Text = $"You have {UpdateCheck.CurrentVersion}. " + (cannot == null
                ? "Update now downloads it, checks it, and restarts the app."
                : r.DownloadUrl != null
                    ? cannot + " Download saves the new exe through your browser; close this one and run the new one."
                    : "Open the release page to download it."),
            Icon = TaskDialogIcon.Information,
            Buttons = { update, download, notes, later },
            Verification = new TaskDialogVerificationCheckBox($"Don't remind me about {r.Version} again"),
        };
        if (cannot != null) page.Buttons.Remove(update); else page.Buttons.Remove(download);
        if (r.DownloadUrl == null) page.Buttons.Remove(download);
        page.DefaultButton = cannot == null ? update : later;
        var picked = TaskDialog.ShowDialog(this, page);
        _s.SkippedUpdate = page.Verification.Checked ? r.Version : "";
        _s.Save();
        if (picked == update) { UpdateNow(r); return; }
        var url = picked == download ? r.DownloadUrl : picked == notes ? r.PageUrl : null;
        if (url == null) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Append("Could not open the browser: " + e.Message + " Download it from " + r.PageUrl, LogKind.Error); }
    }

    /// <summary>Downloads the release with a progress dialog, then offers to restart into it now or when the app closes.</summary>
    void UpdateNow(ReleaseInfo r)
    {
        using var cts = new CancellationTokenSource();
        var cancel = TaskDialogButton.Cancel;
        var progress = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"Downloading Modwright {r.Version}...",
            Text = "The download is checked against the release's SHA-256 checksums before anything is changed.",
            ProgressBar = new TaskDialogProgressBar { Minimum = 0, Maximum = 100, Value = 0 },
            Buttons = { cancel },
            AllowCancel = true,
        };
        cancel.Click += (_, _) => cts.Cancel();
        Exception? error = null;
        var restart = new TaskDialogButton("Restart now");
        var onClose = new TaskDialogButton("When I close the app");
        var ready = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"Modwright {r.Version} is ready",
            Text = _busy ? "An upload is running, so it will be installed when you close the app." : "Restart now to start using it, or it is installed when you close the app.",
            Icon = TaskDialogIcon.ShieldSuccessGreenBar,
            Buttons = { restart, onClose },
            DefaultButton = restart,
        };
        if (_busy) ready.Buttons.Remove(restart);
        progress.Created += async (_, _) =>
        {
            try
            {
                var p = new Progress<int>(v => { if (progress.BoundDialog != null) progress.ProgressBar!.Value = v; });
                var prepared = await AppUpdates.DownloadAsync(r, p, cts.Token);
                if (cts.IsCancellationRequested || progress.BoundDialog == null) return; // closed while finishing
                _pendingUpdate = prepared;
                progress.Navigate(ready);
            }
            catch (Exception e)
            {
                error = e;
                progress.BoundDialog?.Close();
            }
        };
        var picked = TaskDialog.ShowDialog(this, progress);
        if (_pendingUpdate == null) cts.Cancel(); // closed with Cancel or the X while downloading
        if (error is OperationCanceledException || (error == null && _pendingUpdate == null)) { Append("Update cancelled.", LogKind.Note); return; }
        if (error != null)
        {
            Append($"Could not update to {r.Version}: {error.Message}", LogKind.Error);
            MessageBox.Show(this, $"Could not update to {r.Version}:\n\n{error.Message}\n\nNothing was changed. You can download it from {r.PageUrl}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ShowUpdateReady();
        if (picked == restart) RestartToUpdate();
    }

    void ShowUpdateReady()
    {
        if (_pendingUpdate == null) return;
        _updateReady.Text = $"Modwright {_pendingUpdate.Version} is downloaded and is installed when you close the app. Restart now";
        _updateReady.LinkArea = new LinkArea(_updateReady.Text.Length - "Restart now".Length, "Restart now".Length);
        _updateReady.Visible = true;
        Append($"Modwright {_pendingUpdate.Version} is downloaded; it is installed when you close the app (or Options > About > Restart now).", LogKind.Note);
    }

    /// <summary>Installs the downloaded update and starts the new version, unless an upload is running.</summary>
    void RestartToUpdate()
    {
        if (_pendingUpdate == null) return;
        if (_busy) { MessageBox.Show(this, "An upload is running. The update is installed when you close the app, or restart once the upload is done.", Text); return; }
        if (InstallPendingUpdate(restart: true)) Close();
    }

    /// <summary>
    /// Hands <see cref="_pendingUpdate"/> to the program that installs it over this one's files once this
    /// window has closed (and starts it when <paramref name="restart"/> is true). Returns false (and says why) when it
    /// could not.
    /// </summary>
    bool InstallPendingUpdate(bool restart)
    {
        var u = _pendingUpdate;
        if (u == null) return false;
        try
        {
            u.Install(UpdateCheck.CurrentVersion, restart);
            _pendingUpdate = null;
            return true;
        }
        catch (Exception e)
        {
            _pendingUpdate = null;
            if (!IsDisposed && Visible)
            {
                _updateReady.Visible = false;
                Append($"Could not install {u.Version}: {e.Message} Nothing was changed.", LogKind.Error);
                MessageBox.Show(this, $"Could not install {u.Version}:\n\n{e.Message}\n\nNothing was changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
    }
}
