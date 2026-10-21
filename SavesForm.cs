using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// Drop Pod > Saves...: the save games of the picked instance (or the normal game), each compared with the active
/// mods, and a way to make the active mods match a save.
/// </summary>
public sealed class SavesForm : Form
{
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Width = 760, Height = 260, AccessibleName = "Save games" };
    readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 760, Height = 140, AccessibleName = "How the save differs from the active mods" };
    readonly Button _use = new Button { Text = "&Use this save's mods", AutoSize = true, Padding = new Padding(12, 2, 12, 2), Enabled = false }.Role(ThemeRole.Primary);
    readonly Button _asList = new() { Text = "Save as a &modlist...", AutoSize = true, Enabled = false };
    readonly Func<SaveInfo, SaveDiff> _compare;

    /// <summary>The save picked with Use or Save as a modlist.</summary>
    public SaveInfo? Picked { get; private set; }
    /// <summary>True when the pick is to be saved as a modlist instead of used.</summary>
    public bool AsModlist { get; private set; }

    public SavesForm(string where, Func<List<SaveInfo>> load, Func<SaveInfo, SaveDiff> compare)
    {
        _compare = compare;
        Text = "Saves";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var heading = new Label { AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 0, 0, 6), UseMnemonic = false, Text = $"Saves of {where}, newest first. Pick one to see how its mods differ from the active ones." }.Role(ThemeRole.Muted);
        foreach (var (name, width) in new[] { ("Save", 220), ("Saved", 130), ("Game version", 120), ("Mods", 60), ("Compared with the active mods", 220) })
            _list.Columns.Add(Localizer.T(name), (int)(width * Theme.TextScale));
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 2, 12, 2) };
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(_use);
        buttons.Controls.Add(_asList);
        buttons.Controls.Add(close);
        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(16, 12), Padding = new Padding(0, 0, 16, 12) };
        root.Controls.Add(heading);
        root.Controls.Add(_list);
        root.Controls.Add(_details);
        root.Controls.Add(buttons);
        Controls.Add(root);
        CancelButton = close;

        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.DoubleClick += (_, _) => Pick(false);
        _use.Click += (_, _) => Pick(false);
        _asList.Click += (_, _) => Pick(true);
        _list.Items.Add(new ListViewItem(new[] { "Reading the saves...", "", "", "", "" }));
        Shown += async (_, _) =>
        {
            List<SaveInfo> saves;
            try { saves = await Task.Run(load); }
            catch (Exception e) { _list.Items[0].Text = "Could not read the saves: " + e.Message; return; }
            Fill(saves);
        };
        Theme.Apply(this);
    }

    void Fill(List<SaveInfo> saves)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var s in saves)
        {
            var summary = s.Error != null ? "Can't be read" : Describe(_compare(s));
            _list.Items.Add(new ListViewItem(new[] { s.Name, s.Modified == default ? "" : s.Modified.ToString("yyyy-MM-dd HH:mm"), s.GameVersion, s.Error != null ? "" : s.Mods.Mods.Count.ToString(), summary })
            {
                Tag = s,
                ForeColor = s.Error != null ? Theme.Of(Tone.Error) : summary == "Matches" ? Theme.Of(Tone.Normal) : Theme.Of(Tone.Warn),
            });
        }
        if (saves.Count == 0) _list.Items.Add(new ListViewItem(new[] { "No saves yet", "", "", "", "" }));
        _list.EndUpdate();
        if (saves.Count > 0) _list.Items[0].Selected = true;
    }

    static string Describe(SaveDiff d)
    {
        if (d.Matches) return "Matches";
        var parts = new List<string>();
        if (d.Missing.Count > 0) parts.Add($"{d.Missing.Count} not installed");
        if (d.Inactive.Count > 0) parts.Add($"{d.Inactive.Count} not active");
        if (d.Extra.Count > 0) parts.Add($"{d.Extra.Count} extra");
        if (d.OrderDiffers) parts.Add("other order");
        return string.Join(", ", parts);
    }

    void ShowSelected()
    {
        var s = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as SaveInfo : null;
        _use.Enabled = _asList.Enabled = s is { Error: null } && s.Mods.Mods.Count > 0;
        if (s == null) { _details.Text = ""; return; }
        if (s.Error != null) { _details.Text = s.Error; return; }
        var d = _compare(s);
        string Names(IEnumerable<ModlistEntry> e) => string.Join(", ", e.Select(m => m.Name ?? m.Id));
        var lines = new List<string>();
        if (d.Matches) lines.Add("The active mods are exactly the mods this save was played with, in the same order.");
        if (d.Missing.Count > 0) lines.Add($"Not installed ({d.Missing.Count}): {Names(d.Missing)}" + (d.Missing.Any(m => m.WorkshopId != null) ? ". Use this save's mods offers to download the ones on the Workshop." : "."));
        if (d.Inactive.Count > 0) lines.Add($"Installed but not active ({d.Inactive.Count}): {Names(d.Inactive)}.");
        if (d.Extra.Count > 0) lines.Add($"Active but not in the save ({d.Extra.Count}): {string.Join(", ", d.Extra)}. Adding mods to a save usually works; removing them can break it.");
        if (d.OrderDiffers) lines.Add("The mods both have are in another order.");
        _details.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    void Pick(bool asModlist)
    {
        if (_list.SelectedItems.Count != 1 || _list.SelectedItems[0].Tag is not SaveInfo { Error: null } s || s.Mods.Mods.Count == 0) return;
        Picked = s;
        AsModlist = asModlist;
        DialogResult = DialogResult.OK;
    }
}
