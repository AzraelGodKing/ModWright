namespace SteamCmdHelper;

/// <summary>What to change on every ticked mod's Workshop page at once.</summary>
public sealed record BulkPageEdit(int? Visibility, string Find, string Replace, string Append)
{
    public bool ChangesDescriptions => Find.Length > 0 || Append.Trim().Length > 0;
}

/// <summary>
/// "Workshop pages of ticked mods...": one visibility, a text replacement or an addition for many items, kept as each
/// mod's Workshop page changes until its next upload, exactly like an edit made one mod at a time.
/// </summary>
public sealed class BulkPageForm : Form
{
    readonly CheckBox _useVisibility = new() { Text = "Set &visibility to", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
    readonly ComboBox _visibility = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Anchor = AnchorStyles.Left, Enabled = false, AccessibleName = "Visibility" };
    readonly TextBox _find = new() { Dock = DockStyle.Fill, AccessibleName = "Text to find in the descriptions" };
    readonly TextBox _replace = new() { Dock = DockStyle.Fill, AccessibleName = "Text to put in its place" };
    readonly TextBox _append = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 90, Font = new Font("Consolas", 9f), AccessibleName = "Text to add at the end of each description" };

    public BulkPageEdit Result { get; private set; } = new(null, "", "", "");

    public BulkPageForm(IReadOnlyList<ModInfo> mods)
    {
        Text = $"Workshop pages of {mods.Count} ticked mods";
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 400);
        MinimumSize = new Size(480, 380);
        _visibility.Items.AddRange(WorkshopPage.VisibilityNames);
        _visibility.SelectedIndex = WorkshopPage.Private;
        _useVisibility.CheckedChanged += (_, _) => _visibility.Enabled = _useVisibility.Checked;

        var names = string.Join(", ", mods.Take(6).Select(m => m.Name)) + (mods.Count > 6 ? $" and {mods.Count - 6} more" : "");
        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 0, 0, 8), UseMnemonic = false,
            Text = $"For {names}. Each description starts from the change already waiting, else from Steam's page. The changes are sent with each mod's next upload, " +
                   "like changes made in Workshop page..., and listed in the upload confirmation.",
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var size in new[] { 0, 0, 0, 0, 100, 0 }) root.RowStyles.Add(size == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, size));
        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 8, 0) };
        root.Controls.Add(intro, 0, 0); root.SetColumnSpan(intro, 2);
        root.Controls.Add(_useVisibility, 0, 1); root.Controls.Add(_visibility, 1, 1);
        root.Controls.Add(L("In descriptions, &replace"), 0, 2); root.Controls.Add(_find, 1, 2);
        root.Controls.Add(L("&with"), 0, 3); root.Controls.Add(_replace, 1, 3);
        root.Controls.Add(L("&Add at the end"), 0, 4); root.Controls.Add(_append, 1, 4);

        var save = new Button { Text = "Save for next upload", AutoSize = true, Padding = new Padding(8, 2, 8, 2) }.Role(ThemeRole.Accent);
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        root.Controls.Add(buttons, 0, 5); root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
        save.Click += (_, _) =>
        {
            var edit = new BulkPageEdit(_useVisibility.Checked ? _visibility.SelectedIndex : null, _find.Text, _replace.Text, _append.Text);
            if (edit.Visibility == null && !edit.ChangesDescriptions) { MessageBox.Show(this, "Nothing to change yet.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (edit.Visibility is 0 && mods.Any(m => !m.OnSteam)
                && MessageBox.Show(this, "Mods that are not on Steam yet will be created PUBLIC. Continue?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Result = edit;
            DialogResult = DialogResult.OK;
        };
        Theme.Apply(this);
    }
}
