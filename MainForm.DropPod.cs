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
    readonly TextBox _dpFilter = new() { Width = 150, PlaceholderText = "Filter mods  (Ctrl+F)", AccessibleName = "Filter installed mods" };
    readonly ComboBox _dpModlist = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Modlist" };
    readonly ComboBox _dpInstance = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Instance" };
    readonly Label _dpGameText = new() { AutoSize = true, UseMnemonic = false, Tag = "fill" };
    readonly Label _dpCount = new Label { AutoSize = true, UseMnemonic = false }.Role(ThemeRole.Muted);
    readonly Label _dpNote = new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(900, 0), Visible = false };
    readonly List<ListViewItem> _dpRows = new();
    readonly ListView _dpProblems = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.None, AccessibleName = "Problems with the active mods" };
    readonly Label _dpProblemCount = new Label { AutoSize = true, UseMnemonic = false }.Role(ThemeRole.Muted);
    Button _dpSave = null!, _dpUndo = null!, _dpSort = null!, _dpMore = null!, _dpActivateDeps = null!, _dpDownload = null!, _dpUpdates = null!, _dpPlay = null!, _dpInstMore = null!;
    List<ModProblem> _dpFound = new();
    bool _dpBusy;
    LibraryScan? _dpScan;
    /// <summary>The active mods (ids as the game writes them) in load order, as edited here; saved to the game with Save to game.</summary>
    List<string> _dpActive = new();
    bool _dpScanning, _dpScannedOnce, _dpDirty, _dpFilling;

    static readonly string[] DropPodColumns = { "Name", "Author", "Package ID", "Source", "Game versions", "Load order", "Tag", "Note" };
    const int DropPodTagColumn = 6;

    InstanceStore DropPodInstances => new(Path.Combine(AppSettings.Folder, "droppod", "instances"));
    /// <summary>The normal game's install, before an instance's folders are put in; null until found.</summary>
    GameInstall? _dpUsual;
    /// <summary>The picked instance, or null for the normal game.</summary>
    GameInstance? _dpInst;
    bool _dpFillingInstances;

    ModlistStore DropPodStore => new(Path.Combine(AppSettings.Folder, "droppod", "modlists"));

    Control BuildDropPodPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 12, 14, 12), Visible = false };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 32));

        var pick = Btn("Game folder...", (_, _) => PickGameFolder());
        var rescan = SmallBtn("Rescan", (_, _) => _ = ScanDropPodAsync());
        var switchTo = Btn("Switch to it", async (_, _) => await SwitchModlistAsync());
        var saveAs = Btn("Save as...", (_, _) => SaveModlistAs());
        _dpMore = Btn("More ▾", (_, _) => ShowModlistMenu());
        Tip(pick, "Pick RimWorld's install folder by hand, when it isn't found through Steam");
        Tip(rescan, "Read every mod folder again (F5 on this tab)");
        Tip(switchTo, "Make this modlist the game's active mods (writes ModsConfig.xml; the old one is kept as a backup)");
        Tip(saveAs, "Save the active mods and their order as a named modlist");
        Tip(_dpMore, "Import or export a modlist file, share it as a code or a Steam collection, delete it, or pick your own load-order rules file");
        var newInst = Btn("New...", (_, _) => NewInstance());
        var saves = Btn("Saves...", async (_, _) => await ShowSavesAsync());
        Tip(saves, "See the saves of the picked instance (or the normal game), how each one's mods differ from the active ones, and use a save's mods");
        _dpPlay = Btn("Play", (_, _) => LaunchDropPod());
        var test = Btn("Test", (_, _) => { if (LaunchDropPod() is { } log) OpenDropPodLog(log, watch: true); });
        Tip(test, "Start RimWorld and watch its log: errors and warnings are grouped as they come, with the mod most likely at fault");
        _dpInstMore = Btn("More ▾", (_, _) => ShowInstanceMenu());
        _dpTools = SmallBtn("Tools ▾", (_, _) => ShowDropPodTools());
        Tip(_dpTools, "Find the broken mod, load a Steam collection, compare with a friend's list, the load order history, load times and cleaning up");
        Tip(_dpInstance, "The normal game, or an instance: a separate setup with its own options, mod list, saves and log");
        Tip(newInst, "Make a new instance");
        Tip(_dpPlay, "Start RimWorld with the picked instance (or normally)");
        Tip(_dpInstMore, "Export or import an instance, open its folder, or delete it");
        _dpInstance.SelectedIndexChanged += async (_, _) => await PickInstanceAsync();
        page.Controls.Add(Card("RimWorld", new Control[] { _dpTools, rescan }, Row(_dpGameText, pick), Row(Lbl("Instance"), _dpInstance, newInst, _dpPlay, test, saves, _dpInstMore),
            Row(Lbl("Modlist"), _dpModlist, switchTo, saveAs, _dpMore), Row(_dpNote)), 0, 0);

        for (var i = 0; i < DropPodColumns.Length; i++)
            _dpList.Columns.Add(Localizer.T(DropPodColumns[i]), (int)(new[] { 240, 140, 200, 90, 110, 80, 80, 200 }[i] * Theme.TextScale));
        // A RimWorld list can hold hundreds of mods, so it is rebuilt once typing pauses; clearing shows everything at once.
        var filterPause = new Debouncer(150, FillDropPodList);
        Disposed += (_, _) => filterPause.Dispose();
        _dpFilter.TextChanged += (_, _) => { if (_dpFilter.TextLength == 0) { filterPause.Cancel(); FillDropPodList(); } else filterPause.Poke(); };
        _dpFilter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _dpFilter.TextLength > 0) { _dpFilter.Text = ""; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { filterPause.Flush(); e.SuppressKeyPress = true; }
        };
        _dpList.ItemChecked += (_, e) => DropPodTicked(e.Item);
        _dpList.KeyDown += (_, e) =>
        {
            if (e.Alt && e.KeyCode is Keys.Up or Keys.Down) { MoveDropPodMod(e.KeyCode == Keys.Up ? -1 : 1); e.Handled = e.SuppressKeyPress = true; }
        };
        SetUpDropPodDragging();
        _dpSort = SmallBtn("Sort ▾", (_, _) => ShowSortMenu());
        var up = SmallBtn("Up", (_, _) => MoveDropPodMod(-1));
        var down = SmallBtn("Down", (_, _) => MoveDropPodMod(1));
        _dpUndo = SmallBtn("Undo changes", (_, _) => { DropPodEdit(); _dpActive = ActiveFromGame(); _dpDirty = false; FillDropPodList(); });
        _dpSave = SmallBtn("Save to game", (_, _) => { SaveDropPodToGame(); }).Role(ThemeRole.Primary);
        Tip(_dpSort, "Order the active mods by their dependencies and load rules (and your rules file), or A to Z where no rule decides");
        Tip(up, "Load the selected mod earlier (Alt+Up)");
        Tip(down, "Load the selected mod later (Alt+Down). You can also drag mods to a new place in the list");
        Tip(_dpUndo, "Go back to the game's current mod list (Ctrl+Z steps back one change at a time)");
        Tip(_dpSave, "Write the ticked mods, in this order, to the game's ModsConfig.xml (the old one is kept as a backup)");
        page.Controls.Add(Card("Mods", new Control[] { _dpCount, _dpSort, up, down, _dpUndo, _dpSave, _dpFilter }, BuildDropPodListArea()), 0, 1);
        ((Control)page.Controls[1]).Dock = DockStyle.Fill;

        _dpProblems.Columns.Add("", (int)(80 * Theme.TextScale));
        _dpProblems.Columns.Add("", (int)(900 * Theme.TextScale));
        _dpProblems.DoubleClick += (_, _) => SelectProblemMod();
        _dpActivateDeps = SmallBtn("Activate needed mods", (_, _) => ActivateDependencies());
        _dpDownload = SmallBtn("Download missing", async (_, _) => await DownloadMissingAsync());
        _dpUpdates = SmallBtn("Check for updates", async (_, _) => await CheckDropPodUpdatesAsync());
        var readLog = SmallBtn("Read the log", (_, _) => { if (_dpScan != null) OpenDropPodLog(_dpInst?.LogFile ?? _dpGame.LogFile(_dpScan.Install), watch: false); });
        var insight = SmallBtn("Insight", (_, _) => ShowInsight());
        Tip(insight, "Hints about the active mods: Harmony patches that may clash, patches on per-tick methods, mods heavy to load, and your rules file's notes");
        Tip(readLog, "Read the game's last log: errors and warnings grouped, with the mod most likely at fault");
        Tip(_dpActivateDeps, "Tick the installed mods the active ones need, each placed before the first mod that needs it");
        Tip(_dpDownload, "Get the missing dependencies that link a Workshop item: subscribed through Steam when it is running (Steam keeps them up to date), else downloaded with SteamCMD into RimWorld's Mods folder");
        Tip(_dpUpdates, "Ask Steam which installed Workshop mods have a newer version, and GitHub about Mods folder mods that link a GitHub repository");
        page.Controls.Add(Card("Problems", new Control[] { _dpProblemCount, _dpActivateDeps, _dpDownload, _dpUpdates, readLog, insight }, _dpProblems), 0, 2);
        ((Control)page.Controls[2]).Dock = DockStyle.Fill;
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
            _s.CurrentInstances.TryGetValue(_dpGame.AppId, out var instName);
            var inst = string.IsNullOrEmpty(instName) ? null : DropPodInstances.Get(_dpGame.AppId, instName);
            var (usual, scan) = await Task.Run(() =>
            {
                var steam = SteamLibraries.Find();
                var install = _dpGame.Detect(steam, gameFolder, dataFolder);
                if (install == null) return (null, null);
                return (install, ModLibrary.Scan(_dpGame, inst?.Apply(install, _dpGame, steam) ?? install));
            });
            if (_dpInst?.Name != inst?.Name) _dpEdits.Clear();
            _dpUsual = usual;
            _dpInst = inst;
            _dpScan = scan;
            // Frozen copies are only linked in while their instance plays; take out any a crash left behind.
            if (usual != null && GameRunning() == null) Freezer.Unlink(Launcher.ModsFolder(usual));
            FillInstances();
            _dpActive = ActiveFromGame();
            _dpDirty = false;
            ShowDropPodGame(null);
            FillModlists();
            FillDropPodList();
            ShowDropPodDetails();
            UpdateJumpList();
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
        _dpGameText.Text = $"RimWorld {i.Version ?? "(version unknown)"}  ·  {i.GameFolder}" + (_dpInst != null ? $"  ·  instance {_dpInst.Name}" : "");
        _dpGameText.ForeColor = Theme.Of(Tone.Normal);
        Tip(_dpGameText, $"Game: {i.GameFolder}\nWorkshop: {i.WorkshopFolder ?? "none"}\nConfig and saves: {i.DataFolder}");
        var notes = new List<string>(_dpScan.Notes);
        var dupes = _dpScan.Duplicates();
        if (dupes.Count > 0) notes.Add($"{dupes.Count} package ID{(dupes.Count == 1 ? " is" : "s are")} installed more than once: " + string.Join(", ", dupes.Take(5).Select(g => g.First().Id)));
        if (_dpScan.Active == null) notes.Add(_dpInst != null ? "This instance has no mod list yet: tick mods and press Save to game." : "No ModsConfig.xml yet: start RimWorld once so it writes its mod list.");
        if (_dpInst is { GameFolder.Length: > 0 } gi && !Directory.Exists(gi.GameFolder)) notes.Add($"This instance's game folder {gi.GameFolder} is gone, so it uses the usual install.");
        if (extra != null) notes.Add(extra);
        ShowNote(_dpNote, notes.Count == 0 ? null : string.Join("\n", notes));
    }

    void FillDropPodList()
    {
        var keep = (_dpList.SelectedItems.Count > 0 ? _dpList.SelectedItems[0].Name : null);
        var top = _dpList.TopItem?.Name; // kept, so a refill (undo, sort, rescan) doesn't jump back to the top
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
                    m == null ? "" : string.Join(", ", m.SupportedVersions), "", "", "",
                };
                if (DropPodNotes.Get(key) is { } note) { cells[DropPodTagColumn] = note.Color.Length > 0 ? "● " + Localizer.T(note.Color) : ""; cells[DropPodTagColumn + 1] = note.Note; }
                if (filter.Length > 0 && !cells.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase)) && !(DropPodNotes.Get(key)?.Color.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)) return;
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
            if (top != null && _dpList.Items[top] is { } topRow)
                try { _dpList.TopItem = topRow; } catch { } // only once the list has a handle
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
            var byId = _dpScan.ById(); // once, not once per active mod
            var missing = _dpActive.Count(k => !byId.ContainsKey(k));
            _dpCount.Text = $"{_dpScan.Mods.Count} installed, {_dpActive.Count} active" + (missing > 0 ? $" ({missing} not installed)" : "") + (_dpDirty ? "  ·  not saved" : "");
        }
        ColorDropPodRows();
        FindDropPodProblems();
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
            FillNoteCells(it);
        }
    }

    void UpdateDropPodButtons()
    {
        _dpSave.Enabled = _dpUndo.Enabled = _dpDirty && _dpScan != null && !_dpBusy;
        _dpSort.Enabled = _dpScan != null && _dpActive.Count > 1 && !_dpBusy;
        var installed = _dpScan?.ById();
        _dpActivateDeps.Enabled = !_dpBusy && installed != null && _dpFound.Any(p => p.Kind == ProblemKind.MissingDependency && p.WorkshopId == null && installed.ContainsKey(p.Other ?? ""));
        _dpDownload.Enabled = !_dpBusy && Problems.DownloadableFixes(_dpFound).Count > 0;
        _dpUpdates.Enabled = !_dpBusy && _dpScan != null;
        _dpTools.Enabled = !_dpBusy;
    }

    // ------------------------------------------------------------ problems, downloads and updates

    (string Path, DateTime Stamp, SortRules? Rules)? _dpRulesRead;

    /// <summary>Your load-order rules file, read again only when it changes (it is asked for on every tick and move).</summary>
    SortRules? DropPodRules()
    {
        var path = _s.DropPodRulesFile;
        if (path.Length == 0) return null;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { return null; }
        if (_dpRulesRead is { } r && r.Path == path && r.Stamp == stamp) return r.Rules;
        SortRules? rules;
        try { rules = SortRules.Parse(File.ReadAllText(path)); }
        catch { rules = null; }
        _dpRulesRead = (path, stamp, rules);
        return rules;
    }

    void FindDropPodProblems()
    {
        _dpFound = _dpScan == null ? new() : Problems.Find(_dpScan, _dpActive, _dpGame, DropPodRules());
        _dpProblems.BeginUpdate();
        _dpProblems.Items.Clear();
        foreach (var p in _dpFound)
        {
            var it = new ListViewItem(new[] { p.Level switch { ProblemLevel.Error => "Error", ProblemLevel.Warning => "Warning", _ => "Note" }, p.Message })
            { Tag = p, ToolTipText = p.Message, UseItemStyleForSubItems = true };
            it.ForeColor = Theme.Of(p.Level switch { ProblemLevel.Error => Tone.Error, ProblemLevel.Warning => Tone.Warn, _ => Tone.Muted });
            it.BackColor = _dpProblems.BackColor;
            _dpProblems.Items.Add(it);
        }
        _dpProblems.EndUpdate();
        var errors = _dpFound.Count(p => p.Level == ProblemLevel.Error);
        var warnings = _dpFound.Count(p => p.Level == ProblemLevel.Warning);
        _dpProblemCount.Text = _dpScan == null ? "" : _dpFound.Count == 0 ? "None found" : $"{errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}";
    }

    /// <summary>Double-clicking a problem selects its mod in the list above.</summary>
    void SelectProblemMod()
    {
        if (_dpProblems.SelectedItems.Count == 0 || _dpProblems.SelectedItems[0].Tag is not ModProblem p) return;
        if (_dpList.Items[p.Mod] is not { } row) return;
        _dpList.SelectedItems.Clear();
        row.Selected = row.Focused = true;
        row.EnsureVisible();
        _dpList.Focus();
    }

    void ActivateDependencies()
    {
        if (_dpScan == null) return;
        var before = _dpActive.Count;
        DropPodEdit();
        _dpActive = Problems.WithInactiveDependencies(_dpScan, _dpActive, _dpGame);
        _dpDirty |= _dpActive.Count != before;
        ShowDropPodGame($"Ticked {_dpActive.Count - before} mod{(_dpActive.Count - before == 1 ? "" : "s")} the active ones need. Press Save to game to use the list.");
        FillDropPodList();
    }

    async Task DownloadMissingAsync()
    {
        if (_dpScan == null) return;
        var keep = _dpActive;
        await DownloadWorkshopAsync(Problems.DownloadableFixes(_dpFound), () => Problems.WithInactiveDependencies(_dpScan!, keep, _dpGame));
    }

    /// <summary>
    /// Gets Workshop items: through the Steam client as subscriptions when Steam is running (so Steam keeps them up to
    /// date), else with SteamCMD into the Mods folder. Then rescans and sets the active mods to what
    /// <paramref name="activeAfter"/> gives on the new scan (unsaved, so the player can look first).
    /// </summary>
    async Task DownloadWorkshopAsync(IReadOnlyList<string> ids, Func<List<string>> activeAfter)
    {
        if (_dpScan == null || ids.Count == 0) return;
        var note = "";
        if (SteamRestart.ReadState().Running)
        {
            _dpBusy = true;
            UpdateDropPodButtons();
            ShowDropPodGame($"Subscribing to {ids.Count} Workshop item{(ids.Count == 1 ? "" : "s")} through the Steam client and waiting for Steam to download them...");
            ExtrasResult r;
            try { r = await WorkshopExtras.ApplyOnSteamAsync(new ExtrasRequest { AppId = _dpGame.AppId, Subscribe = ids.ToArray(), WaitSeconds = Subscriptions.WaitSeconds(ids.Count) }, CancellationToken.None); }
            finally { _dpBusy = false; }
            if (r.Ok && r.Subscribed.Count > 0)
            {
                await RescanWithActive(activeAfter);
                ShowDropPodGame(Subscriptions.Describe(ids.Count, r.Subscribed.Count, r.Installed.Count));
                return;
            }
            note = $"Subscribing through Steam didn't work ({(r.Ok ? "Steam subscribed to nothing" : r.Error)}), so SteamCMD is used instead. ";
        }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd) || _s.Account.Length == 0)
        {
            ShowDropPodGame(note + "Start Steam to subscribe to them, or set up SteamCMD and a saved Steam login of an account that owns RimWorld under Options > Account to download them.");
            return;
        }
        var mods = SafeFiles.Child(_dpScan.Install.GameFolder, "Mods", directory: true) ?? Path.Combine(_dpScan.Install.GameFolder, "Mods");
        _dpBusy = true;
        UpdateDropPodButtons();
        var record = new DownloadRecord(Path.Combine(AppSettings.Folder, "droppod", "downloads.json"));
        var done = new List<string>();
        var failed = new List<string>();
        try
        {
            foreach (var id in ids)
            {
                ShowDropPodGame($"Downloading Workshop item {id} with SteamCMD ({done.Count + failed.Count + 1} of {ids.Count})...");
                try
                {
                    await WorkshopUpdates.InstallAsync(_s.EffectiveSteamCmd, _s.Account, _dpGame.AppId, id, mods, record, null, CancellationToken.None);
                    done.Add(id);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{id} ({e.Message})"); }
            }
        }
        finally { _dpBusy = false; }
        if (!await RescanWithActive(activeAfter)) return;
        ShowDropPodGame(note + $"Downloaded {done.Count} of {ids.Count} into the Mods folder and ticked them." + (failed.Count > 0 ? " Failed: " + string.Join("; ", failed) : "") +
                        " Press Save to game to use the list. These copies don't update by themselves; Tools > Turn downloaded copies into subscriptions hands them to Steam.");
    }

    /// <summary>Reads the mods again and makes the active list what <paramref name="activeAfter"/> gives on the new scan. False when RimWorld is gone.</summary>
    async Task<bool> RescanWithActive(Func<List<string>> activeAfter)
    {
        _dpDirty = false; // the rescan below rereads the game's list; put the edits back after it
        await ScanDropPodAsync();
        if (_dpScan == null) return false;
        _dpActive = activeAfter();
        _dpDirty = true;
        FillDropPodList();
        return true;
    }

    async Task CheckDropPodUpdatesAsync()
    {
        if (_dpScan == null) return;
        _dpBusy = true;
        UpdateDropPodButtons();
        try
        {
            ShowDropPodGame("Asking Steam about the installed Workshop mods...");
            var withId = _dpScan.Mods.Where(m => m.WorkshopId != null && m.Source != ModSource.Official).ToList();
            var remote = await WorkshopStatus.FetchAsync(withId.Select(m => m.WorkshopId!));
            if (remote.Count == 0 && withId.Count > 0) { ShowDropPodGame("Steam could not be reached, so updates were not checked."); return; }
            var local = new Dictionary<string, DateTimeOffset>(WorkshopUpdates.SteamClientTimes(_dpScan.Install.WorkshopFolder, _dpGame.AppId));
            foreach (var (id, t) in new DownloadRecord(Path.Combine(AppSettings.Folder, "droppod", "downloads.json")).Times) local.TryAdd(id, t);
            var outdated = WorkshopUpdates.Outdated(withId, remote, local);
            var gone = withId.Where(m => remote.TryGetValue(m.WorkshopId!, out var r) && !r.Public).Select(m => m.DisplayName).ToList();
            var lines = new List<string>
            {
                outdated.Count == 0 ? $"All {withId.Count} Workshop mods are up to date." :
                $"{outdated.Count} have a newer version on Steam: " + string.Join(", ", outdated.Take(10).Select(u => $"{u.Mod.DisplayName} ({u.Remote.ToLocalTime():d MMM})")) + (outdated.Count > 10 ? ", ..." : "") +
                (outdated.Any(u => u.Mod.Source == ModSource.Workshop) ? ". The Steam client updates subscribed mods itself; if it hasn't, restart Steam." : "."),
            };
            if (gone.Count > 0) lines.Add($"{gone.Count} are no longer public on Steam (removed or made private): {string.Join(", ", gone.Take(8))}.");
            if (outdated.Count > 0) await ShowUpdateNotesAsync(outdated);
            var fromGitHub = GitHubModUpdates.Candidates(_dpScan.Mods);
            List<GitHubModUpdate> gh = new();
            if (fromGitHub.Count > 0)
            {
                ShowDropPodGame($"Asking GitHub about {fromGitHub.Count} Mods folder mod{(fromGitHub.Count == 1 ? "" : "s")} that link a GitHub repository...");
                gh = await GitHubModUpdates.CheckAsync(fromGitHub, GitHubHttp, CancellationToken.None);
                lines.Add(gh.Count == 0 ? $"The {fromGitHub.Count} mod{(fromGitHub.Count == 1 ? "" : "s")} from GitHub {(fromGitHub.Count == 1 ? "is" : "are")} up to date."
                    : $"{gh.Count} mod{(gh.Count == 1 ? " has" : "s have")} a newer release on GitHub.");
            }
            ShowDropPodGame(string.Join("\n", lines));
            if (gh.Count > 0) await OfferGitHubUpdatesAsync(gh);
        }
        finally { _dpBusy = false; UpdateDropPodButtons(); }
    }

    static readonly HttpClient GitHubHttp = UpdateCheck.CreateClient(TimeSpan.FromMinutes(5));

    /// <summary>Lists mods with a newer GitHub release; the ticked ones are downloaded and put in place, each old copy zipped first.</summary>
    async Task OfferGitHubUpdatesAsync(List<GitHubModUpdate> updates)
    {
        var backups = Path.Combine(AppSettings.Folder, "droppod", "mod-backups");
        using var w = new ListWindow("Updates on GitHub",
            "These Mods folder mods have a newer release on GitHub. Tick the ones to update: each is downloaded from its release and put in place, " +
            "and the old copy is zipped into Modwright's mod-backups folder first.",
            new[] { ("Mod", 260), ("Installed", 90), ("On GitHub", 90), ("Release", 380) }, checkBoxes: true);
        foreach (var u in updates)
            w.AddRow(u, Tone.Normal, u.Mod.DisplayName, u.Mod.ModVersion.Length > 0 ? u.Mod.ModVersion : "?", u.Release.Version, u.Release.Page).Checked = true;
        w.DoubleClicked = tag => { if (tag is GitHubModUpdate u && u.Release.Page.Length > 0) OpenUrl(u.Release.Page); };
        var go = false;
        w.AddButton("&Update the ticked ones", () => { go = true; w.Close(); }, needsRow: true, primary: true);
        w.AddButton("Open the &backups", () => OpenFolder(backups));
        w.AddClose();
        w.ShowDialog(this);
        if (!go || w.Ticked.Count == 0) return;
        if (GameRunning() != null) { ShowDropPodGame("Close RimWorld first; its mods can't be replaced while it runs."); return; }
        var done = 0;
        var failed = new List<string>();
        foreach (var u in w.Ticked.OfType<GitHubModUpdate>())
        {
            ShowDropPodGame($"Updating {u.Mod.DisplayName} to {u.Release.Version} from GitHub...");
            try { await GitHubModUpdates.InstallAsync(u, GitHubHttp, d => _dpGame.ReadMod(d, ModSource.Local), backups, CancellationToken.None); done++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or TaskCanceledException) { failed.Add($"{u.Mod.DisplayName} ({e.Message})"); }
        }
        var keep = _dpActive.ToList();
        var wasDirty = _dpDirty;
        await RescanWithActive(() => keep);
        _dpDirty = wasDirty;
        ShowDropPodGame($"Updated {done} mod{(done == 1 ? "" : "s")} from GitHub." + (failed.Count > 0 ? " Failed: " + string.Join("; ", failed) : ""));
    }

    void DropPodTicked(ListViewItem it)
    {
        if (_dpFilling) return;
        var on = _dpActive.Contains(it.Name);
        if (it.Checked == on) return;
        DropPodEdit();
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
        DropPodEdit();
        (_dpActive[i], _dpActive[j]) = (_dpActive[j], _dpActive[i]);
        _dpDirty = true;
        // The two rows trade places where they are, so the list doesn't redraw from the top and holding Alt+Down
        // walks a mod down smoothly. A filter can hide the neighbour; then the list is filled again.
        var moved = _dpList.Items[key];
        var other = _dpList.Items[_dpActive[i]];
        if (moved == null || other == null || Math.Abs(moved.Index - other.Index) != 1) { FillDropPodList(); return; }
        var to = other.Index;
        _dpFilling = true;
        _dpList.BeginUpdate();
        try
        {
            _dpList.Items.Remove(moved);
            _dpList.Items.Insert(to, moved);
            moved.Selected = moved.Focused = true;
            moved.EnsureVisible();
        }
        finally { _dpList.EndUpdate(); _dpFilling = false; }
        NumberDropPodRows();
    }

    // ------------------------------------------------------------ drag to reorder

    readonly Panel _dpDropLine = new() { Height = 2, Visible = false, Enabled = false };
    (string? Target, bool After) _dpDropAt;

    /// <summary>Drag one or more selected mods to a new place in the load order; a line shows where they land. Dragging an inactive mod into the list activates it there.</summary>
    void SetUpDropPodDragging()
    {
        _dpList.AllowDrop = true;
        _dpList.Controls.Add(_dpDropLine);
        _dpList.ItemDrag += (_, e) =>
        {
            if (_dpBusy || _dpBisect != null || _dpList.SelectedItems.Count == 0) return;
            var keys = _dpList.SelectedItems.Cast<ListViewItem>().Select(i => i.Name).ToList();
            _dpList.DoDragDrop(new DataObject("ModwrightDropPodMods", keys), DragDropEffects.Move);
            _dpDropLine.Visible = false;
        };
        _dpList.DragOver += (_, e) =>
        {
            if (e.Data?.GetData("ModwrightDropPodMods") is not List<string>) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move;
            var pt = _dpList.PointToClient(new Point(e.X, e.Y));
            // Scroll when the mouse is near the top or bottom edge.
            if (_dpList.TopItem is { } top)
            {
                if (pt.Y < top.Bounds.Height && top.Index > 0) _dpList.Items[top.Index - 1].EnsureVisible();
                else if (pt.Y > _dpList.ClientSize.Height - top.Bounds.Height && _dpList.GetItemAt(4, _dpList.ClientSize.Height - 2) is { } last && last.Index + 1 < _dpList.Items.Count)
                    _dpList.Items[last.Index + 1].EnsureVisible();
            }
            var row = _dpList.GetItemAt(4, pt.Y);
            if (row == null || !row.Checked)
            {
                // Past the active mods: they go to the end of the load order, shown under the last active row.
                var lastActive = _dpList.Items.Cast<ListViewItem>().LastOrDefault(i => i.Checked);
                _dpDropAt = (null, true);
                ShowDropLine(lastActive, below: true);
                return;
            }
            var after = pt.Y > row.Bounds.Top + row.Bounds.Height / 2;
            _dpDropAt = (row.Name, after);
            ShowDropLine(row, after);
        };
        _dpList.DragLeave += (_, _) => _dpDropLine.Visible = false;
        _dpList.DragDrop += (_, e) =>
        {
            _dpDropLine.Visible = false;
            if (e.Data?.GetData("ModwrightDropPodMods") is not List<string> keys || keys.Count == 0) return;
            var moved = LoadOrderEdit.Move(_dpActive, keys, _dpDropAt.Target, _dpDropAt.After);
            if (moved.SequenceEqual(_dpActive)) return;
            DropPodEdit();
            _dpActive = moved;
            _dpDirty = true;
            FillDropPodList();
            _dpList.SelectedItems.Clear();
            foreach (var k in keys)
                if (_dpList.Items[k] is { } it) it.Selected = true;
            if (_dpList.Items[keys[0]] is { } first) { first.Focused = true; first.EnsureVisible(); }
        };
    }

    void ShowDropLine(ListViewItem? row, bool below)
    {
        if (row == null) { _dpDropLine.Visible = false; return; }
        _dpDropLine.BackColor = Theme.FocusColor;
        _dpDropLine.SetBounds(0, (below ? row.Bounds.Bottom : row.Bounds.Top) - 1, _dpList.ClientSize.Width, 2);
        _dpDropLine.Visible = true;
        _dpDropLine.BringToFront();
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
        if (!sorted.SequenceEqual(_dpActive)) DropPodEdit();
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

    /// <param name="label">What to call this list in the load-order history; null keeps it out (the broken-mod search's rounds).</param>
    bool WriteActiveToGame(IReadOnlyList<string> keys, string? label)
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
        if (label != null) RecordDropPodHistory(keys, label);
        return true;
    }

    bool SaveDropPodToGame()
    {
        if (!WriteActiveToGame(_dpActive, "Save to game")) return false;
        _dpDirty = false;
        _ = ScanDropPodAsync();
        Say("Saved the mod list to the game.");
        return true;
    }

    /// <summary>
    /// When the mod list has unsaved changes: Yes saves them, No leaves them, Cancel stops. False means stop (Cancel,
    /// or the save failed); <paramref name="question"/> is what to ask.
    /// </summary>
    bool OfferSaveDropPod(string question)
    {
        if (!_dpDirty || _dpScan == null) return true;
        var answer = MessageBox.Show(this, question, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || answer == DialogResult.Yes && SaveDropPodToGame();
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
        if (!WriteActiveToGame(installed, "Switched to " + list.Name)) return;
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
        menu.Items.Add("Export the active mods with their mod settings (.zip)...", null, (_, _) => ExportWithSettings()).Enabled = _dpScan != null;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy a share code of the active mods", null, (_, _) => CopyShareCode()).Enabled = _dpScan != null;
        menu.Items.Add("Add a modlist from a share code...", null, (_, _) => PasteShareCode());
        menu.Items.Add("Send the active mods to a Steam collection...", null, (_, _) => SendToCollection()).Enabled = _dpScan != null;
        menu.Items.Add("Load a Steam collection as a modlist...", null, async (_, _) => await LoadCollectionAsync()).Enabled = _dpScan != null && !_dpBusy;
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
        using var dlg = new OpenFileDialog { Filter = "Modlists (*.json;*.rml;*.zip)|*.json;*.rml;*.zip|All files (*.*)|*.*", Title = "Import a modlist" };
        if (_dpScan != null) dlg.InitialDirectory = Path.Combine(_dpScan.Install.DataFolder, "ModLists");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (dlg.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ImportWithSettings(dlg.FileName);
            else AddModlist(Modlist.Parse(File.ReadAllText(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName)));
        }
        catch (Exception e) when (e is FormatException or IOException or InvalidDataException) { ShowDropPodGame("Could not import it: " + e.Message); }
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

    void SendToCollection()
    {
        if (_dpScan == null) return;
        var (items, leftOut) = SteamCollections.Contents(_dpScan, _dpActive, _dpGame);
        if (items.Count == 0)
        {
            ShowDropPodGame("None of the active mods are on the Steam Workshop, so there is nothing to put in a collection.");
            return;
        }
        var name = _dpModlist.SelectedItem as string ?? "My modlist";
        var key = SteamCollections.Key(_dpGame.AppId, name);
        using var f = new CollectionForm(_dpGame.AppId, name, items, leftOut, _s.SteamCollections.GetValueOrDefault(key));
        f.ShowDialog(this);
        if (f.CollectionId == null) return;
        _s.SteamCollections[key] = f.CollectionId;
        _s.Save();
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

    // ------------------------------------------------------------ instances

    void FillInstances()
    {
        _dpFillingInstances = true;
        try
        {
            _dpInstance.Items.Clear();
            _dpInstance.Items.Add("Normal game");
            foreach (var i in DropPodInstances.All(_dpGame.AppId)) _dpInstance.Items.Add(i.Name);
            var at = _dpInst == null ? 0 : _dpInstance.Items.IndexOf(_dpInst.Name);
            _dpInstance.SelectedIndex = at < 0 ? 0 : at;
        }
        finally { _dpFillingInstances = false; }
        _dpPlay.Enabled = _dpScan != null;
        _dpInstMore.Enabled = true;
    }

    async Task PickInstanceAsync()
    {
        if (_dpFillingInstances) return;
        var name = _dpInstance.SelectedIndex <= 0 ? "" : _dpInstance.SelectedItem as string ?? "";
        if (name == (_dpInst?.Name ?? "")) return;
        if (_dpDirty && !ConfirmDiscardDropPod()) { FillInstances(); return; }
        _dpDirty = false;
        _s.CurrentInstances[_dpGame.AppId] = name;
        _s.Save();
        await ScanDropPodAsync();
    }

    void NewInstance()
    {
        if (_dpUsual == null) { ShowDropPodGame("Find RimWorld first: instances run its install."); return; }
        using var f = new InstanceForm("RimWorld", DropPodStore.All(_dpGame.AppId).Select(l => l.Name));
        if (f.ShowDialog(this) != DialogResult.OK) return;
        IReadOnlyList<string>? mods = f.ModsChoice switch
        {
            0 => _dpActive.ToList(),
            1 => _dpScan?.Mods.Where(m => m.Source == ModSource.Official).OrderBy(_dpGame.SortTier).Select(m => m.Key).Distinct().ToList() ?? new List<string> { "ludeon.rimworld" },
            _ => f.ModlistName is { } n && DropPodStore.All(_dpGame.AppId).FirstOrDefault(l => l.Name == n) is { } list && _dpScan != null ? list.Match(_dpScan, _dpGame).Installed : null,
        };
        if (f.GameFolder.Length > 0 && !Directory.Exists(f.GameFolder)) { ShowDropPodGame("That game folder does not exist."); return; }
        try
        {
            var inst = DropPodInstances.Create(_dpGame, _dpUsual, f.InstanceName, f.GameFolder, f.CopySettings ? _dpUsual.DataFolder : null, mods);
            _dpDirty = false;
            _s.CurrentInstances[_dpGame.AppId] = inst.Name;
            _s.Save();
            _ = ScanDropPodAsync();
            Say($"Made the instance {inst.Name}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not make the instance: " + e.Message); }
    }

    void ShowInstanceMenu()
    {
        var menu = new ContextMenuStrip();
        var inst = _dpInst;
        menu.Items.Add(inst == null ? "Open the game's config and saves folder" : $"Open the folder of \"{inst.Name}\"", null, (_, _) =>
            OpenFolder(inst?.Folder ?? _dpScan?.Install.DataFolder)).Enabled = inst != null || _dpScan != null;
        menu.Items.Add(inst == null ? "Launch options for the normal game..." : $"Launch options for \"{inst.Name}\"...", null, (_, _) => EditLaunchOptions());
        if (inst != null)
            menu.Items.Add(Freezer.IsFrozen(inst) ? "Unfreeze its Workshop mods..." : "Freeze its Workshop mods (Steam updates can't change them)...", null, async (_, _) => await FreezeInstanceAsync()).Enabled = !_dpBusy && _dpScan != null;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Export this instance to a zip...", null, (_, _) => ExportInstance()).Enabled = inst != null;
        menu.Items.Add("Import an instance from a zip...", null, (_, _) => ImportInstance());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(inst == null ? "Delete the instance" : $"Delete the instance \"{inst.Name}\"...", null, (_, _) => DeleteInstance()).Enabled = inst != null;
        Theme.StyleMenu(menu);
        menu.Show(_dpInstMore, new Point(0, _dpInstMore.Height));
    }

    void OpenFolder(string? dir)
    {
        if (dir == null) return;
        try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
        catch (Exception e) { ShowDropPodGame("Could not open it: " + e.Message); }
    }

    void ExportInstance()
    {
        if (_dpInst is not { } inst) return;
        var saves = InstanceStore.SaveCount(inst);
        var withSaves = saves > 0 && MessageBox.Show(this, $"Put its {saves} save{(saves == 1 ? "" : "s")} in the zip too?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        using var dlg = new SaveFileDialog { Filter = "Instance (*.zip)|*.zip", FileName = SafeFiles.SafeName(inst.Name, "instance") + ".zip", Title = "Export the instance" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            InstanceStore.Export(inst, dlg.FileName, withSaves);
            ShowDropPodGame("Exported to " + dlg.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not export it: " + e.Message); }
    }

    void ImportInstance()
    {
        using var dlg = new OpenFileDialog { Filter = "Instance (*.zip)|*.zip", Title = "Import an instance" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var inst = DropPodInstances.Import(dlg.FileName, _dpGame.AppId);
            if (_dpDirty && !ConfirmDiscardDropPod()) { FillInstances(); ShowDropPodGame($"Imported the instance {inst.Name}."); return; }
            _dpDirty = false;
            _s.CurrentInstances[_dpGame.AppId] = inst.Name;
            _s.Save();
            _ = ScanDropPodAsync();
            Say($"Imported the instance {inst.Name}.");
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or InvalidDataException) { ShowDropPodGame("Could not import it: " + e.Message); }
    }

    void DeleteInstance()
    {
        if (_dpInst is not { } inst) return;
        var saves = InstanceStore.SaveCount(inst);
        if (MessageBox.Show(this, $"Delete the instance \"{inst.Name}\"" + (saves > 0 ? $" and its {saves} save{(saves == 1 ? "" : "s")}" : "") + "? This can't be undone. The mods and the normal game stay as they are.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { DropPodInstances.Delete(inst); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not delete it: " + e.Message); return; }
        _dpDirty = false;
        _s.CurrentInstances[_dpGame.AppId] = "";
        _s.Save();
        _ = ScanDropPodAsync();
    }

    async Task ShowSavesAsync()
    {
        if (_dpScan is not { } scan) return;
        var active = _dpActive.ToList();
        using var f = new SavesForm(_dpInst != null ? "the instance " + _dpInst.Name : "the normal game",
            () => Saves.List(_dpGame, scan.Install), s => Saves.Compare(s, active, scan, _dpGame));
        if (f.ShowDialog(this) != DialogResult.OK || f.Picked is not { } save || _dpScan != scan) return;
        if (f.AsModlist)
        {
            AddModlist(Modlist.Parse(save.Mods.ToJson(), save.Name));
            return;
        }
        var (installed, missing) = save.Mods.Match(scan, _dpGame);
        DropPodEdit();
        _dpActive = installed;
        _dpDirty = true;
        FillDropPodList();
        var fetchable = missing.Where(m => m.WorkshopId != null).Select(m => m.WorkshopId!).Distinct().ToList();
        var note = $"The active mods now match the save {save.Name}" + (missing.Count == 0 ? "." : $", except {missing.Count} that {(missing.Count == 1 ? "is" : "are")} not installed: {string.Join(", ", missing.Take(8).Select(m => m.Name ?? m.Id))}{(missing.Count > 8 ? ", ..." : "")}.") + " Press Save to game to use the list.";
        ShowDropPodGame(note);
        if (fetchable.Count == 0 || MessageBox.Show(this, $"{fetchable.Count} of the missing mods are on the Steam Workshop. Download them with SteamCMD into the Mods folder?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await DownloadWorkshopAsync(fetchable, () => save.Mods.Match(_dpScan!, _dpGame).Installed);
    }

    void ShowInsight()
    {
        if (_dpScan is not { } scan) return;
        var byId = scan.ById();
        var active = _dpActive.Where(byId.ContainsKey).ToList();
        var rules = DropPodRules();
        string Name(string key) => byId.TryGetValue(key, out var m) ? m.DisplayName : key;
        using var f = new InsightForm(ct =>
        {
            var mods = active.Select(k => byId[k]).ToList();
            var patches = HarmonyPatches.ForMods(mods, ct);
            var weights = mods.Where(m => m.Source != ModSource.Official).Select(m => { ct.ThrowIfCancellationRequested(); return Insight.Weigh(m); }).ToList();
            var xml = _dpGame is DropPod.RimWorld.RimWorldModule
                ? Insight.Xml(DropPod.RimWorld.XmlPatchSim.Run(XmlMods(mods), scan.Install.Version, ct), Name, mods.Where(m => m.Source == ModSource.Official).Select(m => m.Key).ToList())
                : new();
            return xml.Where(i => i.Level == ProblemLevel.Warning).Concat(Insight.Conflicts(patches, active, Name)).Concat(Insight.TickPatches(patches, active, Name))
                .Concat(xml.Where(i => i.Level != ProblemLevel.Warning))
                .Concat(Insight.Heavy(weights, active, Name)).Concat(Insight.Notes(rules, active, Name)).ToList();
        }, Name);
        f.ShowDialog(this);
    }

    /// <summary>Mods (in load order) as the XML simulation takes them.</summary>
    static List<DropPod.RimWorld.XmlMod> XmlMods(IEnumerable<ModManifest> mods) =>
        mods.Select(m => new DropPod.RimWorld.XmlMod(m.Key, m.DisplayName, m.Id, m.Folder, m.Source == ModSource.Official)).ToList();

    /// <summary>Shows the game's log analysed (modeless, so the game and the Drop Pod stay usable); with <paramref name="watch"/> it follows the log as it grows.</summary>
    void OpenDropPodLog(string path, bool watch)
    {
        if (_dpScan is not { } scan) return;
        ModIndex? index = null;
        var byId = scan.ById();
        var f = new LogForm(path,
            (p, ct) => LogAnalysis.Read(p, index ??= new ModIndex(scan.Mods, ModAssemblies.ForMods(scan.Mods, ct)), ct),
            key => byId.TryGetValue(key, out var m) ? m.DisplayName : key,
            key =>
            {
                if (_dpScan != scan || !_dpActive.Contains(key)) return;
                DropPodEdit();
                _dpActive.Remove(key);
                _dpDirty = true;
                FillDropPodList();
            },
            watch,
            issue =>
            {
                if (issue.Mods.FirstOrDefault() is not { } b || !byId.TryGetValue(b.Mod, out var m)) return null;
                var body = BugReport.Text(issue, m, scan.Install.Version, _dpActive.Count);
                return (BugReport.Target(m, $"{issue.Title} (RimWorld {GameVersion.Short(scan.Install.Version ?? "")})", body), body);
            });
        f.Show(this);
    }

    /// <summary>
    /// Starts the game with the picked instance's folders (or normally), its launch options and, for a frozen instance,
    /// its frozen mods. Returns the log file it will write, or null when it did not start.
    /// </summary>
    /// <param name="checkSave">Offer a copy of the newest save when this list differs from its mods (off during the broken-mod search).</param>
    string? LaunchDropPod(bool checkSave = true)
    {
        if (_dpScan == null) return null;
        // Yes saves and starts in one go; No starts with the list the game already has.
        if (!OfferSaveDropPod("The mod list has changes that are not saved to the game. Save them before starting RimWorld?\n\n" +
                "Yes saves and starts, No starts with the game's current list.")) return null;
        if (GameRunning() != null) { ShowDropPodGame("RimWorld is already running."); return null; }
        if (checkSave && !OfferSaveBackup()) return null;
        var psi = Launcher.StartInfo(_dpGame, _dpScan.Install, _dpInst, _dpInst?.LaunchArgs ?? _s.DropPodLaunchArgs);
        if (psi == null) { ShowDropPodGame("RimWorld's program was not found in " + _dpScan.Install.GameFolder + "."); return null; }
        var log = _dpInst?.LogFile ?? _dpGame.LogFile(_dpScan.Install);
        Process game;
        try
        {
            game = Launcher.Start(psi, _dpGame, _dpScan.Install, _dpInst, note =>
            {
                if (note.Length == 0) return;
                try { BeginInvoke(() => ShowDropPodGame(note)); } catch (InvalidOperationException) { }
            });
        }
        catch (Exception e) { ShowDropPodGame("Could not start RimWorld: " + e.Message); return null; }
        var byId = _dpScan.ById();
        MeasureLoadTime(game, log, DropPodSetupName, ActiveFromGame(), _dpActive.Where(byId.ContainsKey).ToDictionary(k => k, k => byId[k].DisplayName));
        Say(_dpInst != null ? $"Started RimWorld with the instance {_dpInst.Name}." : "Started RimWorld.");
        return log;
    }

    /// <summary>Opens the Drop Pod tab (for --shot).</summary>
    public void ShowDropPod() => ShowPage(_dropPodPage);
}
