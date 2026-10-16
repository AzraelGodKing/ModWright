namespace SteamCmdHelper;

/// <summary>
/// Edits a mod's Workshop page (title, description, visibility). Ticked fields are saved as a <see cref="WorkshopPageEdit"/>
/// and sent with the mod's next upload; the current page is read from Steam to start from.
/// </summary>
public sealed class WorkshopPageForm : Form
{
    readonly ModInfo _mod;
    readonly CheckBox _useTitle = new() { Text = "Title", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
    readonly TextBox _title = new() { Dock = DockStyle.Fill, MaxLength = WorkshopPage.MaxTitle };
    readonly CheckBox _useDescription = new() { Text = "Description", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 8, 0) };
    readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    readonly Label _count = new Label { AutoSize = true, Anchor = AnchorStyles.Right }.Role(ThemeRole.Muted);
    readonly CheckBox _useVisibility = new() { Text = "Visibility", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
    readonly ComboBox _visibility = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Anchor = AnchorStyles.Left };
    readonly CheckBox _usePreview = new() { Text = "Preview image", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
    readonly TextBox _preview = new() { ReadOnly = true, Width = 360, Anchor = AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Preview image file" };
    readonly Label _previewSize = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 0, 0) }.Role(ThemeRole.Muted);
    readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 8) }.Role(ThemeRole.Muted);
    readonly Label _tags = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 6, 0, 0) }.Role(ThemeRole.Muted);
    bool _filling;

    /// <summary>The changes to send (empty = none). Set when the form closes with OK.</summary>
    public WorkshopPageEdit Result { get; private set; } = new();

    public WorkshopPageForm(ModInfo mod, string descriptionFile, string? currentDescription)
    {
        _mod = mod;
        Text = "Workshop page - " + mod.Name;
        Width = 720; Height = 660; MinimumSize = new Size(580, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        _visibility.Items.AddRange(WorkshopPage.VisibilityNames);

        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 4),
            Text = mod.OnSteam
                ? $"Tick what to change on item {mod.WorkshopId}. The changes are sent with this mod's next upload, then cleared. Unticked fields stay as they are on Steam."
                : "This mod is not on Steam yet. Ticked fields replace what it is created with (its name, the description file and Private)."
        };
        var descPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        descPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        descPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        descPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        descPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        descPanel.Controls.Add(_description, 0, 0); descPanel.SetColumnSpan(_description, 2);
        var fromFile = new LinkLabel { Text = "Use " + descriptionFile, AutoSize = true, Anchor = AnchorStyles.Left, Enabled = currentDescription != null }.Role(ThemeRole.Keep);
        fromFile.LinkClicked += (_, _) => { if (currentDescription != null) _description.Text = Crlf(currentDescription); };
        descPanel.Controls.Add(fromFile, 0, 1);
        descPanel.Controls.Add(_count, 1, 1);

        // A new preview image: the mod's own preview file, or any image picked here.
        var previewRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
        var choose = new Button { Text = "Choose...", AutoSize = true, Padding = new Padding(6, 0, 6, 0) };
        choose.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "New preview image (PNG, JPG or GIF, up to 1 MB)", Filter = "Images|*.png;*.jpg;*.jpeg;*.gif" };
            if (mod.PreviewPath.Length > 0) dlg.InitialDirectory = Path.GetDirectoryName(mod.PreviewPath);
            if (dlg.ShowDialog(this) == DialogResult.OK) SetPreview(dlg.FileName);
        };
        var useOwn = new LinkLabel { Text = "Use the mod's preview", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 0, 0), Enabled = mod.PreviewPath.Length > 0 }.Role(ThemeRole.Keep);
        useOwn.LinkClicked += (_, _) => SetPreview(mod.PreviewPath);
        previewRow.Controls.AddRange(new Control[] { _preview, choose, useOwn, _previewSize });

        // Rows: intro, status, title, visibility, preview, description (fills), tags, buttons.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var size in new[] { 0, 0, 0, 0, 0, 100, 0, 0 })
            root.RowStyles.Add(size == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, size));
        root.Controls.Add(intro, 0, 0); root.SetColumnSpan(intro, 2);
        root.Controls.Add(_status, 0, 1); root.SetColumnSpan(_status, 2);
        root.Controls.Add(_useTitle, 0, 2); root.Controls.Add(_title, 1, 2);
        root.Controls.Add(_useVisibility, 0, 3); root.Controls.Add(_visibility, 1, 3);
        root.Controls.Add(_usePreview, 0, 4); root.Controls.Add(previewRow, 1, 4);
        root.Controls.Add(_useDescription, 0, 5); root.Controls.Add(descPanel, 1, 5);
        root.Controls.Add(_tags, 1, 6);

        var save = new Button { Text = "Save for next upload", AutoSize = true, Padding = new Padding(8, 2, 8, 2) }.Role(ThemeRole.Accent);
        var clear = new Button { Text = "Clear changes", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        buttons.Controls.Add(clear);
        root.Controls.Add(buttons, 0, 7); root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
        CancelButton = cancel;

        save.Click += (_, _) => Save();
        clear.Click += (_, _) => { Result = new WorkshopPageEdit(); DialogResult = DialogResult.OK; };

        // Typing in a field ticks it; unticking greys it out so it is clear it will not be sent.
        _title.TextChanged += (_, _) => Touch(_useTitle);
        _description.TextChanged += (_, _) => { Touch(_useDescription); ShowCount(); };
        _visibility.SelectedIndexChanged += (_, _) => Touch(_useVisibility);
        foreach (var c in new[] { _useTitle, _useDescription, _useVisibility, _usePreview }) c.CheckedChanged += (_, _) => ShowEnabled();

        // Start from what is waiting to be sent, else from what the item would be created with.
        var pending = WorkshopPage.Get(mod.Dir);
        Fill(pending?.Title ?? mod.Name, pending?.Description ?? currentDescription ?? "", pending?.Visibility ?? WorkshopPage.Private);
        _useTitle.Checked = pending?.Title != null;
        _useDescription.Checked = pending?.Description != null;
        _useVisibility.Checked = pending?.Visibility != null;
        if (pending?.Preview != null) SetPreview(pending.Preview);
        else _preview.PlaceholderText = mod.OnSteam ? "unchanged (the image on Steam stays)" : "the layout's preview file";
        ShowEnabled();
        ShowCount();
        _tags.Text = "";
        _status.Text = mod.OnSteam ? "Reading the current page from Steam..." : "";

        Theme.Apply(this);
        if (mod.OnSteam) Shown += async (_, _) => await LoadFromSteamAsync();
    }

    async Task LoadFromSteamAsync()
    {
        var d = await WorkshopPage.FetchAsync(_mod.WorkshopId!);
        if (IsDisposed) return;
        if (d == null)
        {
            _status.Text = "Could not read the page from Steam (offline, or the item is private). The fields start from your files.";
            return;
        }
        _status.Text = $"On Steam now: \"{d.Title}\", {(d.Visibility is { } v ? WorkshopPage.VisibilityName(v) : "visibility unknown")}.";
        _tags.Text = d.Tags.Length > 0
            ? "Tags on Steam: " + string.Join(", ", d.Tags) + ". SteamCMD cannot change tags; the game's own uploader sets them."
            : "";
        // Fields that are not ticked (no pending change, not edited while loading) show what Steam has now.
        _filling = true;
        if (!_useTitle.Checked) _title.Text = d.Title;
        if (!_useDescription.Checked) _description.Text = Crlf(d.Description);
        if (!_useVisibility.Checked && d.Visibility is { } vis) _visibility.SelectedIndex = Math.Clamp(vis, 0, WorkshopPage.VisibilityNames.Length - 1);
        _filling = false;
    }

    static string Crlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");

    void Fill(string title, string description, int visibility)
    {
        _filling = true;
        _title.Text = title;
        _description.Text = Crlf(description);
        _visibility.SelectedIndex = Math.Clamp(visibility, 0, WorkshopPage.VisibilityNames.Length - 1);
        _filling = false;
    }

    void Touch(CheckBox c) { if (!_filling) c.Checked = true; }

    void SetPreview(string path)
    {
        _preview.Text = path;
        _usePreview.Checked = true;
        var problem = WorkshopPage.CheckPreview(path);
        _previewSize.Text = problem ?? (File.Exists(path) ? $"{new FileInfo(path).Length / 1024} KB" : "");
        _previewSize.ForeColor = Theme.Of(problem != null ? Tone.Error : Tone.Muted);
        _previewSize.MaximumSize = new Size(260, 0);
    }

    void ShowEnabled()
    {
        _title.Enabled = _useTitle.Checked;
        _description.ReadOnly = !_useDescription.Checked; // stays scrollable for reading
        _visibility.Enabled = _useVisibility.Checked;
        _preview.Enabled = _usePreview.Checked;
    }

    void ShowCount()
    {
        var n = _description.Text.Replace("\r\n", "\n").Length;
        _count.Text = $"{n} / {WorkshopPage.MaxDescription}";
        _count.ForeColor = Theme.Of(n > WorkshopPage.MaxDescription ? Tone.Error : Tone.Muted);
    }

    void Save()
    {
        var edit = new WorkshopPageEdit
        {
            Title = _useTitle.Checked ? _title.Text.Trim() : null,
            Description = _useDescription.Checked ? _description.Text.Replace("\r\n", "\n") : null,
            Visibility = _useVisibility.Checked ? _visibility.SelectedIndex : null,
            Preview = _usePreview.Checked && _preview.Text.Length > 0 ? _preview.Text : null,
        };
        if (WorkshopPage.Validate(edit) is { } error) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (edit.Visibility is 0 && !_mod.OnSteam
            && MessageBox.Show(this, "The item will be created PUBLIC: subscribers can see it as soon as the upload finishes. Continue?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Result = edit;
        DialogResult = DialogResult.OK;
    }
}
