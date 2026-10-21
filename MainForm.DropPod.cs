using System.Diagnostics;
using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;

namespace SteamCmdHelper;

/// <summary>
/// The Drop Pod tab: the RimWorld mods a player has installed (base game and DLC, the Mods folder and Steam Workshop),
/// which are active and in what order, and named modlists to switch between. The logic lives in DropPod/; this file
/// only shows it and passes clicks on.
/// </summary>
public sealed partial class MainForm
{
    readonly IGameModule _dpGame = new RimWorldModule();
    readonly ListView _dpList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, CheckBoxes = true, AccessibleName = "Installed mods; ticked mods are active, in load order" };
    readonly TextBox _dpFilter = new() { Width = 150, PlaceholderText = "Filter mods", AccessibleName = "Filter installed mods" };
    readonly ComboBox _dpModlist = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Modlist" };
    readonly Label _dpGameText = new() { AutoSize = true, UseMnemonic = false, Tag = "fill" };
    readonly Label _dpCount = new Label { AutoSize = true, UseMnemonic = false }.Role(ThemeRole.Muted);
    readonly Label _dpNote = new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(900, 0), Visible = false };
    readonly List<ListViewItem> _dpRows = new();
    Button _dpSave = null!, _dpUndo = null!, _dpSort = null!, _dpMore = null!;
    LibraryScan? _dpScan;
    /// <summary>The active mods (ids as the game writes them) in load order, as edited here; saved to the game with Save to game.</summary>
    List<string> _dpActive = new();
    bool _dpScanning, _dpScannedOnce, _dpDirty, _dpFilling;

    static readonly string[] DropPodColumns = { "Name", "Author", "Package ID", "Source", "Game versions", "Load order" };

    ModlistStore DropPodStore => new(Path.Combine(AppSettings.Folder, "droppod", "modlists"));

    Control BuildDropPodPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14, 12, 14, 12), Visible = false };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var pick = Btn("Game folder...", (_, _) => PickGameFolder());
        var rescan = SmallBtn("Rescan", (_, _) => _ = ScanDropPodAsync());
        var switchTo = Btn("Switch to it", async (_, _) => await SwitchModlistAsync());
        var saveAs = Btn("Save as...", (_, _) => SaveModlistAs());
        _dpMore = Btn("More ▾", (_, _) => ShowModlistMenu());
        Tip(pick, "Pick RimWorld's install folder by hand, when it isn't found through Steam");
        Tip(rescan, "Read every mod folder again (F5 on this tab)");
        Tip(switchTo, "Make this modlist the game's active mods (writes ModsConfig.xml; the old one is kept as a backup)");
        Tip(saveAs, "Save the active mods and their order as a named modlist");
        Tip(_dpMore, "Import or export a modlist file, share it as a code, delete it, or pick your own load-order rules file");
        page.Controls.Add(Card("RimWorld", new Control[] { rescan }, Row(_dpGameText, pick), Row(Lbl("Modlist"), _dpModlist, switchTo, saveAs, _dpMore), Row(_dpNote)), 0, 0);

        for (var i = 0; i < DropPodColumns.Length; i++)
            _dpList.Columns.Add(Localizer.T(DropPodColumns[i]), (int)(new[] { 240, 140, 200, 90, 110, 80 }[i] * Theme.TextScale));
        _dpFilter.TextChanged += (_, _) => FillDropPodList();
        _dpList.ItemChecked += (_, e) => DropPodTicked(e.Item);
        _dpList.KeyDown += (_, e) =>
        {
            if (e.Alt && e.KeyCode is Keys.Up or Keys.Down) { MoveDropPodMod(e.KeyCode == Keys.Up ? -1 : 1); e.Handled = e.SuppressKeyPress = true; }
        };
        _dpSort = SmallBtn("Sort ▾", (_, _) => ShowSortMenu());
        var up = SmallBtn("Up", (_, _) => MoveDropPodMod(-1));
        var down = SmallBtn("Down", (_, _) => MoveDropPodMod(1));
        _dpUndo = SmallBtn("Undo changes", (_, _) => { _dpActive = ActiveFromGame(); _dpDirty = false; FillDropPodList(); });
        _dpSave = SmallBtn("Save to game", (_, _) => SaveDropPodToGame()).Role(ThemeRole.Primary);
        Tip(_dpSort, "Order the active mods by their dependencies and load rules (and your rules file), or A to Z where no rule decides");
        Tip(up, "Load the selected mod earlier (Alt+Up)");
        Tip(down, "Load the selected mod later (Alt+Down)");
        Tip(_dpUndo, "Go back to the game's current mod list");
        Tip(_dpSave, "Write the ticked mods, in this order, to the game's ModsConfig.xml (the old one is kept as a backup)");
        page.Controls.Add(Card("Mods", new Control[] { _dpCount, _dpSort, up, down, _dpUndo, _dpSave, _dpFilter }, _dpList), 0, 1);
        ((Control)page.Controls[1]).Dock = DockStyle.Fill;
        UpdateDropPodButtons();
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
        if (_dpDirty && !ConfirmDiscardDropPod()) return;
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
            _dpActive = ActiveFromGame();
            _dpDirty = false;
            ShowDropPodGame(null);
            FillModlists();
            FillDropPodList();
        }
        catch (Exception e)
        {
            _dpGameText.Text = "Could not read the mods: " + e.Message;
        }
        finally { _dpScanning = false; }
    }

    List<string> ActiveFromGame() =>
        (_dpScan?.Active ?? Array.Empty<string>()).Select(_dpGame.ActiveKey).Distinct().ToList();

    /// <summary>The game line and the note under it: what the scan found odd, then <paramref name="extra"/> (the last action's outcome).</summary>
    void ShowDropPodGame(string? extra)
    {
        if (_dpScan == null)
        {
            _dpGameText.Text = "RimWorld was not found in your Steam libraries. Pick its install folder.";
            _dpGameText.ForeColor = Theme.Of(Tone.Warn);
            ShowNote(_dpNote, extra);
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
        if (extra != null) notes.Add(extra);
        ShowNote(_dpNote, notes.Count == 0 ? null : string.Join("\n", notes));
    }

    void FillDropPodList()
    {
        var keep = (_dpList.SelectedItems.Count > 0 ? _dpList.SelectedItems[0].Name : null);
        _dpFilling = true;
        _dpList.BeginUpdate();
        try
        {
            _dpList.Items.Clear();
            _dpRows.Clear();
            if (_dpScan == null) { _dpCount.Text = ""; return; }
            var byId = _dpScan.ById();
            var filter = _dpFilter.Text.Trim();
            var version = _dpScan.Install.Version;
            void Add(string key, ModManifest? m, bool active)
            {
                var cells = new[]
                {
                    m?.DisplayName ?? key, m == null ? "" : string.Join(", ", m.Authors), m == null ? key : m.Id.Length > 0 ? m.Id : "(none)",
                    m == null ? "Not installed" : m.Source switch { ModSource.Official => "Game", ModSource.Local => "Mods folder", _ => "Workshop" },
                    m == null ? "" : string.Join(", ", m.SupportedVersions), "",
                };
                if (filter.Length > 0 && !cells.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase))) return;
                var it = new ListViewItem(cells) { Tag = m, Name = key, Checked = active, UseItemStyleForSubItems = false };
                var tip = new List<string>();
                if (m == null) tip.Add("In the mod list, but not installed. RimWorld skips it.");
                else
                {
                    tip.Add(m.Folder);
                    if (!m.Supports(version)) tip.Add($"Does not list RimWorld {GameVersion.Short(version!)} as supported.");
                    tip.AddRange(m.Issues);
                }
                it.ToolTipText = string.Join("\n", tip);
                _dpRows.Add(it);
            }
            foreach (var key in _dpActive) Add(key, byId.GetValueOrDefault(key), true);
            var active = _dpActive.ToHashSet();
            foreach (var m in _dpScan.Mods.Where(m => !active.Contains(m.Key)).DistinctBy(m => m.Key)) Add(m.Key, m, false);
            _dpList.Items.AddRange(_dpRows.ToArray());
            if (keep != null && _dpList.Items[keep] is { } again) { again.Selected = again.Focused = true; again.EnsureVisible(); }
            NumberDropPodRows();
        }
        finally { _dpList.EndUpdate(); _dpFilling = false; }
    }

    /// <summary>Load-order numbers, counts, colours and buttons after the active list changed.</summary>
    void NumberDropPodRows()
    {
        var pos = _dpActive.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i + 1);
        foreach (var it in _dpRows) it.SubItems[5].Text = pos.TryGetValue(it.Name, out var p) ? p.ToString() : "";
        if (_dpScan != null)
        {
            var missing = _dpActive.Count(k => !_dpScan.ById().ContainsKey(k));
            _dpCount.Text = $"{_dpScan.Mods.Count} installed, {_dpActive.Count} active" + (missing > 0 ? $" ({missing} not installed)" : "") + (_dpDirty ? "  ·  not saved" : "");
        }
        ColorDropPodRows();
        UpdateDropPodButtons();
    }

    /// <summary>Mods with a broken About.xml in the warning colour, missing ones as errors, mods for another game version muted.</summary>
    void ColorDropPodRows()
    {
        foreach (var it in _dpRows)
        {
            var m = it.Tag as ModManifest;
            var tone = m == null ? Tone.Error : m.Issues.Count > 0 ? Tone.Warn : !m.Supports(_dpScan?.Install.Version) ? Tone.Muted : Tone.Normal;
            foreach (ListViewItem.ListViewSubItem sub in it.SubItems)
            {
                sub.BackColor = _dpList.BackColor;
                sub.ForeColor = Theme.Of(tone);
            }
        }
    }

    void UpdateDropPodButtons()
    {
        _dpSave.Enabled = _dpUndo.Enabled = _dpDirty && _dpScan != null;
        _dpSort.Enabled = _dpScan != null && _dpActive.Count > 1;
    }

    void DropPodTicked(ListViewItem it)
    {
        if (_dpFilling) return;
        var on = _dpActive.Contains(it.Name);
        if (it.Checked == on) return;
        if (it.Checked) _dpActive.Add(it.Name);
        else _dpActive.Remove(it.Name);
        _dpDirty = true;
        NumberDropPodRows();
    }

    void MoveDropPodMod(int by)
    {
        if (_dpList.SelectedItems.Count == 0) return;
        var key = _dpList.SelectedItems[0].Name;
        var i = _dpActive.IndexOf(key);
        var j = i + by;
        if (i < 0 || j < 0 || j >= _dpActive.Count) return;
        (_dpActive[i], _dpActive[j]) = (_dpActive[j], _dpActive[i]);
        _dpDirty = true;
        FillDropPodList();
    }

    void ShowSortMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Sort by rules (keep my order where no rule decides)", null, (_, _) => SortDropPod(SortMode.Rules));
        menu.Items.Add("Sort by rules, A to Z where no rule decides", null, (_, _) => SortDropPod(SortMode.Alphabetical));
        Theme.StyleMenu(menu);
        menu.Show(_dpSort, new Point(0, _dpSort.Height));
    }

    void SortDropPod(SortMode mode)
    {
        if (_dpScan == null) return;
        SortRules? rules = null;
        string? rulesNote = null;
        if (_s.DropPodRulesFile.Length > 0)
        {
            try { rules = SortRules.Parse(File.ReadAllText(_s.DropPodRulesFile)); }
            catch (Exception e) { rulesNote = $"Your rules file was not used: {e.Message}"; }
        }
        var byId = _dpScan.ById();
        var installed = _dpActive.Where(byId.ContainsKey).Select(k => byId[k]).ToList();
        var result = LoadOrder.Sort(installed, _dpScan.Install.Version, mode, rules, _dpGame.SortTier);
        var sorted = result.Order.Select(m => m.Key).Concat(_dpActive.Where(k => !byId.ContainsKey(k))).ToList();
        _dpDirty |= !sorted.SequenceEqual(_dpActive);
        _dpActive = sorted;
        var notes = new List<string>();
        if (rulesNote != null) notes.Add(rulesNote);
        foreach (var c in result.Cycles)
            notes.Add($"These mods' rules go round in a loop, so they were kept together: {string.Join(", ", c.Mods.Select(m => m.DisplayName))} ({string.Join("; ", c.Edges.Take(4))}).");
        notes.Add(_dpDirty ? "Sorted. Press Save to game to use this order." : "Already in order.");
        ShowDropPodGame(string.Join("\n", notes));
        FillDropPodList();
    }

    /// <summary>The game's process when it is running (it writes ModsConfig.xml itself when it closes), or null.</summary>
    string? GameRunning() =>
        _dpGame.ProcessNames.FirstOrDefault(n => { try { return Process.GetProcessesByName(n).Length > 0; } catch { return false; } });

    bool WriteActiveToGame(IReadOnlyList<string> keys)
    {
        if (_dpScan == null) return false;
        if (GameRunning() != null)
        {
            ShowDropPodGame("RimWorld is running, and it writes its own mod list when it closes. Close it, then try again.");
            return false;
        }
        try { _dpGame.WriteActiveMods(_dpScan.Install, keys); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowDropPodGame("Could not write ModsConfig.xml: " + e.Message);
            return false;
        }
        return true;
    }

    void SaveDropPodToGame()
    {
        if (!WriteActiveToGame(_dpActive)) return;
        _dpDirty = false;
        _ = ScanDropPodAsync();
        Say("Saved the mod list to the game.");
    }

    bool ConfirmDiscardDropPod() =>
        MessageBox.Show(this, "The mod list has changes that are not saved to the game. Drop them?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    // ------------------------------------------------------------ modlists

    void FillModlists(string? select = null)
    {
        var lists = DropPodStore.All(_dpGame.AppId);
        _s.CurrentModlists.TryGetValue(_dpGame.AppId, out var current);
        select ??= _dpModlist.SelectedItem as string ?? current;
        _dpModlist.Items.Clear();
        foreach (var l in lists) _dpModlist.Items.Add(l.Name);
        if (select != null && _dpModlist.Items.IndexOf(select) is var i and >= 0) _dpModlist.SelectedIndex = i;
        else if (_dpModlist.Items.Count > 0) _dpModlist.SelectedIndex = 0;
    }

    Modlist? SelectedModlist() =>
        _dpModlist.SelectedItem is string name ? DropPodStore.All(_dpGame.AppId).FirstOrDefault(l => l.Name == name) : null;

    Modlist CurrentAsModlist(string name) =>
        Modlist.From(name, _dpGame.AppId, _dpScan?.Install.Version, _dpActive, _dpScan?.ById() ?? new(), _dpGame.ActiveKey);

    async Task SwitchModlistAsync()
    {
        if (_dpScan == null || SelectedModlist() is not { } list) return;
        if (_dpDirty && !ConfirmDiscardDropPod()) return;
        var (installed, missing) = list.Match(_dpScan, _dpGame);
        if (!WriteActiveToGame(installed)) return;
        _s.CurrentModlists[_dpGame.AppId] = list.Name;
        _s.Save();
        _dpDirty = false;
        await ScanDropPodAsync();
        ShowDropPodGame($"Switched to {list.Name}: {installed.Count} mods active." + (missing.Count == 0 ? "" :
            $" {missing.Count} {(missing.Count == 1 ? "is" : "are")} not installed and were left out: {string.Join(", ", missing.Take(8).Select(m => m.Name ?? m.Id))}{(missing.Count > 8 ? ", ..." : "")}."));
    }

    void SaveModlistAs()
    {
        if (_dpScan == null) return;
        var name = Prompt("Name for this modlist:", _dpModlist.SelectedItem as string ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (_dpModlist.Items.Contains(name) && MessageBox.Show(this, $"Replace the modlist \"{name}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        DropPodStore.Save(CurrentAsModlist(name));
        _s.CurrentModlists[_dpGame.AppId] = name;
        _s.Save();
        FillModlists(name);
        ShowDropPodGame($"Saved the modlist {name} ({_dpActive.Count} mods).");
    }

    void ShowModlistMenu()
    {
        var selected = _dpModlist.SelectedItem as string;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Import a modlist file (.json, or RimWorld's .rml)...", null, (_, _) => ImportModlist());
        menu.Items.Add("Export the active mods to a file...", null, (_, _) => ExportModlist()).Enabled = _dpScan != null;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy a share code of the active mods", null, (_, _) => CopyShareCode()).Enabled = _dpScan != null;
        menu.Items.Add("Add a modlist from a share code...", null, (_, _) => PasteShareCode());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(selected == null ? "Delete the modlist" : $"Delete the modlist \"{selected}\"", null, (_, _) => DeleteModlist()).Enabled = selected != null;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_s.DropPodRulesFile.Length == 0 ? "Use my own load-order rules file..." : "Rules file: " + Path.GetFileName(_s.DropPodRulesFile) + "  (change...)", null, (_, _) => PickRulesFile());
        if (_s.DropPodRulesFile.Length > 0) menu.Items.Add("Stop using the rules file", null, (_, _) => { _s.DropPodRulesFile = ""; _s.Save(); });
        Theme.StyleMenu(menu);
        menu.Show(_dpMore, new Point(0, _dpMore.Height));
    }

    /// <summary>Saves an imported list under its name, or "name (2)" when that is taken.</summary>
    void AddModlist(Modlist list)
    {
        var names = DropPodStore.All(_dpGame.AppId).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = list.Name.Trim().Length > 0 ? list.Name.Trim() : "Imported";
        for (var n = 2; names.Contains(name); n++) name = $"{list.Name.Trim()} ({n})";
        list.Name = name;
        list.AppId = _dpGame.AppId;
        DropPodStore.Save(list);
        FillModlists(name);
        var missing = _dpScan == null ? 0 : list.Match(_dpScan, _dpGame).Missing.Count;
        ShowDropPodGame($"Added the modlist {name} ({list.Mods.Count} mods" + (missing > 0 ? $", {missing} not installed here" : "") + "). Press Switch to it to use it.");
    }

    void ImportModlist()
    {
        using var dlg = new OpenFileDialog { Filter = "Modlists (*.json;*.rml)|*.json;*.rml|All files (*.*)|*.*", Title = "Import a modlist" };
        if (_dpScan != null) dlg.InitialDirectory = Path.Combine(_dpScan.Install.DataFolder, "ModLists");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { AddModlist(Modlist.Parse(File.ReadAllText(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName))); }
        catch (Exception e) when (e is FormatException or IOException) { ShowDropPodGame("Could not import it: " + e.Message); }
    }

    void ExportModlist()
    {
        var name = _dpModlist.SelectedItem as string ?? "My modlist";
        using var dlg = new SaveFileDialog { Filter = "Modlist (*.json)|*.json", FileName = name + ".json", Title = "Export the active mods" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, CurrentAsModlist(Path.GetFileNameWithoutExtension(dlg.FileName)).ToJson());
            ShowDropPodGame("Exported to " + dlg.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not export it: " + e.Message); }
    }

    void CopyShareCode()
    {
        var code = CurrentAsModlist(_dpModlist.SelectedItem as string ?? "Shared modlist").ToShareCode();
        try { Clipboard.SetText(code); }
        catch (Exception e) { ShowDropPodGame("Could not copy it: " + e.Message); return; }
        ShowDropPodGame($"Copied a share code for {_dpActive.Count} mods ({code.Length} characters). Paste it anywhere; others add it with More > Add a modlist from a share code.");
    }

    void PasteShareCode()
    {
        string clip = "";
        try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
        var code = Prompt("Paste the share code:", clip.TrimStart().StartsWith("DROPPOD", StringComparison.OrdinalIgnoreCase) ? clip.Trim() : "");
        if (string.IsNullOrWhiteSpace(code)) return;
        try { AddModlist(Modlist.FromShareCode(code)); }
        catch (FormatException e) { ShowDropPodGame(e.Message); }
    }

    void DeleteModlist()
    {
        if (_dpModlist.SelectedItem is not string name) return;
        if (MessageBox.Show(this, $"Delete the modlist \"{name}\"? The game's own mod list stays as it is.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        DropPodStore.Delete(name);
        FillModlists();
    }

    void PickRulesFile()
    {
        using var dlg = new OpenFileDialog { Filter = "Load-order rules (*.json)|*.json|All files (*.*)|*.*", Title = "Pick your load-order rules file" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var rules = SortRules.Parse(File.ReadAllText(dlg.FileName));
            _s.DropPodRulesFile = dlg.FileName;
            _s.Save();
            ShowDropPodGame($"Sorting now also uses {Path.GetFileName(dlg.FileName)} ({rules.Rules.Count} mods with rules).");
        }
        catch (Exception e) { ShowDropPodGame("That rules file can't be read: " + e.Message); }
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
