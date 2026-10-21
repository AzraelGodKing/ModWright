using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// Drop Pod > Read the log (and Test): the game's log with its errors and warnings grouped, the mods most likely at
/// fault, and a way to untick them. While watching, it reads the log again whenever the game writes to it.
/// </summary>
public sealed class LogForm : Form
{
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, AccessibleName = "Errors and warnings in the log" };
    readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f), AccessibleName = "The selected message in full" };
    readonly Label _summary = new() { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) };
    readonly CheckBox _errorsOnly = new() { Text = "&Errors only", AutoSize = true };
    readonly Button _untick = new() { Text = "&Untick the mod at fault", AutoSize = true, Enabled = false };
    readonly Button _reportBtn = new() { Text = "Report to the &author", AutoSize = true, Enabled = false };
    readonly Func<LogIssue, (string? Url, string Body)?>? _reportOf;
    readonly System.Windows.Forms.Timer _watch = new() { Interval = 3000 };
    readonly string _path;
    readonly Func<string, CancellationToken, LogReport> _read;
    readonly Func<string, string> _name;
    readonly Action<string> _untickMod;
    LogReport? _report;
    (long Length, DateTime Written) _seen;
    bool _reading;

    /// <param name="read">Reads and analyses the log (runs in the background).</param>
    /// <param name="name">A mod key's display name.</param>
    /// <param name="untick">Unticks a mod in the Drop Pod's list.</param>
    /// <param name="watch">Keep reading the log as the game writes it.</param>
    /// <param name="report">For a problem: where to report it to the author of the mod at fault, and the text; null hides the button.</param>
    public LogForm(string path, Func<string, CancellationToken, LogReport> read, Func<string, string> name, Action<string> untick, bool watch,
        Func<LogIssue, (string? Url, string Body)?>? report = null)
    {
        _reportOf = report;
        _path = path;
        _read = read;
        _name = name;
        _untickMod = untick;
        Text = "Game log: " + Path.GetFileName(path);
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Size = new Size((int)(980 * Theme.TextScale), (int)(640 * Theme.TextScale));
        MinimumSize = new Size(600, 400);

        foreach (var (col, width) in new[] { ("Level", 70), ("Times", 55), ("Mod most likely at fault", 220), ("Message", 600) })
            _list.Columns.Add(Localizer.T(col), (int)(width * Theme.TextScale));
        var again = new Button { Text = "&Read again", AutoSize = true };
        var open = new Button { Text = "&Open the log", AutoSize = true };
        var copy = new Button { Text = "&Copy a report", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        top.Controls.Add(_summary);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        foreach (var c in new Control[] { _errorsOnly, again, open, copy, _untick, _reportBtn, close }) buttons.Controls.Add(c);
        _reportBtn.Visible = report != null;
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_details);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        CancelButton = close;
        close.Click += (_, _) => Close();

        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _errorsOnly.CheckedChanged += (_, _) => Fill();
        again.Click += async (_, _) => await ReadAsync(force: true);
        open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_path) { UseShellExecute = true }); } catch { } };
        copy.Click += (_, _) => { if (_report != null) try { Clipboard.SetText(ReportText(_report)); } catch { } };
        _untick.Click += (_, _) =>
        {
            if (Selected()?.Mods.FirstOrDefault() is not { } b) return;
            _untickMod(b.Mod);
            _untick.Enabled = false;
            _summary.Text = $"Unticked {_name(b.Mod)}. Press Save to game in the Drop Pod, then test again.";
        };
        _reportBtn.Click += (_, _) =>
        {
            if (Selected() is not { } i || _reportOf?.Invoke(i) is not { } r) return;
            try { Clipboard.SetText(r.Body); } catch { }
            if (r.Url != null)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(r.Url) { UseShellExecute = true }); } catch { }
            _summary.Text = r.Url == null ? "The report is on the clipboard; the mod names no page to report on." :
                r.Url.Contains("github.com") ? "Opened a new GitHub issue with the report filled in (it's on the clipboard too)." : "Opened the mod's page; the report is on the clipboard to paste in a comment.";
        };
        _watch.Tick += async (_, _) => await ReadAsync(force: false);
        Shown += async (_, _) => { await ReadAsync(force: true); if (watch) _watch.Start(); };
        FormClosed += (_, _) => _watch.Dispose();
        Theme.Apply(this);
    }

    async Task ReadAsync(bool force)
    {
        if (_reading) return;
        (long, DateTime) now;
        try { var fi = new FileInfo(_path); now = fi.Exists ? (fi.Length, fi.LastWriteTimeUtc) : (-1, default); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { now = (-1, default); }
        if (!force && now == _seen) return;
        _seen = now;
        if (now.Item1 < 0) { _summary.Text = _watch.Enabled ? "Waiting for the game to start its log..." : "There is no log yet: " + _path; return; }
        _reading = true;
        try
        {
            _report = await Task.Run(() => _read(_path, CancellationToken.None));
            Fill();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _summary.Text = "Could not read the log: " + e.Message; }
        finally { _reading = false; }
    }

    void Fill()
    {
        if (_report is not { } r) return;
        var selected = Selected()?.Line;
        _summary.Text = $"{r.Errors} error{(r.Errors == 1 ? "" : "s")} and {r.Warnings} warning{(r.Warnings == 1 ? "" : "s")} in {r.Lines:N0} lines" +
            (r.GameVersion != null ? $"  ·  RimWorld {r.GameVersion}" : "") +
            (r.ErrorsByMod.Count > 0 ? "  ·  most errors: " + string.Join(", ", r.ErrorsByMod.OrderByDescending(p => p.Value).Take(3).Select(p => $"{_name(p.Key)} ({p.Value})")) : "") +
            (r.Truncated ? "  ·  only the first 2,000 kinds are listed" : "") + (_watch.Enabled ? "  ·  watching" : "");
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var i in r.Issues.Where(i => !_errorsOnly.Checked || i.Level == LogLevel.Error))
        {
            var mod = i.Mods.FirstOrDefault() is { } b ? _name(b.Mod) + (i.Mods.Count > 1 ? $" (+{i.Mods.Count - 1})" : "") : "";
            var it = new ListViewItem(new[] { i.Level == LogLevel.Error ? "Error" : "Warning", i.Count.ToString(), mod, i.Title })
            {
                Tag = i,
                ForeColor = Theme.Of(i.Level == LogLevel.Error ? Tone.Error : Tone.Warn),
            };
            _list.Items.Add(it);
            if (i.Line == selected) it.Selected = true;
        }
        if (_list.Items.Count == 0) _list.Items.Add(new ListViewItem(new[] { "", "", "", "No errors or warnings." }));
        _list.EndUpdate();
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0 && _list.Items[0].Tag != null) _list.Items[0].Selected = true;
    }

    LogIssue? Selected() => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as LogIssue : null;

    static readonly Dictionary<Blame, string> Why = new()
    {
        [Blame.Code] = "its code is in the stack trace",
        [Blame.Patch] = "it patches a method in the stack trace",
        [Blame.File] = "the message names a file in its folder",
        [Blame.Named] = "the message names it",
    };

    void ShowSelected()
    {
        var i = Selected();
        _untick.Enabled = i?.Mods.Count > 0;
        _reportBtn.Enabled = i?.Mods.Count > 0;
        if (i == null) { _details.Text = ""; return; }
        var head = i.Mods.Count == 0 ? "No mod could be pinned on this one." : string.Join(Environment.NewLine, i.Mods.Select(b => $"{_name(b.Mod)}: {Why[b.Why]}"));
        _details.Text = $"{head}{Environment.NewLine}Line {i.Line}, {i.Count} time{(i.Count == 1 ? "" : "s")}.{Environment.NewLine}{Environment.NewLine}{i.Text.Replace("\n", Environment.NewLine)}";
    }

    string ReportText(LogReport r)
    {
        var lines = new List<string> { $"RimWorld {r.GameVersion ?? "(version unknown)"}: {r.Errors} errors, {r.Warnings} warnings" };
        foreach (var i in r.Issues.Take(50))
            lines.Add($"- {(i.Level == LogLevel.Error ? "Error" : "Warning")} x{i.Count}: {i.Title}" + (i.Mods.Count > 0 ? $" [{string.Join(", ", i.Mods.Select(b => _name(b.Mod)))}]" : ""));
        return string.Join(Environment.NewLine, lines);
    }
}
