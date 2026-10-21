using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// Drop Pod > Problems > Insight: hints about speed and clashes among the active mods (Harmony patches on the same
/// method, per-tick patches, heavy textures, the player's own notes), worked out in the background.
/// </summary>
public sealed class InsightForm : Form
{
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Width = 900, Height = 360, ShowItemToolTips = true, AccessibleName = "Hints about the active mods" };
    readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(900, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, 6), Text = "Reading the active mods' code and folders..." };

    static readonly Dictionary<InsightKind, string> Kinds = new()
    {
        [InsightKind.PatchConflict] = "Clash",
        [InsightKind.TickPatches] = "Per tick",
        [InsightKind.LoadWeight] = "Loading",
        [InsightKind.RulesNote] = "Your note",
    };

    public InsightForm(Func<CancellationToken, List<InsightItem>> work, Func<string, string> name)
    {
        Text = "Insight";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        foreach (var (col, width) in new[] { ("What", 80), ("Mods", 220), ("Hint", 580) })
            _list.Columns.Add(Localizer.T(col), (int)(width * Theme.TextScale));
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 6, 0, 0), UseMnemonic = false,
            Text = "These are hints from what the mods' files declare, not measurements. Patches a mod sets up in code while the game runs " +
                   "can't be seen here, and several mods changing one method usually work together fine; look here when the log shows errors there.",
        }.Role(ThemeRole.Muted);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 2, 12, 2), Margin = new Padding(0, 10, 0, 0) };
        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(16, 12), Padding = new Padding(0, 0, 16, 12) };
        root.Controls.Add(_summary);
        root.Controls.Add(_list);
        root.Controls.Add(hint);
        root.Controls.Add(close);
        Controls.Add(root);
        CancelButton = close;

        var cts = new CancellationTokenSource();
        FormClosed += (_, _) => { cts.Cancel(); cts.Dispose(); };
        Shown += async (_, _) =>
        {
            List<InsightItem> items;
            try { items = await Task.Run(() => work(cts.Token)); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { _summary.Text = "Could not read the mods: " + e.Message; return; }
            if (IsDisposed) return;
            _list.BeginUpdate();
            foreach (var i in items)
                _list.Items.Add(new ListViewItem(new[] { Kinds[i.Kind], string.Join(", ", i.Mods.Select(name)), i.Message })
                {
                    ToolTipText = i.Message,
                    ForeColor = Theme.Of(i.Level == ProblemLevel.Warning ? Tone.Warn : Tone.Normal),
                });
            if (items.Count == 0) _list.Items.Add(new ListViewItem(new[] { "", "", "Nothing stands out among the active mods." }));
            _list.EndUpdate();
            var clashes = items.Count(i => i.Kind == InsightKind.PatchConflict);
            _summary.Text = $"{clashes} possible clash{(clashes == 1 ? "" : "es")}, {items.Count(i => i.Kind == InsightKind.TickPatches)} mods with per-tick patches, " +
                            $"{items.Count(i => i.Kind == InsightKind.LoadWeight)} heavy to load.";
        };
        Theme.Apply(this);
    }
}
