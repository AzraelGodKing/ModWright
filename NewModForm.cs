namespace SteamCmdHelper;

/// <summary>"New mod...": the name and a few details, then the folder is made for the current layout.</summary>
public sealed class NewModForm : Form
{
    readonly TextBox _name = new() { Dock = DockStyle.Fill, AccessibleName = "Mod name" };
    readonly TextBox _folder = new() { Dock = DockStyle.Fill, AccessibleName = "Folder name" };
    readonly TextBox _author = new() { Dock = DockStyle.Fill, AccessibleName = "Author" };
    readonly TextBox _versions = new() { Dock = DockStyle.Fill, AccessibleName = "Supported game versions" };
    readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 80, AccessibleName = "Description" };
    readonly CheckBox _csharp = new() { Text = "Add a &C# project with Harmony (builds into the newest version's Assemblies folder)", AutoSize = true };
    readonly Label _package = new Label { AutoSize = true, UseMnemonic = false }.Role(ThemeRole.Muted);
    bool _folderTyped;

    public NewModOptions Result { get; private set; } = new();

    public NewModForm(string modsRoot, ProjectConfig cfg, string author, string newestVersion)
    {
        var rimworld = RimWorldLint.Applies(cfg);
        Text = "New mod";
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(600, rimworld ? 420 : 300);
        _author.Text = author;
        _versions.Text = newestVersion;

        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 8), UseMnemonic = false,
            Text = $"Makes a new mod folder in {modsRoot} with the files the {cfg.GameName} layout needs" +
                   (rimworld ? " (About.xml with a packageId, a preview image, a changelog entry and the description), ready to upload." : ", ready to fill in."),
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 8, 0) };
        root.Controls.Add(intro); root.SetColumnSpan(intro, 2);
        root.Controls.Add(L("&Name")); root.Controls.Add(_name);
        root.Controls.Add(L("&Folder")); root.Controls.Add(_folder);
        if (rimworld)
        {
            root.Controls.Add(L("&Author")); root.Controls.Add(_author);
            root.Controls.Add(new Label()); root.Controls.Add(_package);
            root.Controls.Add(L("Game &versions")); root.Controls.Add(_versions);
        }
        root.Controls.Add(L("&Description")); root.Controls.Add(_description);
        if (rimworld) { root.Controls.Add(new Label()); root.Controls.Add(_csharp); }

        var create = new Button { Text = "Create", AutoSize = true, Padding = new Padding(10, 2, 10, 2) }.Role(ThemeRole.Accent);
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(create);
        root.Controls.Add(buttons); root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
        AcceptButton = create;
        CancelButton = cancel;

        _name.TextChanged += (_, _) => { if (!_folderTyped) _folder.Text = NewMod.FolderFor(_name.Text); ShowPackage(); };
        _folder.TextChanged += (_, _) => { if (_folder.Focused) _folderTyped = true; ShowPackage(); };
        _author.TextChanged += (_, _) => ShowPackage();
        create.Click += (_, _) =>
        {
            var o = new NewModOptions
            {
                Name = _name.Text.Trim(), Folder = _folder.Text.Trim(), Author = _author.Text.Trim(), Description = _description.Text.Trim(),
                Versions = rimworld ? _versions.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>(),
                CSharp = rimworld && _csharp.Checked,
            };
            if (NewMod.Validate(modsRoot, o) is { } bad) { MessageBox.Show(this, bad, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Result = o;
            DialogResult = DialogResult.OK;
        };
        ShowPackage();
        Theme.Apply(this);
    }

    void ShowPackage() => _package.Text = "packageId: " + NewMod.PackageId(_author.Text, _folder.Text.Length > 0 ? _folder.Text : "ModName");
}
