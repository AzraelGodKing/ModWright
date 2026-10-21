namespace SteamCmdHelper;

/// <summary>
/// A plain window around a list: a line of text over it, optional tick boxes, an optional text pane showing the
/// selected row in full, and buttons that act on the list. The Drop Pod's smaller windows (history, cleanup, update
/// notes, load times, comparing lists) are all this with different rows.
/// </summary>
public sealed class ListWindow : Form
{
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true };
    readonly Label _intro = new() { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 8) };
    readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AccessibleName = "The selected row in full" };
    readonly FlowLayoutPanel _buttons = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
    readonly List<(Button Button, bool NeedsRow)> _rowButtons = new();
    readonly Func<object?, string>? _detailsOf;

    /// <param name="detailsOf">Text for the pane under the list when a row is picked; null for no pane.</param>
    public ListWindow(string title, string intro, (string Name, int Width)[] columns, bool checkBoxes = false, Func<object?, string>? detailsOf = null, Size? size = null)
    {
        Text = title;
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        var sz = size ?? new Size(860, 520);
        Size = new Size((int)(sz.Width * Theme.TextScale), (int)(sz.Height * Theme.TextScale));
        MinimumSize = new Size(520, 320);
        _detailsOf = detailsOf;
        _list.CheckBoxes = checkBoxes;
        _list.AccessibleName = title;
        _intro.Text = intro;
        _intro.MaximumSize = new Size(Size.Width - 40, 0);
        foreach (var (name, width) in columns) _list.Columns.Add(Localizer.T(name), (int)(width * Theme.TextScale));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_intro, 0, 0);
        if (detailsOf != null)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(_details);
            root.Controls.Add(split, 0, 1);
            Shown += (_, _) => { try { split.SplitterDistance = split.Height * 3 / 5; } catch (InvalidOperationException) { } };
        }
        else root.Controls.Add(_list, 0, 1);
        root.Controls.Add(_buttons, 0, 2);
        Controls.Add(root);
        Resize += (_, _) => _intro.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 40), 0);

        _list.SelectedIndexChanged += (_, _) => RowChanged();
        _list.DoubleClick += (_, _) => DoubleClicked?.Invoke(Selected);
        Theme.Apply(this);
    }

    /// <summary>Runs when a row is double-clicked, with its tag.</summary>
    public Action<object?>? DoubleClicked { get; set; }

    public string Intro { get => _intro.Text; set => _intro.Text = value; }

    public ListView List => _list;

    public ListViewItem AddRow(object? tag, Tone tone, params string[] cells)
    {
        var it = new ListViewItem(cells) { Tag = tag, ForeColor = Theme.Of(tone), BackColor = _list.BackColor, ToolTipText = string.Join("  ·  ", cells.Where(c => c.Length > 0)) };
        _list.Items.Add(it);
        return it;
    }

    public void Clear() => _list.Items.Clear();

    /// <summary>A button under the list. With <paramref name="needsRow"/> it is only on while a row is picked (or ticked).</summary>
    public Button AddButton(string text, Action click, bool needsRow = false, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        if (primary) b.Role(ThemeRole.Primary);
        b.Click += (_, _) => click();
        _buttons.Controls.Add(b);
        _rowButtons.Add((b, needsRow));
        Theme.Apply(b);
        RowChanged();
        return b;
    }

    /// <summary>Adds the Close button (Esc).</summary>
    public void AddClose(string text = "Close")
    {
        var close = new Button { Text = text, AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(8, 2, 8, 2) };
        _buttons.Controls.Add(close);
        CancelButton = close;
        close.Click += (_, _) => Close();
        Theme.Apply(close);
    }

    public object? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag : null;

    public List<object> Ticked => _list.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag != null).Select(i => i.Tag!).ToList();

    public void RowChanged()
    {
        var any = Selected != null || (_list.CheckBoxes && _list.CheckedItems.Count > 0);
        foreach (var (b, needs) in _rowButtons) if (needs) b.Enabled = any;
        if (_detailsOf != null) _details.Text = Selected == null ? "" : _detailsOf(Selected).Replace("\r", "").Replace("\n", Environment.NewLine);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_list.CheckBoxes) _list.ItemChecked += (_, _) => RowChanged();
        if (_list.Items.Count > 0 && _list.SelectedItems.Count == 0) _list.Items[0].Selected = true;
    }
}
