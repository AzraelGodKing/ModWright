namespace SteamCmdHelper;

/// <summary>
/// A Windows notification when an upload or dry run ends while the window is in the background, so a long batch can
/// run behind other work. Clicking it brings the window back. Off under Options > Uploading.
/// </summary>
public static class Notify
{
    static NotifyIcon? _icon;

    /// <summary>Shows <paramref name="text"/> as a notification from <paramref name="owner"/>, unless it is the active window.</summary>
    public static void IfInBackground(Form owner, string title, string text, bool ok)
    {
        if (!owner.IsHandleCreated || Form.ActiveForm == owner) return;
        try
        {
            _icon ??= new NotifyIcon();
            _icon.Icon = owner.Icon ?? SystemIcons.Application;
            _icon.Text = "SteamCMD Helper";
            _icon.Visible = true;
            void Back(object? sender, EventArgs e)
            {
                if (owner.IsDisposed) return;
                if (owner.WindowState == FormWindowState.Minimized) owner.WindowState = FormWindowState.Normal;
                owner.Activate();
                Hide();
            }
            _icon.BalloonTipClicked -= Back;
            _icon.BalloonTipClicked += Back;
            _icon.Click -= Back;
            _icon.Click += Back;
            _icon.BalloonTipClosed += (_, _) => Hide();
            _icon.ShowBalloonTip(10000, title, text.Length > 240 ? text[..237] + "..." : text, ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
        catch { /* a notification is a nicety; never let it break the upload's end */ }
    }

    /// <summary>Takes the icon out of the notification area (also when the app closes).</summary>
    public static void Hide()
    {
        if (_icon == null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
