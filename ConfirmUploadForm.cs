namespace SteamCmdHelper;

/// <summary>
/// The upload confirmation: what each ticked mod will do on Steam, and its change note, which can be edited before it
/// is sent. Edits change only what goes to Steam, never the changelog file. When git packaging would leave a mod's
/// uncommitted changes out, it offers to commit them first.
/// </summary>
public sealed class ConfirmUploadForm : Form
{
    public const int MaxNote = 8000;

    readonly List<ModInfo> _mods;
    readonly Dictionary<string, string> _original = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _notes = new(StringComparer.OrdinalIgnoreCase);
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly TextBox _note = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    readonly Label _noteTitle = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
    readonly Label _count = new Label { AutoSize = true, Anchor = AnchorStyles.Right }.Role(ThemeRole.Muted);
    readonly LinkLabel _reset = new() { Text = "Use the changelog entry", AutoSize = true, Anchor = AnchorStyles.Left, LinkBehavior = LinkBehavior.HoverUnderline };
    readonly LinkLabel _fromGit = new() { Text = "Draft from git commits", AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(12, 3, 3, 3) };
    readonly LinkLabel _preview = new() { Text = "Preview as on Steam", AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(12, 3, 3, 3) };
    readonly Func<ModInfo, GitOps.CommitNote?>? _draft;
    readonly Func<ModInfo, string, string>? _asSent;
    readonly CheckBox _commitFirst = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    ModInfo? _shown;

    /// <summary>Notes the user changed, by mod folder (only those that differ from the changelog entry).</summary>
    public Dictionary<string, string> EditedNotes => _notes.Where(kv => kv.Value != _original[kv.Key]).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the uncommitted changes of the listed mods should be committed before packaging.</summary>
    public bool CommitFirst => _commitFirst.Visible && _commitFirst.Checked;

    /// <param name="heading">"Publish to the Steam Workshop (RimWorld):" or "Dry run for:".</param>
    /// <param name="actions">What each mod will do, by mod folder ("update 123", "NEW private item", plus page changes).</param>
    /// <param name="notes">Each mod's change note from its changelog, by mod folder.</param>
    /// <param name="offerCommit">Git packaging is in use, so uncommitted changes would be left out.</param>
    /// <param name="draft">Drafts a mod's note from its git commits since its last upload; null hides the link.</param>
    /// <param name="warnings">Things to check before sending (such as a version Steam already has), shown above the note.</param>
    /// <param name="checklist">The author's release checklist for this mods folder, as boxes to tick.</param>
    /// <param name="mustTick">Upload stays refused until every checklist box is ticked (not for dry runs).</param>
    public ConfirmUploadForm(string heading, IReadOnlyList<ModInfo> mods, IReadOnlyDictionary<string, string> actions,
        IReadOnlyDictionary<string, string> notes, bool dry, bool offerCommit, Func<ModInfo, GitOps.CommitNote?>? draft = null,
        IReadOnlyList<string>? warnings = null, Func<ModInfo, string, string>? asSent = null,
        IReadOnlyList<string>? checklist = null, bool mustTick = false)
    {
        _draft = draft;
        _asSent = asSent;
        _preview.Visible = asSent != null;
        _fromGit.Visible = draft != null;
        _mods = mods.ToList();
        foreach (var m in _mods) { _original[m.Dir] = notes.TryGetValue(m.Dir, out var n) ? n : ""; _notes[m.Dir] = _original[m.Dir]; }
        Text = dry ? "Dry run" : "Upload to Steam";
        Width = 760; Height = 600; MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

        _list.Columns.Add("Mod", 220);
        _list.Columns.Add("Version", 80);
        _list.Columns.Add("On Steam", 380);
        foreach (var m in _mods)
            _list.Items.Add(new ListViewItem(new[] { m.Name, m.Version, actions.TryGetValue(m.Dir, out var a) ? a : "" }) { Tag = m });
        _list.Height = Math.Min(200, 28 + 22 * _mods.Count);

        var dirty = _mods.Where(m => m.Dirty).ToList();
        _commitFirst.Text = $"Commit the uncommitted changes in {(dirty.Count == 1 ? dirty[0].Name : dirty.Count + " mods")} first (git packaging uploads only what is committed)";
        _commitFirst.Visible = offerCommit && dirty.Count > 0;
        _commitFirst.Checked = _commitFirst.Visible;

        var intro = new Label { Text = heading, AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 0, 0, 6) };
        var foot = new Label
        {
            AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 8, 0, 0),
            Text = (dry ? "Nothing is uploaded: every check runs and the package is built, then removed."
                        : "Updates go live for subscribers right away.") +
                   " Editing a change note here changes only what is sent to Steam, not your changelog file.",
        }.Role(ThemeRole.Muted);
        var noteRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        noteRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        noteRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        _reset.Margin = new Padding(0, 3, 3, 3);
        links.Controls.Add(_reset);
        links.Controls.Add(_fromGit);
        links.Controls.Add(_preview);
        noteRow.Controls.Add(links, 0, 0);
        noteRow.Controls.Add(_count, 1, 0);

        var warn = new Label
        {
            AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 8, 0, 0), ForeColor = Theme.Current.Warn,
            Visible = warnings is { Count: > 0 }, Text = string.Join("\n", (warnings ?? Array.Empty<string>()).Select(w => "• " + w)),
        }.Role(ThemeRole.Keep);

        // The Launch Pad's release checklist: the author's own steps, ticked off before sending.
        var checks = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 8, 0, 0), Visible = checklist is { Count: > 0 } };
        if (checklist is { Count: > 0 })
        {
            checks.Controls.Add(new Label { AutoSize = true, Text = mustTick && !dry ? "Release checklist (tick every step to upload):" : "Release checklist:", Font = new Font("Segoe UI Semibold", 9.5f), Margin = new Padding(0, 0, 0, 2) });
            foreach (var step in checklist) checks.Controls.Add(new CheckBox { Text = step, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 0) });
        }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Padding = new Padding(12) };
        foreach (var size in new[] { 0, 0, 0, 0, 100, 0, 0, 0, 0, 0 })
            root.RowStyles.Add(size == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, size));
        var listHost = new Panel { Dock = DockStyle.Top, Height = _list.Height, Margin = Padding.Empty };
        listHost.Controls.Add(_list);
        root.Controls.Add(intro, 0, 0);
        root.Controls.Add(listHost, 0, 1);
        root.Controls.Add(warn, 0, 2);
        root.Controls.Add(_noteTitle, 0, 3);
        root.Controls.Add(_note, 0, 4);
        root.Controls.Add(noteRow, 0, 5);
        root.Controls.Add(_commitFirst, 0, 6);
        root.Controls.Add(checks, 0, 7);
        root.Controls.Add(foot, 0, 8);

        var go = new Button { Text = dry ? "Dry run" : "Upload", AutoSize = true, Padding = new Padding(12, 2, 12, 2), DialogResult = DialogResult.OK }.Role(dry ? ThemeRole.Accent : ThemeRole.Primary);
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(go);
        root.Controls.Add(buttons, 0, 9);
        Controls.Add(root);
        CancelButton = cancel;

        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedItems.Count == 1) Show((ModInfo)_list.SelectedItems[0].Tag!); };
        _note.TextChanged += (_, _) =>
        {
            if (_shown == null) return;
            _notes[_shown.Dir] = _note.Text.Replace("\r\n", "\n").Trim();
            ShowCount();
        };
        _reset.LinkClicked += (_, _) => { if (_shown != null) _note.Text = Crlf(_original[_shown.Dir]); };
        _fromGit.LinkClicked += (_, _) =>
        {
            if (_shown == null || _draft == null) return;
            var n = _draft(_shown);
            if (n == null) { _noteTitle.Text = $"Change note for {_shown.Name} (no commits touched it since its last upload from this PC)"; return; }
            _note.Text = Crlf(n.Note);
            _noteTitle.Text = $"Change note for {_shown.Name} (drafted from {n.Source})";
            _note.Focus();
        };
        _preview.LinkClicked += (_, _) =>
        {
            if (_shown == null || _asSent == null) return;
            try
            {
                var file = SteamText.WritePreview(_shown.Name, _asSent(_shown, _notes[_shown.Dir]), $"Change note for version {_shown.Version}, as Steam will show it");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not open the preview: " + ex.Message, Text); }
        };
        go.Click += (_, e) =>
        {
            if (mustTick && !dry && checks.Controls.OfType<CheckBox>().FirstOrDefault(c => !c.Checked) is { } open)
            {
                MessageBox.Show(this, $"Tick every step of your release checklist first (\"{open.Text}\" is not ticked).", Text);
                open.Focus();
                DialogResult = DialogResult.None;
                return;
            }
            var tooLong = _mods.FirstOrDefault(m => _notes[m.Dir].Length > MaxNote);
            if (tooLong == null) return;
            MessageBox.Show(this, $"The change note for {tooLong.Name} is over Steam's {MaxNote} characters. Shorten it first.", Text);
            DialogResult = DialogResult.None;
        };

        Theme.Apply(this);
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        Shown += (_, _) => _list.Columns[2].Width = Math.Max(160, _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[1].Width - 4);
    }

    void Show(ModInfo m)
    {
        _shown = null; // not an edit
        _noteTitle.Text = $"Change note for {m.Name}";
        _note.Text = Crlf(_notes[m.Dir]);
        _shown = m;
        ShowCount();
    }

    void ShowCount()
    {
        if (_shown == null) return;
        var n = _notes[_shown.Dir].Length;
        var edited = _notes[_shown.Dir] != _original[_shown.Dir];
        _count.Text = (edited ? "edited · " : "") + $"{n} / {MaxNote}" + (n == 0 ? " · empty" : "");
        _reset.Visible = edited;
    }

    static string Crlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");
}
