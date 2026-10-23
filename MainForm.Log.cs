using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>The log box under the Upload tab.</summary>
public sealed partial class MainForm
{
    static Color LogColor(LogKind k) => Theme.HighContrast ? Theme.LogText : k switch
    {
        LogKind.Note => Color.DeepSkyBlue,
        LogKind.Ok => Color.LightGreen,
        LogKind.Warn => Color.Orange,
        LogKind.Error => Color.Tomato,
        _ => Theme.LogText,
    };

    static Color LogTime => Theme.HighContrast ? SystemColors.GrayText : Color.FromArgb(118, 122, 134);

    LogFilter CurrentLogFilter => (LogFilter)Math.Max(0, _logFilter.SelectedIndex);

    /// <summary>Lines added since the log box was last drawn; they are drawn together (see <see cref="FlushLog"/>).</summary>
    readonly List<(DateTime Time, string Text, LogKind Kind)> _pendingLog = new();
    readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 100 };
    /// <summary>Lines in the log box; it is trimmed back to <see cref="MaxLogLines"/> once it holds this many more.</summary>
    int _logBoxLines;
    const int LogTrimSlack = 2000;

    /// <summary>
    /// Adds a line with the time in front, coloured by what kind of line it is. The line is kept at once, but the log box
    /// draws new lines about ten times a second, so SteamCMD printing hundreds of lines doesn't slow the window down.
    /// </summary>
    void Append(string line, LogKind kind = LogKind.Plain)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(line, kind)); return; }
        var entry = (DateTime.Now, line, kind);
        _logLines.Add(entry);
        if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        if (!Display.Shows(CurrentLogFilter, kind)) return;
        _pendingLog.Add(entry);
        // --shot and a window not shown yet draw straight away; otherwise the timer gathers lines.
        if (NoStartupPrompts || !_log.IsHandleCreated) FlushLog();
        else _logTimer.Enabled = true;
    }

    /// <summary>Draws the lines <see cref="Append"/> gathered, then trims the log box if it grew too long.</summary>
    void FlushLog()
    {
        _logTimer.Enabled = false;
        if (_pendingLog.Count == 0) return;
        if (!_log.IsHandleCreated)
        {
            foreach (var e in _pendingLog) Write(e);
            _logBoxLines += _pendingLog.Count;
            _pendingLog.Clear();
            return;
        }
        var follow = _autoScroll.Checked;
        // With auto-scroll off, the new lines go at the end but the view (and any selection) stays where the reader has it.
        var first = SendMessage(_log.Handle, EmGetFirstVisibleLine, 0, 0);
        var (start, length) = (_log.SelectionStart, _log.SelectionLength);
        SendMessage(_log.Handle, WmSetRedraw, 0, 0);
        try
        {
            foreach (var e in _pendingLog) Write(e);
            _logBoxLines += _pendingLog.Count;
            _pendingLog.Clear();
            if (_logBoxLines > MaxLogLines + LogTrimSlack)
            {
                var cut = _log.GetFirstCharIndexFromLine(_logBoxLines - MaxLogLines);
                if (cut > 0)
                {
                    _log.Select(0, cut);
                    _log.SelectedText = ""; // works on a read-only box, like AppendText
                    first = Math.Max(0, first - (_logBoxLines - MaxLogLines));
                    start = Math.Max(0, start - cut);
                    _logBoxLines = MaxLogLines;
                }
            }
            if (follow)
            {
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
            }
            else
            {
                _log.Select(start, length);
                SendMessage(_log.Handle, EmLineScroll, 0, first - SendMessage(_log.Handle, EmGetFirstVisibleLine, 0, 0));
            }
        }
        finally
        {
            SendMessage(_log.Handle, WmSetRedraw, 1, 0);
            _log.Invalidate();
        }
        if (follow) _log.ScrollToCaret();
    }

    void Write((DateTime Time, string Text, LogKind Kind) e)
    {
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = LogTime;
        _log.AppendText(e.Time.ToString("HH:mm:ss") + "  ");
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = LogColor(e.Kind);
        _log.AppendText(e.Text + "\n");
        _log.SelectionColor = _log.ForeColor;
    }

    /// <summary>Shows the lines the log filter lets through (after the filter or the colours changed).</summary>
    void RenderLog()
    {
        if (!_log.IsHandleCreated) return;
        SendMessage(_log.Handle, WmSetRedraw, 0, 0);
        try
        {
            _log.Clear();
            _pendingLog.Clear();
            _logBoxLines = 0;
            foreach (var e in _logLines)
                if (Display.Shows(CurrentLogFilter, e.Kind)) { Write(e); _logBoxLines++; }
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        finally
        {
            SendMessage(_log.Handle, WmSetRedraw, 1, 0);
            _log.Invalidate();
        }
    }

    const int WmSetRedraw = 0x000B, EmGetFirstVisibleLine = 0x00CE, EmLineScroll = 0x00B6;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hwnd, int msg, int wParam, int lParam);

    /// <summary>The whole log as text, whatever the filter shows.</summary>
    string LogText() => string.Join(Environment.NewLine, _logLines.Select(e => e.Time.ToString("HH:mm:ss") + "  " + e.Text));

    void CopyLog()
    {
        if (_logLines.Count == 0) return;
        try { Clipboard.SetText(LogText()); }
        catch (Exception e) { Append("Could not copy the log: " + e.Message, LogKind.Error); }
    }

    void SaveLog()
    {
        using var d = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", FileName = $"SteamCmdHelper-log-{DateTime.Now:yyyy-MM-dd-HHmm}.txt" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(d.FileName, LogText() + Environment.NewLine); Append("Log saved to " + d.FileName, LogKind.Note); }
        catch (Exception e) { Append("Could not save the log: " + e.Message, LogKind.Error); }
    }

    /// <summary>Saves the log lines from <paramref name="from"/> on as this upload run's own log.</summary>
    void SaveRunLog(int from, DateTime started, string label)
    {
        var lines = _logLines.Skip(Math.Min(from, _logLines.Count))
            .Select(e => e.Time.ToString("HH:mm:ss") + "  " + e.Text);
        RunLogs.Save(RunLogs.DefaultDir, label, started, string.Join(Environment.NewLine, lines));
    }

    void ShowPastLogs()
    {
        using var f = new PastLogsForm(RunLogs.DefaultDir);
        f.ShowDialog(this);
    }

    void OpenSteamCmdLogs()
    {
        var logs = Path.Combine(Path.GetDirectoryName(_s.EffectiveSteamCmd)!, "logs");
        if (!Directory.Exists(logs)) { Append("SteamCMD has no logs folder yet (" + logs + "); it appears after SteamCMD's first run.", LogKind.Warn); return; }
        OpenPath(logs);
    }
}
