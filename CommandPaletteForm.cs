namespace SteamCmdHelper;

/// <summary>
/// Ctrl+K: a search box over every action in the window (buttons, the selected mod's menu, Options sections and
/// their settings). Type a few letters, then Enter runs the top match; Up and Down pick another.
/// </summary>
public sealed class CommandPaletteForm : Form
{
    readonly List<PaletteEntry> _all;
    readonly TextBox _box = new() { Dock = DockStyle.Top, Font = Theme.BodyFont, AccessibleName = "Search actions", PlaceholderText = "Type what you want to do" };
    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, AccessibleName = "Actions", BorderStyle = BorderStyle.None };
    List<PaletteEntry> _shown = new();

    /// <summary>The action picked; run it after the palette has closed.</summary>
    public PaletteEntry? Picked { get; private set; }

    public CommandPaletteForm(IEnumerable<PaletteEntry> entries)
    {
        _all = entries.ToList();
        Text = "Find an action";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        KeyPreview = true;
        Size = new Size(LogicalToDeviceUnits(560), LogicalToDeviceUnits(380));
        _list.ItemHeight = LogicalToDeviceUnits(26);
        var pad = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(8) };
        Controls.Add(_list);
        Controls.Add(pad);
        Controls.Add(_box);
        Padding = new Padding(LogicalToDeviceUnits(8));

        _box.TextChanged += (_, _) => Fill();
        _list.DoubleClick += (_, _) => Pick();
        _list.DrawItem += DrawEntry;
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Down: Move(1); e.Handled = true; break;
                case Keys.Up: Move(-1); e.Handled = true; break;
                case Keys.PageDown: Move(8); e.Handled = true; break;
                case Keys.PageUp: Move(-8); e.Handled = true; break;
                case Keys.Enter: Pick(); e.Handled = e.SuppressKeyPress = true; break;
                case Keys.Escape: DialogResult = DialogResult.Cancel; Close(); e.Handled = true; break;
            }
        };
        Theme.Apply(this);
        Fill();
    }

    void Fill()
    {
        _shown = PaletteSearch.Rank(_box.Text, _all);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _shown) _list.Items.Add(e.Name + (e.Where.Length > 0 ? "  ·  " + e.Where : "") + (e.Shortcut.Length > 0 ? "  (" + e.Shortcut + ")" : ""));
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();
    }

    void Move(int by)
    {
        if (_list.Items.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + by, 0, _list.Items.Count - 1);
    }

    void Pick()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _shown.Count) return;
        Picked = _shown[_list.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }

    void DrawEntry(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _shown.Count) return;
        var entry = _shown[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var c = Theme.Current;
        using (var back = new SolidBrush(selected ? c.Accent : _list.BackColor)) e.Graphics.FillRectangle(back, e.Bounds);
        var fore = selected ? c.AccentText : _list.ForeColor;
        var muted = selected ? c.AccentText : c.Muted;
        var r = Rectangle.Inflate(e.Bounds, -LogicalToDeviceUnits(8), 0);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(e.Graphics, entry.Name, _list.Font, r, fore, flags);
        var nameWidth = TextRenderer.MeasureText(e.Graphics, entry.Name, _list.Font, r.Size, flags).Width;
        var rest = new Rectangle(r.X + nameWidth + LogicalToDeviceUnits(10), r.Y, Math.Max(0, r.Width - nameWidth - LogicalToDeviceUnits(10)), r.Height);
        TextRenderer.DrawText(e.Graphics, entry.Where, Theme.SmallFont, rest, muted, flags);
        if (entry.Shortcut.Length > 0) TextRenderer.DrawText(e.Graphics, entry.Shortcut, Theme.SmallFont, r, muted, flags | TextFormatFlags.Right);
    }
}
