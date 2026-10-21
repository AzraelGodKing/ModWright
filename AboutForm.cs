using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;

namespace SteamCmdHelper;

/// <summary>
/// Right-click > RimWorld > Edit About.xml: the fields players see, the mods it needs and its load-order rules, with
/// the Drop Pod's checks shown live as you type. Rules can be picked from the mods The Drop Pod found installed, so
/// packageIds and Workshop links are right the first time. Fields it doesn't show stay in the file as they were.
/// </summary>
public sealed class AboutForm : Form
{
    readonly TextBox _name = new() { Width = 420, AccessibleName = "Name" };
    readonly TextBox _author = new() { Width = 420, AccessibleName = "Author", PlaceholderText = "Several: separate them with commas" };
    readonly TextBox _packageId = new() { Width = 420, AccessibleName = "packageId", PlaceholderText = "YourName.ModName" };
    readonly TextBox _modVersion = new() { Width = 140, AccessibleName = "Mod version" };
    readonly TextBox _url = new() { Width = 420, AccessibleName = "Web page" };
    readonly TextBox _versions = new() { Width = 420, AccessibleName = "Supported versions", PlaceholderText = "e.g. 1.5, 1.6" };
    readonly TextBox _desc = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 420, Height = 90, AccessibleName = "Description" };
    readonly ListBox _deps = new() { Width = 420, Height = 70, AccessibleName = "Needs (modDependencies)" };
    readonly ListBox _after = new() { Width = 420, Height = 54, AccessibleName = "Load after" };
    readonly ListBox _before = new() { Width = 420, Height = 54, AccessibleName = "Load before" };
    readonly ListBox _incompatible = new() { Width = 420, Height = 54, AccessibleName = "Incompatible with" };
    readonly ListView _lint = new() { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, Width = 560, Height = 110, AccessibleName = "Checks" };
    readonly Label _lintTitle = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 2) }.Role(ThemeRole.Title);
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };
    readonly string? _xml;
    readonly Func<List<ModManifest>> _library;

    public AboutFields Result { get; private set; }

    /// <param name="library">The installed mods The Drop Pod knows, for picking rules.</param>
    public AboutForm(string modName, AboutFields fields, string? xml, Func<List<ModManifest>> library)
    {
        _xml = xml;
        _library = library;
        Result = fields;
        Text = "About.xml - " + modName;
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _name.Text = fields.Name; _author.Text = fields.Author; _packageId.Text = fields.PackageId; _modVersion.Text = fields.ModVersion;
        _url.Text = fields.Url; _versions.Text = string.Join(", ", fields.SupportedVersions); _desc.Text = fields.Description.Replace("\r\n", "\n").Replace("\n", "\r\n");
        foreach (var d in fields.Dependencies) _deps.Items.Add(new DepItem(d));
        foreach (var (box, items) in new[] { (_after, fields.LoadAfter), (_before, fields.LoadBefore), (_incompatible, fields.Incompatible) })
            foreach (var i in items) box.Items.Add(i);
        _lint.Columns.Add("", 540);

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Location = new Point(12, 12), Padding = new Padding(0, 0, 12, 12) };
        void Row(string label, Control c, Control? extra = null)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 8, 0) });
            if (extra == null) { grid.Controls.Add(c); return; }
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            p.Controls.Add(c);
            p.Controls.Add(extra);
            grid.Controls.Add(p);
        }
        Row("&Name", _name);
        Row("&Author", _author);
        Row("&packageId", _packageId);
        Row("Mod &version", _modVersion);
        Row("&Web page", _url);
        Row("&Game versions", _versions);
        Row("&Description", _desc);
        Row("Needs", _deps, Buttons(_deps, dependency: true));
        Row("Load after", _after, Buttons(_after));
        Row("Load before", _before, Buttons(_before));
        Row("Incompatible with", _incompatible, Buttons(_incompatible));
        grid.Controls.Add(new Label());
        var lintBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        lintBox.Controls.Add(_lintTitle);
        lintBox.Controls.Add(_lint);
        grid.Controls.Add(lintBox);
        var ok = new Button { Text = "&Save", AutoSize = true, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.OK }.Role(ThemeRole.Primary);
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        grid.Controls.Add(new Label());
        grid.Controls.Add(buttons);
        Controls.Add(grid);
        CancelButton = cancel;

        foreach (var t in new[] { _name, _author, _packageId, _modVersion, _url, _versions, _desc }) t.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Recheck(); };
        ok.Click += (_, _) =>
        {
            Result = Collect();
            if (AboutEditor.Lint(Result, _xml).Any(f => f.Level == LintLevel.Block) &&
                MessageBox.Show(this, "RimWorld would refuse to load the mod as it is (see the checks). Save anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                DialogResult = DialogResult.None;
        };
        Theme.Apply(this);
        Recheck();
    }

    sealed record DepItem(ModDependency Dep)
    {
        public override string ToString() => Dep.DisplayName is { Length: > 0 } n ? $"{n} ({Dep.Id})" : Dep.Id;
    }

    FlowLayoutPanel Buttons(ListBox box, bool dependency = false)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
        Button B(string text, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, Font = Theme.SmallFont, Padding = new Padding(4, 0, 4, 0), Margin = new Padding(0, 0, 6, 0) };
            b.Click += (_, _) => { click(); Recheck(); };
            p.Controls.Add(b);
            return b;
        }
        B("Pick from installed mods...", () =>
        {
            var mods = _library();
            if (mods.Count == 0) { MessageBox.Show(this, "The Drop Pod hasn't found any installed mods yet. Open The Drop Pod once, or add a packageId by hand.", Text); return; }
            using var w = new ListWindow("Pick mods", "Tick the installed mods to add.", new[] { ("Mod", 260), ("packageId", 220) }, checkBoxes: true, size: new Size(560, 480));
            foreach (var m in mods.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)) w.AddRow(m, Tone.Normal, m.DisplayName, m.Id);
            w.AddButton("&Add", () => { w.DialogResult = DialogResult.OK; w.Close(); }, primary: true);
            w.AddClose("Cancel");
            if (w.ShowDialog(this) != DialogResult.OK) return;
            foreach (var m in w.Ticked.OfType<ModManifest>())
                if (dependency) { if (!box.Items.OfType<DepItem>().Any(d => d.Dep.Key == m.Key)) box.Items.Add(new DepItem(AboutEditor.DependencyOn(m))); }
                else if (!box.Items.OfType<string>().Contains(m.Id, StringComparer.OrdinalIgnoreCase)) box.Items.Add(m.Id);
        });
        B("Add by packageId...", () =>
        {
            var id = Ask("packageId (such as brrainz.harmony):");
            if (id.Length == 0) return;
            box.Items.Add(dependency ? new DepItem(new ModDependency(id, null, null, null)) : id);
        });
        B("Remove", () => { if (box.SelectedIndex >= 0) box.Items.RemoveAt(box.SelectedIndex); });
        return p;
    }

    string Ask(string label)
    {
        using var f = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(360, 110), Font = Font };
        var l = new Label { Text = label, AutoSize = true, Location = new Point(12, 12) };
        var t = new TextBox { Location = new Point(12, 36), Width = 336, AccessibleName = label };
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Location = new Point(192, 72), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(273, 72), Width = 75 };
        f.Controls.AddRange(new Control[] { l, t, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        ok.Role(ThemeRole.Accent);
        Theme.Apply(f);
        return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : "";
    }

    AboutFields Collect() => new()
    {
        Name = _name.Text.Trim(), Author = _author.Text.Trim(), PackageId = _packageId.Text.Trim(), ModVersion = _modVersion.Text.Trim(), Url = _url.Text.Trim(),
        Description = _desc.Text.Replace("\r\n", "\n").Trim(),
        SupportedVersions = _versions.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToList(),
        Dependencies = _deps.Items.OfType<DepItem>().Select(d => d.Dep).ToList(),
        LoadAfter = _after.Items.OfType<string>().ToList(), LoadBefore = _before.Items.OfType<string>().ToList(), Incompatible = _incompatible.Items.OfType<string>().ToList(),
    };

    void Recheck()
    {
        List<LintFinding> found;
        try { found = AboutEditor.Lint(Collect(), _xml); }
        catch (Exception e) { found = new() { new LintFinding("about-xml", LintLevel.Block, "The file could not be read: " + e.Message) }; }
        _lint.BeginUpdate();
        _lint.Items.Clear();
        foreach (var f in found)
            _lint.Items.Add(new ListViewItem((f.Level == LintLevel.Block ? "Blocks: " : "") + f.Message) { ForeColor = Theme.Of(f.Level == LintLevel.Block ? Tone.Error : Tone.Warn) });
        _lint.EndUpdate();
        _lintTitle.Text = found.Count == 0 ? "Checks: all good" : $"Checks: {found.Count} to look at";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }
}
