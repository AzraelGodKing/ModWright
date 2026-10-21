namespace SteamCmdHelper;

/// <summary>Drop Pod > Instance > New...: the name, starting mods and game install of a new instance.</summary>
public sealed class InstanceForm : Form
{
    readonly TextBox _name = new() { Width = 300, AccessibleName = "Instance name" };
    readonly ComboBox _mods = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "Mods to start with" };
    readonly CheckBox _copy = new() { Text = "&Copy my game options and key bindings", AutoSize = true, Checked = true };
    readonly TextBox _game = new() { Width = 380, PlaceholderText = "The usual install", AccessibleName = "Game folder" };

    public string InstanceName => _name.Text.Trim();
    /// <summary>0 = the active mods shown now, 1 = only the base game and DLC, 2+ = the modlist of that name (<see cref="ModlistName"/>).</summary>
    public int ModsChoice => _mods.SelectedIndex;
    public string? ModlistName => _mods.SelectedIndex >= 2 ? _mods.SelectedItem as string : null;
    public bool CopySettings => _copy.Checked;
    public string GameFolder => _game.Text.Trim();

    public InstanceForm(string gameName, IEnumerable<string> modlists)
    {
        Text = "New instance";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 0, 0, 8), UseMnemonic = false,
            Text = $"An instance is a separate {gameName} setup with its own options, mod list, saves and log; your normal game is not touched. " +
                   "The mods themselves are shared. Pick it in the Instance list to change its mods, and press Play to start it.",
        }.Role(ThemeRole.Muted);
        _mods.Items.Add("The active mods shown now");
        _mods.Items.Add("Only the base game and DLC");
        foreach (var m in modlists) _mods.Items.Add(m);
        _mods.SelectedIndex = 0;
        var browse = new Button { Text = "&Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = $"Pick another {gameName} install folder for this instance", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) _game.Text = dlg.SelectedPath;
        };
        var ok = new Button { Text = "&Create", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(12, 2, 12, 2), Enabled = false }.Role(ThemeRole.Primary);
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 2, 12, 2) };
        _name.TextChanged += (_, _) => ok.Enabled = InstanceName.Length > 0;

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        void Line(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 10, 0) });
            grid.Controls.Add(c);
        }
        Line("&Name", _name);
        Line("&Mods", _mods);
        var gameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        gameRow.Controls.Add(_game);
        gameRow.Controls.Add(browse);
        Line("&Game folder", gameRow);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(16, 12), Padding = new Padding(0, 0, 16, 12) };
        root.Controls.Add(hint);
        root.Controls.Add(grid);
        root.Controls.Add(_copy);
        root.Controls.Add(buttons);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        Theme.Apply(this);
    }
}
