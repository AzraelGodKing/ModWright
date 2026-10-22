namespace SteamCmdHelper;

/// <summary>
/// Beside the mod list: the selected mod's preview image at a size you can see, its versions and Steam numbers, what
/// it still needs, the changelog entry and description it will upload with, and buttons for what is done with it most.
/// With nothing selected it sums up the whole list. What it says comes from <see cref="ModDetails"/>.
/// </summary>
public sealed class ModDetailsPanel : Panel
{
    readonly TableLayoutPanel _body = new() { Location = Point.Empty, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(12, 10, 12, 10) };
    readonly PictureBox _preview = new() { SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 10), AccessibleName = "Preview image" };
    readonly Label _name = new Label { AutoSize = true, Font = Theme.HeadingFont, UseMnemonic = false, Margin = new Padding(0, 0, 0, 2) }.Role(ThemeRole.Title);
    readonly Label _state = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 8) }.Role(ThemeRole.Keep);
    readonly TableLayoutPanel _facts = new() { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
    readonly FlowLayoutPanel _actions = new() { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 6) };
    readonly Label _needsHead = Heading("Needs"), _notesHead = Heading("Change note"), _descHead = Heading("Description");
    readonly Label _needs = Wrapping().Role(ThemeRole.Keep), _notes = Wrapping(), _desc = Wrapping().Role(ThemeRole.Muted);
    readonly List<(Button Button, Func<ModInfo, bool> Applies)> _buttons = new();
    readonly PreviewCache _images = new();
    ModInfo? _mod;

    public ModDetailsPanel()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        AccessibleName = "Mod details";
        _facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var c in new Control[] { _preview, _name, _state, _facts, _actions, _needsHead, _needs, _notesHead, _notes, _descHead, _desc })
        {
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _body.Controls.Add(c);
        }
        Controls.Add(_body);
        Resize += (_, _) => FitWidth();
    }

    static Label Heading(string text) => new Label
    {
        Text = text.ToUpperInvariant(), AccessibleName = text, AutoSize = true, Font = Theme.SectionFont, Margin = new Padding(0, 8, 0, 3),
    }.Role(ThemeRole.Title);

    static Label Wrapping() => new() { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 2) };

    /// <summary>A button under the mod's name; shown only for mods <paramref name="applies"/> says yes to.</summary>
    public Button AddAction(string text, string tip, ToolTip tips, Action run, Func<ModInfo, bool>? applies = null)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 0, 4, 0), Font = Theme.SmallFont, Margin = new Padding(0, 0, 6, 6) };
        b.Click += (_, _) => run();
        tips.SetToolTip(b, Localizer.T(tip));
        _buttons.Add((b, applies ?? (_ => true)));
        _actions.Controls.Add(b);
        return b;
    }

    /// <summary>Turns the buttons off while an upload runs.</summary>
    public void SetBusy(bool busy)
    {
        foreach (var (b, _) in _buttons) b.Enabled = !busy;
    }

    /// <summary>Shows one mod.</summary>
    public void ShowMod(ModInfo m, string stateLabel, Tone stateTone, string stateLine, List<(string Label, string Value)> facts, string changeNote, string description)
    {
        SuspendLayout();
        var other = !ReferenceEquals(_mod, m) && (_mod == null || _mod.Dir != m.Dir);
        _mod = m;
        ShowPreview(m.PreviewPath.Length > 0 ? m.PreviewPath : null);
        _name.Text = m.Name;
        _state.Text = stateLabel + (stateLine.Length > 0 ? " · " + stateLine : "");
        _state.ForeColor = Theme.Of(stateTone);
        FillFacts(facts);

        var needs = m.Problems.Select(p => "Fix: " + p).Concat(m.Warnings.Select(w => "Note: " + w)).ToList();
        if (m.PreviewOk == false && m.PreviewNote.Length > 0 && !needs.Any(n => n.Contains(m.PreviewNote))) needs.Add("Preview: " + m.PreviewNote);
        _needs.Text = string.Join("\n", needs);
        _needs.ForeColor = Theme.Of(m.Problems.Count > 0 ? Tone.Error : Tone.Warn);
        _needsHead.Visible = _needs.Visible = needs.Count > 0;

        _notesHead.Text = (m.Version.Length > 0 ? "Change note for " + m.Version : "Change note").ToUpperInvariant();
        _notes.Text = changeNote;
        _notesHead.Visible = _notes.Visible = changeNote.Length > 0;
        _desc.Text = description;
        _descHead.Visible = _desc.Visible = description.Length > 0;

        _actions.Visible = true;
        foreach (var (b, applies) in _buttons) b.Visible = applies(m);
        ResumeLayout(true);
        FitWidth();
        if (other) AutoScrollPosition = Point.Empty;
    }

    /// <summary>With no mod selected: the whole list in a few lines, and how to see one mod.</summary>
    public void ShowList(string folderName, string summary, List<(string Label, string Value)> facts)
    {
        SuspendLayout();
        _mod = null;
        ShowPreview(null);
        _name.Text = folderName.Length > 0 ? folderName : "Mods";
        _state.Text = summary;
        _state.ForeColor = Theme.Current.Muted;
        FillFacts(facts);
        _actions.Visible = false;
        _needsHead.Visible = _needs.Visible = _notesHead.Visible = _notes.Visible = false;
        _desc.Text = "Select a mod to see its preview, its numbers on Steam, the change note and description it will upload with, and buttons for it.";
        _descHead.Visible = false;
        _desc.Visible = true;
        ResumeLayout(true);
        FitWidth();
    }

    void FillFacts(List<(string Label, string Value)> facts)
    {
        _facts.SuspendLayout();
        while (_facts.Controls.Count > 0) _facts.Controls[0].Dispose();
        _facts.RowStyles.Clear();
        _facts.RowCount = facts.Count;
        var p = Theme.Current;
        for (var i = 0; i < facts.Count; i++)
        {
            _facts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var key = new Label { Text = Localizer.T(facts[i].Label), AutoSize = true, ForeColor = p.Muted, BackColor = BackColor, Margin = new Padding(0, 1, 14, 1), UseMnemonic = false }.Role(ThemeRole.Muted);
            var value = new Label { Text = facts[i].Value, AutoSize = true, ForeColor = p.Text, BackColor = BackColor, Margin = new Padding(0, 1, 0, 1), UseMnemonic = false };
            _facts.Controls.Add(key, 0, i);
            _facts.Controls.Add(value, 1, i);
        }
        _facts.ResumeLayout(true);
    }

    /// <summary>Wraps the text to the panel's width and sizes the preview to fit it.</summary>
    void FitWidth()
    {
        var w = ClientSize.Width - _body.Padding.Horizontal - (VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
        if (w < 40) return;
        foreach (var l in new[] { _name, _state, _needs, _notes, _desc }) l.MaximumSize = new Size(w, 0);
        var keyWidth = _facts.Controls.OfType<Label>().Where((_, i) => i % 2 == 0).Select(l => l.PreferredWidth + l.Margin.Horizontal).DefaultIfEmpty(0).Max();
        foreach (Control c in _facts.Controls)
            if (_facts.GetColumn(c) == 1) c.MaximumSize = new Size(Math.Max(60, w - keyWidth), 0);
        _actions.MaximumSize = new Size(w, 0);
        if (_preview.Image is { } img)
        {
            // No taller than a third of the panel, so the numbers under it show without scrolling.
            var most = Math.Min(LogicalToDeviceUnits((int)(220 * Theme.TextScale)), Math.Max(LogicalToDeviceUnits(72), ClientSize.Height / 3));
            var h = Math.Min(most, (int)(w * (double)img.Height / img.Width));
            _preview.Size = new Size(Math.Min(w, (int)Math.Round(h * (double)img.Width / img.Height)), Math.Max(1, h));
        }
    }

    string? _previewPath;

    /// <summary>Shows the preview at once when it was read before; otherwise reads it in the background and shows it then.</summary>
    void ShowPreview(string? path)
    {
        _previewPath = path;
        Image? img = null;
        if (path != null && !_images.TryGet(path, out img)) _ = LoadPreviewAsync(path);
        _preview.Image = img;
        _preview.Visible = img != null;
    }

    async Task LoadPreviewAsync(string path)
    {
        var img = await _images.LoadAsync(path, () => _preview.Image);
        if (IsDisposed || _previewPath != path) return; // another mod was picked meanwhile
        SuspendLayout();
        _preview.Image = img;
        _preview.Visible = img != null;
        ResumeLayout(true);
        FitWidth();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewPath = null;
            _preview.Image = null;
            _images.Dispose();
        }
        base.Dispose(disposing);
    }
}
