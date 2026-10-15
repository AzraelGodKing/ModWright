using System.Runtime.InteropServices;

namespace SteamCmdHelper;

/// <summary>
/// Keeps one window open per Windows session, so two copies never run SteamCMD against the same login cache at once.
/// A second start brings the open window to the front and exits. The command line is not affected.
/// </summary>
static class SingleInstance
{
    const string MutexName = @"Local\SteamCmdHelper.Window";
    const string ActivateName = @"Local\SteamCmdHelper.Activate";
    static Mutex? _mutex;
    static EventWaitHandle? _activate;

    /// <summary>
    /// True when this process may open the window. <paramref name="wait"/> is how long to wait for another copy to close
    /// (used right after an update, while the old version is still exiting).
    /// </summary>
    public static bool Claim(TimeSpan wait)
    {
        _mutex = new Mutex(false, MutexName);
        try { return _mutex.WaitOne(wait); }
        catch (AbandonedMutexException) { return true; } // the other copy died without closing; it is ours now
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Asks the open window to come to the front.</summary>
    public static void ActivateOther()
    {
        try
        {
            AllowSetForegroundWindow(-1); // ASFW_ANY: this process was just started by the user, so it may pass focus on
            using var ev = EventWaitHandle.OpenExisting(ActivateName);
            ev.Set();
        }
        catch { /* the other copy is closing; nothing to show */ }
    }

    /// <summary>Brings <paramref name="form"/> to the front whenever another copy is started.</summary>
    public static void Listen(Form form)
    {
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
        {
            if (form.IsDisposed || !form.IsHandleCreated) return;
            try
            {
                form.BeginInvoke(() =>
                {
                    if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                    form.Activate();
                    form.BringToFront();
                });
            }
            catch (InvalidOperationException) { /* closing */ }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }
}
