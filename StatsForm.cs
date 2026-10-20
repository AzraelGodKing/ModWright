namespace SteamCmdHelper;

/// <summary>
/// Workshop numbers over time for one mod or the whole mods folder, from the daily counts kept on this PC, plus new
/// comments (counted through the Steam client on request).
/// </summary>
public sealed class StatsForm : Form
{
    readonly IReadOnlyList<ModInfo> _mods;
    readonly Dictionary<string, List<SubscriberPoint>> _points;
    readonly Func<Task<Dictionary<string, long>?>> _countComments;
    readonly ComboBox _which = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Mod" };
    readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Count" };
    readonly Chart _chart = new() { Dock = DockStyle.Fill };
    readonly Label _numbers = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 8, 0, 0), UseMnemonic = false };
    readonly Label _comments = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 4, 0, 0), UseMnemonic = false }.Role(ThemeRole.Muted);
    readonly Button _count = new() { Text = "&Count comments", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    readonly Button _open = new() { Text = "&Open comments", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };

    /// <param name="countComments">Counts comments on every mod through the Steam client; null when it could not.</param>
    public StatsForm(IReadOnlyList<ModInfo> mods, ModInfo? selected, Dictionary<string, List<SubscriberPoint>> points, Func<Task<Dictionary<string, long>?>> countComments)
    {
        _mods = mods.Where(m => m.OnSteam).ToList();
        _points = points;
        _countComments = countComments;
        Text = "Workshop stats";
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(520, 420);

        _which.Items.Add($"All {_mods.Count} mods (total)");
        foreach (var m in _mods) _which.Items.Add(m.Name);
        var at = selected == null ? -1 : _mods.ToList().FindIndex(m => m.Dir == selected.Dir);
        _which.SelectedIndex = at >= 0 ? at + 1 : 0;
        foreach (var k in Enum.GetValues<StatKind>()) _kind.Items.Add(WorkshopStats.Name(k));
        _kind.SelectedIndex = 0;
        _which.SelectedIndexChanged += (_, _) => ShowData();
        _kind.SelectedIndexChanged += (_, _) => ShowData();
        _count.Click += async (_, _) => await CountAsync();
        _open.Click += (_, _) => OpenComments();

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        top.Controls.AddRange(new Control[] { _which, _kind });
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 2, 14, 2) }.Role(ThemeRole.Accent);
        buttons.Controls.AddRange(new Control[] { _count, _open, close });
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 4, 0, 0), UseMnemonic = false,
            Text = "Counts are read from Steam's public item details once a day while the app is open. Comments can only be counted by the Steam client: " +
                   "it has to be running, and Steam briefly shows you playing the game.",
        }.Role(ThemeRole.Muted);
        bottom.Controls.AddRange(new Control[] { _numbers, _comments, hint, buttons });
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 12) };
        root.Controls.Add(_chart);
        root.Controls.Add(bottom);
        root.Controls.Add(top);
        Controls.Add(root);
        AcceptButton = CancelButton = close;
        Theme.Apply(this);
        ShowData();
        ShowComments();
    }

    ModInfo? Current => _which.SelectedIndex > 0 ? _mods[_which.SelectedIndex - 1] : null;

    List<(DateOnly Day, long Value)> SeriesFor(StatKind k) => Current is { } m
        ? WorkshopStats.Series(_points.GetValueOrDefault(m.WorkshopId!) ?? new(), k)
        : WorkshopStats.Total(_mods.Select(x => (IReadOnlyList<SubscriberPoint>)(_points.GetValueOrDefault(x.WorkshopId!) ?? new())), k);

    void ShowData()
    {
        var kind = (StatKind)_kind.SelectedIndex;
        var series = SeriesFor(kind);
        _chart.Show(series, WorkshopStats.Name(kind));
        string Line(StatKind k)
        {
            var s = SeriesFor(k);
            if (s.Count == 0) return $"{WorkshopStats.Name(k)}: not counted yet";
            static string D(long? d) => d is { } x ? (x >= 0 ? "+" : "") + x.ToString("N0") : "?";
            return $"{WorkshopStats.Name(k)}: {s[^1].Value:N0} ({D(WorkshopStats.Change(s, 7))} in 7 days, {D(WorkshopStats.Change(s, 30))} in 30 days)";
        }
        _numbers.Text = string.Join("\n", Enum.GetValues<StatKind>().Select(Line));
        _open.Enabled = Current != null;
    }

    void ShowComments()
    {
        var all = WorkshopStats.LoadComments();
        if (Current is { } m)
            _comments.Text = all.TryGetValue(m.WorkshopId!, out var c)
                ? $"Comments: {c.Known:N0}" + (c.Known > c.Seen ? $", {c.Known - c.Seen:N0} new since you last opened them" : ", none new")
                : "Comments: not counted yet (Count comments).";
        else
        {
            var fresh = WorkshopStats.New(all).Where(kv => _mods.Any(x => x.WorkshopId == kv.Key)).ToList();
            _comments.Text = fresh.Count == 0 ? "No new comments known (Count comments to look)."
                : "New comments: " + string.Join(", ", fresh.Select(kv => $"{_mods.First(x => x.WorkshopId == kv.Key).Name} ({kv.Value})"));
        }
    }

    async Task CountAsync()
    {
        _count.Enabled = false;
        _comments.Text = "Counting comments through the Steam client...";
        try { await _countComments(); }
        finally { if (!IsDisposed) { _count.Enabled = true; ShowComments(); } }
    }

    void OpenComments()
    {
        if (Current is not { } m) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WorkshopStats.CommentsUrl(m.WorkshopId!)) { UseShellExecute = true }); }
        catch { return; }
        WorkshopStats.MarkSeen(m.WorkshopId!);
        ShowComments();
    }

    /// <summary>A plain line chart of one count by day.</summary>
    sealed class Chart : Control
    {
        List<(DateOnly Day, long Value)> _series = new();
        string _name = "";

        public Chart()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            AccessibleRole = AccessibleRole.Chart;
        }

        public void Show(List<(DateOnly Day, long Value)> series, string name)
        {
            _series = series;
            _name = name;
            AccessibleName = series.Count == 0 ? name + ": no data yet"
                : $"{name} from {series[0].Value:N0} on {series[0].Day:d MMM yyyy} to {series[^1].Value:N0} on {series[^1].Day:d MMM yyyy}";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var p = Theme.Current;
            g.Clear(p.Surface);
            using var text = new SolidBrush(p.Text);
            using var muted = new SolidBrush(p.Muted);
            using var font = new Font(Font.FontFamily, Font.Size * 0.9f);
            if (_series.Count < 2)
            {
                var msg = _series.Count == 0 ? $"No {_name.ToLowerInvariant()} counted yet. The app counts once a day while it is open."
                    : $"{_name}: {_series[0].Value:N0} on {_series[0].Day:d MMM}. The chart starts once there are two days.";
                TextRenderer.DrawText(g, msg, Font, ClientRectangle, p.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            var min = _series.Min(x => x.Value);
            var max = _series.Max(x => x.Value);
            if (max == min) { max++; min = Math.Max(0, min - 1); }
            var labelW = (int)g.MeasureString(max.ToString("N0"), font).Width + 8;
            var area = new Rectangle(labelW, 10, Math.Max(10, Width - labelW - 14), Math.Max(10, Height - 40));
            using var grid = new Pen(Theme.Blend(p.Surface, p.Muted, 0.25));
            for (var i = 0; i <= 4; i++)
            {
                var y = area.Bottom - area.Height * i / 4;
                g.DrawLine(grid, area.Left, y, area.Right, y);
                var v = min + (max - min) * i / 4;
                g.DrawString(v.ToString("N0"), font, muted, 2, y - font.Height / 2);
            }
            var first = _series[0].Day.DayNumber;
            var span = Math.Max(1, _series[^1].Day.DayNumber - first);
            var pts = _series.Select(x => new PointF(
                area.Left + area.Width * (float)(x.Day.DayNumber - first) / span,
                area.Bottom - area.Height * (float)(x.Value - min) / (max - min))).ToArray();
            using var line = new Pen(p.Accent, 2f);
            g.DrawLines(line, pts);
            g.DrawString(_series[0].Day.ToString("d MMM yyyy"), font, muted, area.Left, area.Bottom + 6);
            var end = _series[^1].Day.ToString("d MMM yyyy");
            g.DrawString(end, font, muted, area.Right - g.MeasureString(end, font).Width, area.Bottom + 6);
        }
    }
}
