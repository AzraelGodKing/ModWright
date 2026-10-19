namespace SteamCmdHelper;

/// <summary>Asks where and on which branch to install a test copy of the game ("Try in a game beta...").</summary>
public sealed class TestInstallForm : Form
{
    readonly ComboBox _branch = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 260, AccessibleName = "Game branch" };
    readonly TextBox _folder = new() { Width = 360, AccessibleName = "Install folder" };
    readonly TextBox _password = new() { Width = 260, UseSystemPasswordChar = true, AccessibleName = "Beta password", PlaceholderText = "only for betas that need one" };
    readonly CheckBox _addMod = new() { AutoSize = true };

    public string Branch => string.IsNullOrWhiteSpace(_branch.Text) ? "public" : _branch.Text.Split(' ')[0].Trim();
    public string Folder => _folder.Text.Trim();
    public string? Password => _password.Text.Length > 0 ? _password.Text : null;
    public bool AddMod => _addMod.Visible && _addMod.Checked;

    /// <param name="branches">The game's branches, when Steam told us; the box still takes any name.</param>
    /// <param name="mod">The selected mod, offered to put into the install with what it needs; null for none.</param>
    /// <param name="modsFolder">Where the game loads mods from (empty: the layout does not say, so no mods are added).</param>
    public TestInstallForm(string game, string appId, IReadOnlyList<GameBranch>? branches, string folder, ModInfo? mod, string modsFolder)
    {
        Text = "Try in a game beta";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        foreach (var b in branches ?? new List<GameBranch> { new("public", "", null, "", false) })
            _branch.Items.Add(b.Name + (b.Description.Length > 0 ? $"  ({b.Description})" : "") + (b.Password ? "  [password]" : ""));
        // A beta anyone can install is the usual reason to be here, so it is picked when there is one.
        var open = branches?.ToList().FindIndex(b => !b.IsPublic && !b.Password) ?? -1;
        _branch.SelectedIndex = open >= 0 ? open : 0;
        _folder.Text = folder;
        _addMod.Text = mod != null ? $"Put {mod.Name} and the Workshop items it needs into the game's {modsFolder} folder" : "";
        _addMod.Checked = _addMod.Visible = mod != null && modsFolder.Length > 0;

        var browse = new Button { Text = "&Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Pick an empty folder for the test install", SelectedPath = _folder.Text, UseDescriptionForTitle = true };
            if (d.ShowDialog(this) == DialogResult.OK) _folder.Text = d.SelectedPath;
        };

        var ok = new Button { Text = "&Install", AutoSize = true, Padding = new Padding(14, 2, 14, 2) }.Role(ThemeRole.Accent);
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(10, 2, 10, 2) };
        ok.Click += (_, _) =>
        {
            if (TestInstall.FolderProblem(Folder, appId) is { } why) { MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            DialogResult = DialogResult.OK;
        };

        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3 };
        grid.Controls.Add(L("&Branch"), 0, 0); grid.Controls.Add(_branch, 1, 0);
        grid.Controls.Add(L("&Folder"), 0, 1); grid.Controls.Add(_folder, 1, 1); grid.Controls.Add(browse, 2, 1);
        grid.Controls.Add(L("&Password"), 0, 2); grid.Controls.Add(_password, 1, 2);

        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 10),
            Text = $"Installs a separate copy of {(game.Length > 0 ? game : "the game")} with SteamCMD, using your saved Steam login (the account must own the game). " +
                   "Your normal Steam install is not touched. The first install downloads the whole game; later ones only update it.",
        }.Role(ThemeRole.Muted);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12) };
        root.Controls.Add(intro);
        root.Controls.Add(grid);
        root.Controls.Add(_addMod);
        root.Controls.Add(buttons);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        Theme.Apply(this);
    }
}
