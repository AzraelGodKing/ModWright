namespace SteamCmdHelper;

/// <summary>
/// Runs an action once input has paused for a moment, so typing in a filter box rebuilds a long list once instead of
/// on every key. Used from the UI thread only.
/// </summary>
public sealed class Debouncer : IDisposable
{
    readonly System.Windows.Forms.Timer _timer;
    readonly Action _run;

    public Debouncer(int milliseconds, Action run)
    {
        _run = run;
        _timer = new System.Windows.Forms.Timer { Interval = milliseconds };
        _timer.Tick += (_, _) => { _timer.Stop(); _run(); };
    }

    /// <summary>Starts (or restarts) the wait.</summary>
    public void Poke()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Runs the action now if it is waiting, for a moment that can't wait (Enter in the box).</summary>
    public void Flush()
    {
        if (!_timer.Enabled) return;
        _timer.Stop();
        _run();
    }

    /// <summary>Drops a waiting run, when the caller does the work itself straight away.</summary>
    public void Cancel() => _timer.Stop();

    public void Dispose() => _timer.Dispose();
}
