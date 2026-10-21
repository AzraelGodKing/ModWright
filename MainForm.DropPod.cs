using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;

namespace SteamCmdHelper;

/// <summary>
/// The Drop Pod tab: the RimWorld mods a player has installed (base game and DLC, the Mods folder and Steam Workshop),
/// read by <see cref="ModLibrary"/>. The logic lives in DropPod/; this file only shows it.
/// </summary>
public sealed partial class MainForm
{
    readonly IGameModule _dpGame = new RimWorldModule();
    readonly ListView _dpList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, AccessibleName = "Installed mods" };
    readonly TextBox _dpFilter = new() { Width = 170, PlaceholderText = "Filter mods", AccessibleName = "Filter installed mods" };
    readonly Label _dpGameText = new() { AutoSize = true, UseMnemonic = false, Tag = "fill" };
    readonly Label _dpCount = new Label { AutoSize = true, UseMnemonic = false }.Role(ThemeRole.Muted);
    readonly Label _dpNote = new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(900, 0), Visible = false };
    readonly List<ListViewItem> _dpRows = new();
    LibraryScan? _dpScan;
    bool _dpScanning, _dpScannedOnce;

    static readonly string[] DropPodColumns = { "Name", "Author", "Package ID", "Source", "Game versions", "Load order" };

    Control BuildDropPodPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14, 12, 14, 12), Visible = false };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var pick = Btn("Game &folder...", (_, _) => PickGameFolder());
        var rescan = SmallBtn("Rescan", (_, _) => _ = ScanDropPodAsync());
        Tip(pick, "Pick RimWorld's install folder by hand, when it isn't found through Steam");
        Tip(rescan, "Read every mod folder again (F5 on this tab)");
        page.Controls.Add(Card("RimWorld", new Control[] { rescan }, Row(_dpGameText, pick), Row(_dpNote)), 0, 0);

        for (var i = 0; i < DropPodColumns.Length; i++)
            _dpList.Columns.Add(Localizer.T(DropPodColumns[i]), (int)(new[] { 240, 140, 200, 90, 110, 90 }[i] * Theme.TextScale));
        _dpFilter.TextChanged += (_, _) => FillDropPodList();
        page.Controls.Add(Card("Installed mods", new Control[] { _dpCount, _dpFilter }, _dpList), 0, 1);
        ((Control)page.Controls[1]).Dock = DockStyle.Fill;
        return page;
    }

    /// <summary>The first time the tab opens, read the mods (in the background).</summary>
    void DropPodShown()
    {
        if (_dpScannedOnce) return;
        _dpScannedOnce = true;
        _ = ScanDropPodAsync();
    }

    async Task ScanDropPodAsync()
    {
        if (_dpScanning) return;
        _dpScanning = true;
        _dpGameText.Text = "Looking for RimWorld...";
        try
        {
            _s.GameFolders.TryGetValue(_dpGame.AppId, out var gameFolder);
            _s.GameDataFolders.TryGetValue(_dpGame.AppId, out var dataFolder);
            var scan = await Task.Run(() =>
            {
                var install = _dpGame.Detect(SteamLibraries.Find(), gameFolder, dataFolder);
                return install == null ? null : ModLibrary.Scan(_dpGame, install);
            });
            _dpScan = scan;
            ShowDropPodGame();
            FillDropPodList();
        }
        catch (Exception e)
        {
            _dpGameText.Text = "Could not read the mods: " + e.Message;
        }
        finally { _dpScanning = false; }
    }

    void ShowDropPodGame()
    {
        if (_dpScan == null)
        {
            _dpGameText.Text = "RimWorld was not found in your Steam libraries. Pick its install folder.";
            _dpGameText.ForeColor = Theme.Of(Tone.Warn);
            ShowNote(_dpNote, null);
            return;
        }
        var i = _dpScan.Install;
        _dpGameText.Text = $"RimWorld {i.Version ?? "(version unknown)"}  ·  {i.GameFolder}";
        _dpGameText.ForeColor = Theme.Of(Tone.Normal);
        Tip(_dpGameText, $"Game: {i.GameFolder}\nWorkshop: {i.WorkshopFolder ?? "none"}\nConfig and saves: {i.DataFolder}");
        var notes = new List<string>(_dpScan.Notes);
        var dupes = _dpScan.Duplicates();
        if (dupes.Count > 0) notes.Add($"{dupes.Count} package ID{(dupes.Count == 1 ? " is" : "s are")} installed more than once: " + string.Join(", ", dupes.Take(5).Select(g => g.First().Id)));
        if (_dpScan.Active == null) notes.Add("No ModsConfig.xml yet: start RimWorld once so it writes its mod list.");
        ShowNote(_dpNote, notes.Count == 0 ? null : string.Join("\n", notes));
    }

    void FillDropPodList()
    {
        _dpList.BeginUpdate();
        try
        {
            _dpList.Items.Clear();
            _dpRows.Clear();
            if (_dpScan == null) { _dpCount.Text = ""; return; }
            var order = (_dpScan.Active ?? Array.Empty<string>())
                .Select((id, n) => (Key: RimWorldIds.WithoutSteamSuffix(ModManifest.KeyOf(id)), n))
                .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().n + 1);
            var filter = _dpFilter.Text.Trim();
            foreach (var m in _dpScan.Mods)
            {
                order.TryGetValue(m.Key, out var pos);
                var cells = new[]
                {
                    m.DisplayName, string.Join(", ", m.Authors), m.Id.Length > 0 ? m.Id : "(none)",
                    m.Source switch { ModSource.Official => "Game", ModSource.Local => "Mods folder", _ => "Workshop" },
                    string.Join(", ", m.SupportedVersions), pos > 0 ? pos.ToString() : "",
                };
                if (filter.Length > 0 && !cells.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
                var it = new ListViewItem(cells) { Tag = m, UseItemStyleForSubItems = false };
                var tip = new List<string> { m.Folder };
                if (!m.Supports(_dpScan.Install.Version)) tip.Add($"Does not list RimWorld {GameVersion.Short(_dpScan.Install.Version!)} as supported.");
                tip.AddRange(m.Issues);
                it.ToolTipText = string.Join("\n", tip);
                _dpRows.Add(it);
            }
            _dpList.Items.AddRange(_dpRows.ToArray());
            var active = _dpScan.Mods.Count(m => order.ContainsKey(m.Key));
            _dpCount.Text = $"{_dpScan.Mods.Count} installed, {active} active";
            ColorDropPodRows();
        }
        finally { _dpList.EndUpdate(); }
    }

    /// <summary>Mods with a broken About.xml in the warning colour, mods for another game version muted.</summary>
    void ColorDropPodRows()
    {
        foreach (var it in _dpRows)
        {
            var m = (ModManifest)it.Tag!;
            var tone = m.Issues.Count > 0 ? Tone.Warn : !m.Supports(_dpScan?.Install.Version) ? Tone.Muted : Tone.Normal;
            foreach (ListViewItem.ListViewSubItem sub in it.SubItems)
            {
                sub.BackColor = _dpList.BackColor;
                sub.ForeColor = Theme.Of(tone);
            }
        }
    }

    void PickGameFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Pick RimWorld's install folder (the one with RimWorldWin64.exe)", UseDescriptionForTitle = true };
        if (_dpScan != null) dlg.InitialDirectory = _dpScan.Install.GameFolder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _s.GameFolders[_dpGame.AppId] = dlg.SelectedPath;
        _s.Save();
        _ = ScanDropPodAsync();
    }

    /// <summary>Opens the Drop Pod tab (for --shot).</summary>
    public void ShowDropPod() => ShowPage(_dropPodPage);
}
