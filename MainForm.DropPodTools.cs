using System.Diagnostics;
using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// The Drop Pod's tools beyond the list itself: undo and the load-order history, why a mod sits where it does, notes and
/// colours, the details panel, finding the broken mod, Steam collections in, comparing with a friend, cleaning up,
/// mod settings with modlists, update notes, save backups, launch options, freezing an instance and load times.
/// The logic lives in DropPod/; this file shows it.
/// </summary>
public sealed partial class MainForm
{
    readonly EditUndo _dpEdits = new();
    readonly DropPodDetailsPanel _dpDetails = new() { Dock = DockStyle.Fill };
    SplitContainer _dpSplit = null!;
    Button _dpTools = null!;
    ModNotes? _dpNotesStore;
    BisectForm? _dpBisect;

    ModNotes DropPodNotes => _dpNotesStore ??= new ModNotes(Path.Combine(AppSettings.Folder, "droppod", "notes.json"));
    OrderHistory DropPodHistory => new(OrderHistory.FolderFor(Path.Combine(AppSettings.Folder, "droppod", "history"), _dpGame.AppId, _dpInst?.Name));
    string SaveBackupFolder => Path.Combine(AppSettings.Folder, "droppod", "save-backups", _dpInst == null ? "Normal game" : SafeFiles.SafeName(_dpInst.Name, "instance"));
    LoadTimes DropPodLoadTimes => new(Path.Combine(AppSettings.Folder, "droppod", "loadtimes.json"));
    /// <summary>What the current setup is called in records: the picked instance, or the normal game with its modlist.</summary>
    string DropPodSetupName => _dpInst != null ? "Instance " + _dpInst.Name : _s.CurrentModlists.TryGetValue(_dpGame.AppId, out var l) && !string.IsNullOrEmpty(l) ? l : "Normal game";

    /// <summary>The list and the details panel side by side (the panel can be dragged narrower or wider).</summary>
    Control BuildDropPodListArea()
    {
        // Panel2MinSize is set once the split is wide enough (FitDropPodSplit): set here, on the new 150-wide split, it throws.
        _dpSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6 };
        _dpSplit.Panel1.Controls.Add(_dpList);
        _dpSplit.Panel2.Controls.Add(_dpDetails);
        _dpSplit.SplitterMoved += (_, _) =>
        {
            if (!_dpSplit.IsHandleCreated || _dpSplit.Width < 400) return;
            _s.DropPodDetailsWidth = _dpSplit.Width - _dpSplit.SplitterDistance;
        };
        _dpSplit.HandleCreated += (_, _) => FitDropPodSplit();
        _dpSplit.Resize += (_, _) => FitDropPodSplit();
        Tip(_dpSplit, "Drag to give the mod list or the details more room");
        _dpDetails.AddAction("Workshop page", "Open the mod's Steam Workshop page", _tip, m => OpenUrl(SteamCollections.Url(m.WorkshopId!)), m => m.WorkshopId != null);
        _dpDetails.AddAction("Folder", "Open the mod's folder", _tip, m => OpenFolder(m.Folder));
        _dpDetails.AddAction("Why here?", "Why the mod sits where it does in the load order", _tip, _ => ExplainSelected(), m => _dpActive.Contains(_dpGame.ActiveKey(m.Key)));
        _dpDetails.AddAction("Note...", "Your own note and colour on this mod", _tip, _ => EditNote());
        _dpList.SelectedIndexChanged += (_, _) => QueueDropPodDetails();
        _dpList.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { DropPodUndo(redo: false); e.Handled = e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Y) { DropPodUndo(redo: true); e.Handled = e.SuppressKeyPress = true; }
        };
        _dpList.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowDropPodRowMenu(e.Location); };
        _dpProblems.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowDropPodProblemMenu(e.Location); };
        return _dpSplit;
    }

    void FitDropPodSplit()
    {
        if (_dpSplit.Width < 400) return;
        try { if (_dpSplit.Panel2MinSize != 160) _dpSplit.Panel2MinSize = 160; } catch (InvalidOperationException) { }
        var want = _s.DropPodDetailsWidth > 0 ? _s.DropPodDetailsWidth : (int)(320 * Theme.TextScale);
        // Keep the list at least 360 wide; when the window is too narrow for both, the panel gives way.
        var dist = Math.Clamp(_dpSplit.Width - want, Math.Min(360, _dpSplit.Width - 170), _dpSplit.Width - 170);
        try { if (dist > 0 && _dpSplit.SplitterDistance != dist) _dpSplit.SplitterDistance = dist; } catch (InvalidOperationException) { }
    }

    // ------------------------------------------------------------ details, notes and colours

    bool _dpDetailsQueued;

    /// <summary>Shows the details once the selection settles, so moving between rows doesn't blink through the summary.</summary>
    void QueueDropPodDetails()
    {
        if (_dpDetailsQueued) return;
        if (!IsHandleCreated) { ShowDropPodDetails(); return; }
        _dpDetailsQueued = true;
        BeginInvoke(() =>
        {
            _dpDetailsQueued = false;
            if (!IsDisposed) ShowDropPodDetails();
        });
    }

    void ShowDropPodDetails()
    {
        if (_dpScan is not { } scan) { _dpDetails.ShowSummary("No mods yet", Array.Empty<string>()); return; }
        var key = _dpList.SelectedItems.Count > 0 ? _dpList.SelectedItems[0].Name : null;
        if (key == null || _dpList.SelectedItems[0].Tag is not ModManifest m)
        {
            var byId = scan.ById();
            var lines = new List<string>
            {
                $"{scan.Mods.Count} installed, {_dpActive.Count} active",
                $"{scan.Mods.Count(x => x.Source == ModSource.Workshop)} from the Workshop, {scan.Mods.Count(x => x.Source == ModSource.Local)} in the Mods folder, {scan.Mods.Count(x => x.Source == ModSource.Official)} from the game",
            };
            var missing = _dpActive.Count(k => !byId.ContainsKey(k));
            if (missing > 0) lines.Add($"{missing} in the list but not installed");
            if (_dpInst != null && Freezer.IsFrozen(_dpInst)) lines.Add($"This instance's Workshop mods are frozen ({Freezer.Read(_dpInst).Count}).");
            lines.Add("");
            lines.Add("Pick a mod to see more. Ctrl+Z undoes a change to the list, Ctrl+Y redoes it.");
            _dpDetails.ShowSummary(key == null ? "The Drop Pod" : key + " (not installed)", lines);
            return;
        }
        var all = scan.ById();
        var version = scan.Install.Version;
        var facts = new List<string>();
        if (m.Authors.Count > 0) facts.Add("By " + string.Join(", ", m.Authors));
        facts.Add((m.ModVersion.Length > 0 ? "Version " + m.ModVersion + "  ·  " : "") + m.Source switch { ModSource.Official => "From the game", ModSource.Local => FolderLinks.IsLink(m.Folder) ? "Mods folder (linked)" : "Mods folder", _ => "Workshop " + m.WorkshopId });
        if (m.Id.Length > 0) facts.Add("Package ID " + m.Id);
        if (m.SupportedVersions.Count > 0) facts.Add("Game versions " + string.Join(", ", m.SupportedVersions) + (m.Supports(version) ? "" : $"  (not {GameVersion.Short(version!)})"));
        var pos = _dpActive.IndexOf(_dpGame.ActiveKey(m.Key));
        facts.Add(pos >= 0 ? $"Active, #{pos + 1} in the load order" : "Not active");
        var needs = m.Dependencies.For(version).Select(d => all.TryGetValue(_dpGame.ActiveKey(d.Id), out var x) ? x.DisplayName : (d.DisplayName ?? d.Id) + " (not installed)").ToList();
        if (needs.Count > 0) facts.Add("Needs " + string.Join(", ", needs));
        var neededBy = scan.Mods.DistinctBy(x => x.Key).Where(x => x.Dependencies.For(version).Any(d => _dpGame.ActiveKey(d.Id) == _dpGame.ActiveKey(m.Key))).Select(x => x.DisplayName).ToList();
        if (neededBy.Count > 0) facts.Add("Needed by " + string.Join(", ", neededBy.Take(8)) + (neededBy.Count > 8 ? $" and {neededBy.Count - 8} more" : ""));
        var lists = DropPodStore.All(_dpGame.AppId).Where(l => l.Mods.Any(e => _dpGame.ActiveKey(e.Id) == _dpGame.ActiveKey(m.Key))).Select(l => l.Name).ToList();
        if (lists.Count > 0) facts.Add("In modlists " + string.Join(", ", lists));
        if (m.Issues.Count > 0) facts.Add("Problems: " + string.Join("; ", m.Issues));
        var n = DropPodNotes.Get(m.Key);
        _dpDetails.ShowMod(m, facts, n == null ? null : (n.Color.Length > 0 ? $"[{n.Color}] " : "") + n.Note);
    }

    static Color TagColor(string name) => name switch
    {
        "Red" => Color.FromArgb(0xE0, 0x4F, 0x4F), "Orange" => Color.FromArgb(0xF0, 0x8A, 0x24), "Yellow" => Color.FromArgb(0xD8, 0xB4, 0x1E),
        "Green" => Color.FromArgb(0x4C, 0xAF, 0x50), "Blue" => Color.FromArgb(0x3D, 0x8B, 0xFD), "Purple" => Color.FromArgb(0xA0, 0x6C, 0xD5), _ => Theme.Of(Tone.Normal),
    };

    /// <summary>The Tag and Note cells of a row, from the notes file.</summary>
    void FillNoteCells(ListViewItem it)
    {
        var n = DropPodNotes.Get(it.Name);
        it.SubItems[DropPodTagColumn].Text = n?.Color is { Length: > 0 } c ? "● " + Localizer.T(c) : "";
        it.SubItems[DropPodTagColumn].ForeColor = n?.Color is { Length: > 0 } cc ? TagColor(cc) : it.SubItems[0].ForeColor;
        it.SubItems[DropPodTagColumn + 1].Text = n?.Note ?? "";
    }

    void EditNote()
    {
        if (_dpList.SelectedItems.Count == 0) return;
        var key = _dpList.SelectedItems[0].Name;
        var cur = DropPodNotes.Get(key);
        using var f = new Form { Text = "Note and colour", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, 150), Font = Font, ShowInTaskbar = false };
        var l = new Label { Text = "Your note on " + (_dpList.SelectedItems[0].Text) + ":", AutoSize = true, Location = new Point(12, 12), UseMnemonic = false };
        var t = new TextBox { Text = cur?.Note ?? "", Location = new Point(12, 36), Width = 396, AccessibleName = "Note" };
        var cl = new Label { Text = "&Colour", AutoSize = true, Location = new Point(12, 72) };
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(70, 68), Width = 140, AccessibleName = "Colour" };
        c.Items.Add(Localizer.T("None"));
        foreach (var col in ModNotes.Colors) c.Items.Add(Localizer.T(col));
        c.SelectedIndex = cur?.Color is { Length: > 0 } cc ? Array.IndexOf(ModNotes.Colors, cc) + 1 : 0;
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(252, 110), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(333, 110), Width = 75 };
        f.Controls.AddRange(new Control[] { l, t, cl, c, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        ok.Role(ThemeRole.Accent);
        Theme.Apply(f);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        DropPodNotes.Set(key, t.Text, c.SelectedIndex <= 0 ? "" : ModNotes.Colors[c.SelectedIndex - 1]);
        if (_dpList.Items[key] is { } row) FillNoteCells(row);
        ShowDropPodDetails();
    }

    // ------------------------------------------------------------ undo and history

    /// <summary>Call before changing the active list, so Ctrl+Z can bring it back.</summary>
    void DropPodEdit() => _dpEdits.Push(_dpActive);

    void DropPodUndo(bool redo)
    {
        var next = redo ? _dpEdits.Redo(_dpActive) : _dpEdits.Undo(_dpActive);
        if (next == null) { ShowDropPodGame(redo ? "Nothing to redo." : "Nothing to undo."); return; }
        _dpActive = next;
        _dpDirty = !_dpActive.SequenceEqual(ActiveFromGame());
        FillDropPodList();
        ShowDropPodGame((redo ? "Redone." : "Undone.") + (_dpDirty ? " Press Save to game to use the list." : ""));
    }

    void RecordDropPodHistory(IReadOnlyList<string> keys, string label)
    {
        try { DropPodHistory.Record(keys, label); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // history is a nice-to-have; saving still worked
    }

    void ShowDropPodHistory()
    {
        if (_dpScan is not { } scan) return;
        var byId = scan.ById();
        string Name(string k) => byId.TryGetValue(k, out var m) ? m.DisplayName : k;
        var snaps = DropPodHistory.All();
        using var w = new ListWindow("Load order history",
            $"Every list the Drop Pod saved to the game for {(_dpInst != null ? "the instance " + _dpInst.Name : "the normal game")}, newest first (the last 100). " +
            "Use this list puts it in the Mods list to look at; press Save to game there to use it.",
            new[] { ("When", 140), ("What", 220), ("Mods", 60), ("Compared with now", 260) },
            detailsOf: o => o is OrderSnapshot s ? string.Join("\n", s.Active.Select((k, i) => $"{i + 1}. {Name(k)}")) : "");
        foreach (var s in snaps)
            w.AddRow(s, Tone.Normal, s.At.ToString("g"), s.Label, s.Active.Count.ToString(), OrderHistory.Compare(s.Active, _dpActive).ToString());
        if (snaps.Count == 0) w.Intro += "\n\nNothing yet: the history starts with the next Save to game, switch or sort you save.";
        OrderSnapshot? picked = null;
        w.AddButton("&Use this list", () => { picked = w.Selected as OrderSnapshot; w.Close(); }, needsRow: true, primary: true);
        w.DoubleClicked = o => { picked = o as OrderSnapshot; w.Close(); };
        w.AddClose();
        w.ShowDialog(this);
        if (picked == null) return;
        DropPodEdit();
        _dpActive = picked.Active.ToList();
        _dpDirty = !_dpActive.SequenceEqual(ActiveFromGame());
        FillDropPodList();
        ShowDropPodGame($"The list from {picked.At:g} is back ({OrderHistory.Compare(ActiveFromGame(), _dpActive)} compared with the game's). Press Save to game to use it; Ctrl+Z undoes this.");
    }

    // ------------------------------------------------------------ menus

    void ShowDropPodRowMenu(Point at)
    {
        if (_dpList.SelectedItems.Count == 0 || _dpScan == null) return;
        var row = _dpList.SelectedItems[0];
        var m = row.Tag as ModManifest;
        var active = _dpActive.Contains(row.Name);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Why is this mod here?", null, (_, _) => ExplainSelected()).Enabled = active && m != null;
        menu.Items.Add("Note and colour...", null, (_, _) => EditNote());
        AddIgnoreDuplicateItem(menu, row.Name);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open its folder", null, (_, _) => OpenFolder(m?.Folder)).Enabled = m != null;
        menu.Items.Add("Open its Workshop page", null, (_, _) => OpenUrl(SteamCollections.Url(m!.WorkshopId!))).Enabled = m?.WorkshopId != null;
        menu.Items.Add("Open its change notes on Steam", null, (_, _) => OpenUrl(UpdateNotes.Url(m!.WorkshopId!))).Enabled = m?.WorkshopId != null;
        Theme.StyleMenu(menu);
        menu.Show(_dpList, at);
    }

    /// <summary>Right-click on a problem: the same "installed more than once" switch as on the mod's row.</summary>
    void ShowDropPodProblemMenu(Point at)
    {
        if (_dpProblems.SelectedItems.Count == 0 || _dpProblems.SelectedItems[0].Tag is not ModProblem p || p.Kind != ProblemKind.Duplicate) return;
        var menu = new ContextMenuStrip();
        AddIgnoreDuplicateItem(menu, p.Mod);
        Theme.StyleMenu(menu);
        menu.Show(_dpProblems, at);
    }

    /// <summary>
    /// "Ignore installed twice": for a mod you make, the copy in the Mods folder and your own Workshop subscription are
    /// both meant to be there, so the warning can be switched off for it and stays off. Only offered for mods that are
    /// installed more than once (or already ignored, to switch it back on).
    /// </summary>
    void AddIgnoreDuplicateItem(ContextMenuStrip menu, string key)
    {
        var ignored = _s.DropPodIgnoreDuplicates.Contains(key);
        if (!ignored && _dpScan?.Duplicates().Any(g => g.Key == key) != true) return;
        var item = new ToolStripMenuItem("Ignore \"installed more than once\" for this mod") { Checked = ignored };
        item.Click += (_, _) =>
        {
            if (ignored) _s.DropPodIgnoreDuplicates.Remove(key);
            else _s.DropPodIgnoreDuplicates.Add(key);
            _s.Save();
            ShowDropPodGame(null);
            FindDropPodProblems();
        };
        menu.Items.Add(item);
    }

    void ShowDropPodTools()
    {
        var menu = new ContextMenuStrip();
        var scanned = _dpScan != null;
        menu.Items.Add("Find the broken mod for me...", null, (_, _) => StartBisect()).Enabled = scanned && !_dpBusy && _dpBisect == null;
        menu.Items.Add("Load a Steam collection as a modlist...", null, async (_, _) => await LoadCollectionAsync()).Enabled = scanned && !_dpBusy;
        menu.Items.Add("Compare with a friend's list...", null, (_, _) => CompareWithFriend()).Enabled = scanned;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Load order history...", null, (_, _) => ShowDropPodHistory()).Enabled = scanned;
        menu.Items.Add("Load times...", null, (_, _) => ShowLoadTimes());
        menu.Items.Add("Clean up unused mods and settings...", null, async (_, _) => await ShowCleanupAsync()).Enabled = scanned && !_dpBusy;
        menu.Items.Add("Turn downloaded copies into subscriptions...", null, async (_, _) => await ConvertDownloadedCopiesAsync()).Enabled = scanned && !_dpBusy;
        menu.Items.Add("Optimize textures...", null, async (_, _) => await OptimizeTexturesAsync()).Enabled = scanned && !_dpBusy;
        menu.Items.Add(new ToolStripSeparator());
        var backups = new ToolStripMenuItem("Back up the newest save before a risky start") { Checked = _s.DropPodSaveBackups, CheckOnClick = true };
        backups.CheckedChanged += (_, _) => { _s.DropPodSaveBackups = backups.Checked; _s.Save(); };
        menu.Items.Add(backups);
        menu.Items.Add("Open the save backups", null, (_, _) => OpenFolder(SaveBackupFolder));
        Theme.StyleMenu(menu);
        menu.Show(_dpTools, new Point(0, _dpTools.Height));
    }

    void ExplainSelected()
    {
        if (_dpScan is not { } scan || _dpList.SelectedItems.Count == 0) return;
        var key = _dpList.SelectedItems[0].Name;
        var byId = scan.ById();
        var mods = _dpActive.Where(byId.ContainsKey).Select(k => byId[k]).ToList();
        var at = mods.FindIndex(m => m.Key == key);
        if (at < 0) { ShowDropPodGame("That mod isn't active, so it has no place in the load order."); return; }
        var lines = LoadOrder.Explain(mods, at, scan.Install.Version, DropPodRules(), _dpGame.SortTier);
        MessageBox.Show(this, $"{mods[at].DisplayName} is #{at + 1} of {mods.Count}.\n\n" + string.Join("\n\n", lines), "Why is this mod here?", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------ find the broken mod

    void StartBisect()
    {
        if (_dpScan is not { } scan || _dpBisect != null) return;
        if (_dpDirty && !ConfirmDiscardDropPod()) return;
        var original = ActiveFromGame();
        if (original.Count < 2) { ShowDropPodGame("There are no mods to search through: tick some and save them to the game first."); return; }
        var b = Bisect.For(original, scan, _dpGame);
        if (b.Suspects.Count < 2) { ShowDropPodGame("Only one mod besides the game is active, so it's the one to look at."); return; }
        var byId = scan.ById();
        _dpBisect = new BisectForm(b, k => byId.TryGetValue(k, out var m) ? m.DisplayName : k,
            trial =>
            {
                if (!WriteActiveToGame(trial, null)) return false;
                _dpActive = trial.ToList();
                _dpDirty = false;
                FillDropPodList();
                return LaunchDropPod(checkSave: false) != null;
            },
            (culprit, untick) =>
            {
                var back = original.ToList();
                if (untick && culprit != null) back.Remove(culprit);
                if (WriteActiveToGame(back, untick ? "Found the broken mod" : null)) { _dpDirty = false; _ = ScanDropPodAsync(); }
            });
        _dpBisect.FormClosed += (_, _) => _dpBisect = null;
        _dpBisect.Show(this);
    }

    // ------------------------------------------------------------ Steam collections in

    async Task LoadCollectionAsync()
    {
        if (_dpScan is not { } scan) return;
        var text = Prompt("Steam collection link or id:", "");
        if (string.IsNullOrWhiteSpace(text)) return;
        if (SteamCollections.ParseId(text) is not { } id) { ShowDropPodGame("That isn't a Steam collection link (they look like steamcommunity.com/sharedfiles/filedetails/?id=123)."); return; }
        _dpBusy = true;
        UpdateDropPodButtons();
        Modlist list;
        try
        {
            ShowDropPodGame("Asking Steam what is in the collection...");
            var items = await CollectionImport.ItemsAsync(id);
            var details = await WorkshopStatus.FetchAsync(items.Append(id));
            var title = details.TryGetValue(id, out var c) && c.Title.Length > 0 ? c.Title : "Collection " + id;
            list = CollectionImport.ToModlist(title, _dpGame.AppId, items, scan, details.ToDictionary(kv => kv.Key, kv => kv.Value.Title));
        }
        catch (IOException e) { ShowDropPodGame(e.Message); return; }
        finally { _dpBusy = false; UpdateDropPodButtons(); }
        AddModlist(list);
        var (installed, missing) = list.Match(scan, _dpGame);
        var fetch = missing.Where(m => m.WorkshopId != null).Select(m => m.WorkshopId!).ToList();
        List<string> Sorted()
        {
            var by = _dpScan!.ById();
            var mods = list.Match(_dpScan, _dpGame).Installed.Select(k => by[k]).ToList();
            var official = _dpScan.Mods.Where(m => m.Source == ModSource.Official && !mods.Any(x => x.Key == m.Key)).DistinctBy(m => m.Key);
            return LoadOrder.Sort(official.Concat(mods).ToList(), _dpScan.Install.Version, SortMode.Rules, DropPodRules(), _dpGame.SortTier).Order.Select(m => m.Key).ToList();
        }
        if (fetch.Count > 0 && MessageBox.Show(this, $"{installed.Count} of the collection's {list.Mods.Count} mods are installed. Download the other {fetch.Count} with SteamCMD into the Mods folder, then make the collection the active list (sorted)?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            DropPodEdit();
            await DownloadWorkshopAsync(fetch, Sorted);
            return;
        }
        if (MessageBox.Show(this, $"Make the collection's {installed.Count} installed mods the active list now (with the game and DLC, sorted)? You can look before pressing Save to game.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        DropPodEdit();
        _dpActive = Sorted();
        _dpDirty = true;
        FillDropPodList();
        ShowDropPodGame($"The active list is now the collection {list.Name}" + (missing.Count > 0 ? $" ({missing.Count} not installed)" : "") + ". Press Save to game to use it.");
    }

    // ------------------------------------------------------------ comparing with a friend

    void CompareWithFriend()
    {
        if (_dpScan is not { } scan) return;
        string clip = "";
        try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
        var lists = DropPodStore.All(_dpGame.AppId);
        var code = Prompt("Paste your friend's share code (or type the name of one of your modlists):", clip.TrimStart().StartsWith("DROPPOD", StringComparison.OrdinalIgnoreCase) ? clip.Trim() : "");
        if (string.IsNullOrWhiteSpace(code)) return;
        Modlist theirs;
        if (lists.FirstOrDefault(l => l.Name.Equals(code.Trim(), StringComparison.CurrentCultureIgnoreCase)) is { } own) theirs = own;
        else
            try { theirs = Modlist.FromShareCode(code); }
            catch (FormatException e) { ShowDropPodGame(e.Message); return; }
        var d = Saves.Compare(theirs, _dpActive, scan, _dpGame);
        var versions = PlayTogether.Versions(theirs, scan, _dpGame);
        var byId = scan.ById();
        using var w = new ListWindow("Compare with " + theirs.Name,
            d.Matches && versions.Count == 0 ? "Your active mods match this list exactly." :
            "How your active mods differ from this list. The Multiplayer mod needs the same mods, versions and order on every PC." + (d.OrderDiffers ? " The mods you share are in another order." : ""),
            new[] { ("Difference", 170), ("Mod", 300), ("Yours", 90), ("Theirs", 90) });
        foreach (var e in d.Missing) w.AddRow(e, Tone.Error, "Not installed here", e.Name ?? e.Id, "", e.Version ?? "");
        foreach (var e in d.Inactive) w.AddRow(e, Tone.Warn, "Installed, not active", byId.TryGetValue(_dpGame.ActiveKey(e.Id), out var mm) ? mm.DisplayName : e.Name ?? e.Id, "", e.Version ?? "");
        foreach (var k in d.Extra) w.AddRow(null, Tone.Warn, "Only you have it", byId.TryGetValue(k, out var x) ? x.DisplayName : k, "", "");
        foreach (var v in versions) w.AddRow(null, Tone.Warn, "Another version", v.Name, v.Mine, v.Theirs);
        var make = false;
        var fetch = d.Missing.Where(e => e.WorkshopId != null).Select(e => e.WorkshopId!).Distinct().ToList();
        w.AddButton("&Make mine match", () => { make = true; w.Close(); }, primary: true).Enabled = !d.Matches;
        w.AddClose();
        w.ShowDialog(this);
        if (!make) return;
        DropPodEdit();
        _dpActive = theirs.Match(scan, _dpGame).Installed;
        _dpDirty = true;
        FillDropPodList();
        ShowDropPodGame($"The active mods now follow {theirs.Name}" + (d.Missing.Count > 0 ? $", except {d.Missing.Count} not installed here" : "") + ". Press Save to game to use the list." +
            (versions.Count > 0 ? $" {versions.Count} mod{(versions.Count == 1 ? " has" : "s have")} another version than theirs; the one who is behind should update." : ""));
        if (fetch.Count > 0 && MessageBox.Show(this, $"{fetch.Count} of the missing mods are on the Steam Workshop. Download them with SteamCMD into the Mods folder?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            _ = DownloadWorkshopAsync(fetch, () => theirs.Match(_dpScan!, _dpGame).Installed);
    }

    // ------------------------------------------------------------ cleaning up

    async Task ShowCleanupAsync()
    {
        if (_dpScan is not { } scan || _dpUsual is not { } usual) return;
        ShowDropPodGame("Looking at what your modlists, instances and saves use...");
        var lists = DropPodStore.All(_dpGame.AppId);
        var instances = DropPodInstances.All(_dpGame.AppId);
        var steam = await Task.Run(SteamLibraries.Find);
        var (unused, orphans) = await Task.Run(() =>
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            void Add(IEnumerable<string>? keys) { if (keys != null) foreach (var k in keys) used.Add(_dpGame.ActiveKey(k)); }
            Add(_dpGame.ReadActiveMods(usual));
            Add(_dpActive);
            foreach (var l in lists) Add(l.Mods.Select(e => e.Id));
            foreach (var i in instances) Add(_dpGame.ReadActiveMods(i.Apply(usual, _dpGame, steam)));
            foreach (var save in Saves.List(_dpGame, usual).Take(10)) Add(save.Mods.Mods.Select(e => e.Id));
            // A modlist from a collection may name items by Workshop id only.
            var byWorkshop = scan.Mods.Where(m => m.WorkshopId != null).GroupBy(m => m.WorkshopId!).ToDictionary(g => g.Key, g => g.First().Key);
            foreach (var l in lists) foreach (var e in l.Mods) if (e.WorkshopId != null && byWorkshop.TryGetValue(e.WorkshopId, out var k)) used.Add(k);
            return (Hangar.Unused(scan, used, _dpGame), Hangar.Orphans(usual.DataFolder, scan));
        });
        ShowDropPodGame(null);
        var total = unused.Sum(u => u.Bytes) + orphans.Sum(o => o.Bytes);
        using var w = new ListWindow("Clean up",
            unused.Count + orphans.Count == 0 ? "Nothing to clean up: every installed mod is in a modlist, an instance or a recent save, and every mod settings file has its mod." :
            $"{unused.Count} mod{(unused.Count == 1 ? "" : "s")} no modlist, instance or recent save uses, and {orphans.Count} settings file{(orphans.Count == 1 ? "" : "s")} left by mods that are gone ({Hangar.Size(total)} in all). " +
            "Tick what to remove: Workshop mods are unsubscribed through the Steam client (Steam must be running); Mods folder copies and settings files go to the Recycle Bin.",
            new[] { ("What", 150), ("Name", 330), ("Size", 80), ("Where", 260) }, checkBoxes: true);
        foreach (var u in unused) w.AddRow(u, Tone.Normal, u.Mod.Source == ModSource.Workshop ? "Workshop mod" : FolderLinks.IsLink(u.Mod.Folder) ? "Linked mod" : "Mods folder copy", u.Mod.DisplayName, Hangar.Size(u.Bytes), u.Mod.Folder);
        foreach (var o in orphans) w.AddRow(o, Tone.Muted, "Settings file", o.ModFolder, Hangar.Size(o.Bytes), o.File);
        var go = false;
        w.AddButton("&Remove the ticked ones", () => { go = true; w.Close(); }, needsRow: true, primary: true);
        w.AddClose();
        w.ShowDialog(this);
        if (!go) return;
        var picked = w.Ticked;
        var workshop = picked.OfType<UnusedMod>().Where(u => u.Mod.Source == ModSource.Workshop && u.Mod.WorkshopId != null).Select(u => u.Mod.WorkshopId!).ToArray();
        var files = picked.OfType<UnusedMod>().Where(u => u.Mod.Source != ModSource.Workshop).Select(u => u.Mod.Folder).ToList();
        var settings = picked.OfType<OrphanSettings>().Select(o => o.File).ToList();
        if (MessageBox.Show(this, $"Remove {picked.Count} item{(picked.Count == 1 ? "" : "s")}?" + (workshop.Length > 0 ? $" {workshop.Length} Workshop mod{(workshop.Length == 1 ? " is" : "s are")} unsubscribed." : ""),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var notes = new List<string>();
        var recycled = 0;
        foreach (var dir in files)
        {
            try
            {
                if (FolderLinks.IsLink(dir)) FolderLinks.Remove(dir); // only the link; the folder it points at stays
                else Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                recycled++;
            }
            catch (Exception e) { notes.Add($"{Path.GetFileName(dir)}: {e.Message}"); }
        }
        foreach (var f in settings)
        {
            try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); recycled++; }
            catch (Exception e) { notes.Add($"{Path.GetFileName(f)}: {e.Message}"); }
        }
        if (workshop.Length > 0)
        {
            ShowDropPodGame($"Unsubscribing {workshop.Length} Workshop mod{(workshop.Length == 1 ? "" : "s")} through the Steam client...");
            var r = await WorkshopExtras.ApplyOnSteamAsync(new ExtrasRequest { AppId = _dpGame.AppId, Unsubscribe = workshop }, CancellationToken.None);
            notes.Add(r.Ok ? $"Unsubscribed {r.Done.Count} Workshop mod{(r.Done.Count == 1 ? "" : "s")}; Steam removes their files." : "Unsubscribing: " + r.Error);
        }
        _dpDirty = false;
        await ScanDropPodAsync();
        ShowDropPodGame($"Moved {recycled} to the Recycle Bin." + (notes.Count > 0 ? " " + string.Join(" ", notes) : ""));
    }

    /// <summary>
    /// Tools > Turn downloaded copies into subscriptions: Workshop mods SteamCMD put in the Mods folder are subscribed
    /// through Steam, and each copy goes to the Recycle Bin once Steam has its own. The mod list doesn't change, since
    /// both copies have the same id.
    /// </summary>
    async Task ConvertDownloadedCopiesAsync()
    {
        if (_dpScan is not { } scan) return;
        var copies = Subscriptions.DownloadedCopies(scan);
        if (copies.Count == 0) { ShowDropPodGame("No Mods folder copies of Workshop mods to hand to Steam: every Workshop mod here is already subscribed."); return; }
        if (!SteamRestart.ReadState().Running) { ShowDropPodGame("Start Steam and sign in with the account that owns RimWorld first; subscribing goes through the Steam client."); return; }
        if (GameRunning() != null) { ShowDropPodGame("Close RimWorld first; its mods can't be moved while it runs."); return; }
        if (MessageBox.Show(this,
                $"{copies.Count} mod{(copies.Count == 1 ? "" : "s")} in the Mods folder came from the Workshop and won't update by themselves:\n\n{string.Join("\n", copies.Take(12).Select(c => c.DisplayName))}{(copies.Count > 12 ? "\n..." : "")}\n\n" +
                "Subscribe to them through Steam, and move each Mods folder copy to the Recycle Bin once Steam has downloaded it? Your mod list stays the same.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!OfferSaveDropPod("The mod list has changes that are not saved to the game. Save them first?\n\nYes saves, No keeps them unsaved.")) return;
        var keep = _dpActive.ToList();
        _dpBusy = true;
        UpdateDropPodButtons();
        ShowDropPodGame($"Subscribing to {copies.Count} Workshop mod{(copies.Count == 1 ? "" : "s")} through the Steam client and waiting for the downloads...");
        ExtrasResult r;
        try
        {
            r = await WorkshopExtras.ApplyOnSteamAsync(new ExtrasRequest
            {
                AppId = _dpGame.AppId, Subscribe = copies.Select(c => c.WorkshopId!).Distinct().ToArray(), WaitSeconds = Subscriptions.WaitSeconds(copies.Count),
            }, CancellationToken.None);
        }
        finally { _dpBusy = false; }
        if (!r.Ok) { ShowDropPodGame("Subscribing: " + r.Error); return; }
        var notes = new List<string>();
        var moved = 0;
        foreach (var c in Subscriptions.Replaced(copies, r.Installed))
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(c.Folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                moved++;
            }
            catch (Exception e) { notes.Add($"{c.FolderName}: {e.Message}"); }
        }
        await RescanWithActive(() => keep);
        var waiting = r.Subscribed.Count - r.Installed.Count;
        ShowDropPodGame($"Subscribed to {r.Subscribed.Count} and moved {moved} Mods folder cop{(moved == 1 ? "y" : "ies")} to the Recycle Bin." +
                        (waiting > 0 ? $" Steam is still downloading {waiting}; their Mods folder copies stay until you run this again." : "") +
                        (notes.Count > 0 ? " " + string.Join(" ", notes) : ""));
    }

    TextureOptimizer Textures => new(Path.Combine(AppSettings.Folder, "droppod", "textures"));

    /// <summary>
    /// Tools > Optimize textures: the active mods with PNG textures, biggest first; the ticked ones get a DDS beside
    /// each PNG, which RimWorld loads instead (faster, far less video memory). Mods done before can be undone here.
    /// </summary>
    async Task OptimizeTexturesAsync()
    {
        if (_dpScan is not { } scan) return;
        var byId = scan.ById();
        var active = _dpActive.Where(byId.ContainsKey).Select(k => byId[k]).ToList();
        var tex = Textures;
        ShowDropPodGame("Looking for PNG textures in the active mods...");
        var (todo, done) = await Task.Run(() => (tex.Candidates(active), active.Where(m => tex.Made(m).Count > 0).ToList()));
        ShowDropPodGame(null);
        var version = scan.Install.Version is { } v ? GameVersion.Short(v) : "";
        using var w = new ListWindow("Optimize textures",
            "RimWorld 1.5 and later load a DDS texture instead of the PNG of the same name: it loads faster and takes far less video memory, " +
            "which matters most with big texture mods. Tick the mods to convert; each PNG gets a DDS beside it and the PNGs are not changed. " +
            "Textures whose sides aren't multiples of 4 stay PNG. Mods already done are listed at the end; tick them and press Undo to remove the DDS files again." +
            (version.Length > 0 && Version.TryParse(version, out var gv) && gv < new Version(1, 5) ? $" Your RimWorld is {version}, which doesn't read DDS files; this only helps from 1.5 on." : ""),
            new[] { ("Mod", 300), ("PNG files", 90), ("Size", 100), ("", 140) }, checkBoxes: true);
        foreach (var (m, files, bytes) in todo) w.AddRow(m, Tone.Normal, m.DisplayName, files.ToString("N0"), Hangar.Size(bytes), "to convert");
        foreach (var m in done.Where(d => todo.All(t => t.Mod != d))) w.AddRow(new UndoTextures(m), Tone.Muted, m.DisplayName, tex.Made(m).Count.ToString("N0"), "", "already converted");
        if (todo.Count + done.Count == 0) w.Intro = "None of the active mods has PNG textures left to convert.";
        string action = "";
        w.AddButton("&Convert the ticked ones", () => { action = "convert"; w.Close(); }, needsRow: true, primary: true);
        w.AddButton("&Undo the ticked ones", () => { action = "undo"; w.Close(); }, needsRow: true);
        w.AddClose();
        w.ShowDialog(this);
        var picked = w.Ticked;
        if (action.Length == 0 || picked.Count == 0) return;
        if (GameRunning() != null) { ShowDropPodGame("Close RimWorld first; it holds its textures open while it runs."); return; }
        if (action == "undo")
        {
            var mods = picked.Select(p => p is UndoTextures u ? u.Mod : (ModManifest)p).ToList();
            var removed = await Task.Run(() => mods.Sum(tex.Undo));
            ShowDropPodGame($"Removed {removed:N0} DDS file{(removed == 1 ? "" : "s")} Modwright had made; those mods use their PNGs again.");
            return;
        }
        _dpBusy = true;
        UpdateDropPodButtons();
        var results = new List<(ModManifest Mod, TextureResult R)>();
        try
        {
            foreach (var m in picked.OfType<ModManifest>())
            {
                var name = m.DisplayName;
                var r = await Task.Run(() => tex.Optimize(m, DecodePng, CancellationToken.None, (i, n) =>
                {
                    if (i % 25 != 0) return;
                    try { BeginInvoke(() => ShowDropPodGame($"Converting {name}'s textures: {i:N0} of {n:N0}...")); } catch (InvalidOperationException) { }
                }));
                results.Add((m, r));
            }
        }
        finally { _dpBusy = false; UpdateDropPodButtons(); }
        var converted = results.Sum(x => x.R.Converted);
        var skipped = results.Sum(x => x.R.Skipped);
        var errors = results.SelectMany(x => x.R.Errors.Select(e => $"{x.Mod.DisplayName}: {e}")).ToList();
        ShowDropPodGame($"Converted {converted:N0} texture{(converted == 1 ? "" : "s")} in {results.Count} mod{(results.Count == 1 ? "" : "s")} " +
                        $"({Hangar.Size(results.Sum(x => x.R.PngBytes))} of PNG, {Hangar.Size(results.Sum(x => x.R.DdsBytes))} of DDS; the graphics card now holds the compressed DDS instead of the unpacked PNG)." +
                        (skipped > 0 ? $" {skipped:N0} stay PNG (sides not multiples of 4)." : "") +
                        (errors.Count > 0 ? $" {errors.Count} couldn't be read: {string.Join("; ", errors.Take(3))}" : ""));
    }

    sealed record UndoTextures(ModManifest Mod);

    /// <summary>A PNG as RGBA pixels, top row first.</summary>
    static (byte[] Rgba, int Width, int Height) DecodePng(string path)
    {
        using var bmp = new Bitmap(path);
        var (w, h) = (bmp.Width, bmp.Height);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var px = new byte[w * h * 4];
            for (var y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w * 4, w * 4);
            for (var i = 0; i < px.Length; i += 4) (px[i], px[i + 2]) = (px[i + 2], px[i]); // BGRA to RGBA
            return (px, w, h);
        }
        finally { bmp.UnlockBits(data); }
    }

    // ------------------------------------------------------------ mod settings with modlists

    void ExportWithSettings()
    {
        if (_dpScan is not { } scan) return;
        var name = _dpModlist.SelectedItem as string ?? "My modlist";
        using var dlg = new SaveFileDialog { Filter = "Modlist with mod settings (*.zip)|*.zip", FileName = name + ".zip", Title = "Export the active mods with their settings" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var byId = scan.ById();
            var files = ModSettings.For(scan.Install.DataFolder, _dpActive.Where(byId.ContainsKey).Select(k => byId[k]));
            ModSettings.Pack(CurrentAsModlist(Path.GetFileNameWithoutExtension(dlg.FileName)), files, dlg.FileName);
            ShowDropPodGame($"Exported {_dpActive.Count} mods and {files.Count} mod settings file{(files.Count == 1 ? "" : "s")} to {dlg.FileName}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not export it: " + e.Message); }
    }

    /// <summary>Imports a modlist zip made by <see cref="ExportWithSettings"/>, and offers to put its settings files in place.</summary>
    void ImportWithSettings(string zip)
    {
        var (list, settings) = ModSettings.Unpack(zip, Path.GetFileNameWithoutExtension(zip));
        AddModlist(list);
        if (settings.Count == 0 || _dpScan is not { } scan) return;
        if (MessageBox.Show(this, $"The modlist carries {settings.Count} mod settings file{(settings.Count == 1 ? "" : "s")}. Put {(settings.Count == 1 ? "it" : "them")} in place for {(_dpInst != null ? "the instance " + _dpInst.Name : "the normal game")}? Files it replaces are kept as backups.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (GameRunning() != null) { ShowDropPodGame("RimWorld is running and would overwrite the settings when it closes. Close it and import again."); return; }
        try { ShowDropPodGame($"Added the modlist {list.Name} and put {ModSettings.Restore(scan.Install.DataFolder, settings)} mod settings files in place."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not write the settings: " + e.Message); }
    }

    // ------------------------------------------------------------ updates with their notes

    async Task ShowUpdateNotesAsync(List<ModUpdate> outdated)
    {
        var notes = new Dictionary<string, List<UpdateNote>>();
        ShowDropPodGame($"Reading the change notes of {outdated.Count} updated mod{(outdated.Count == 1 ? "" : "s")} from Steam...");
        using var limit = new SemaphoreSlim(4);
        await Task.WhenAll(outdated.Take(40).Select(async u =>
        {
            await limit.WaitAsync();
            try { var n = await UpdateNotes.FetchAsync(u.Mod.WorkshopId!); lock (notes) notes[u.Mod.WorkshopId!] = n; }
            finally { limit.Release(); }
        }));
        string Text(object? o) => o is ModUpdate u && notes.TryGetValue(u.Mod.WorkshopId!, out var n) && n.Count > 0
            ? string.Join("\n\n", n.Select(x => (x.When is { } t ? t.ToLocalTime().ToString("g") + "\n" : "") + x.Text))
            : "Steam's change notes could not be read here. Open the change notes page to see them.";
        using var w = new ListWindow("What changed",
            "The installed Workshop mods with a newer version on Steam, and what their authors wrote about it. Notes that mention breaking changes or new saves are in red: " +
            "in the middle of a colony, freeze an instance first (Instance > More) or wait until you start a new one. The Steam client updates subscribed mods by itself.",
            new[] { ("Mod", 260), ("Yours", 110), ("On Steam", 110), ("Watch out for", 240) }, detailsOf: Text);
        foreach (var u in outdated)
        {
            var risky = notes.TryGetValue(u.Mod.WorkshopId!, out var n) ? n.SelectMany(x => x.Warnings).Distinct().ToList() : new List<string>();
            w.AddRow(u, risky.Count > 0 ? Tone.Error : Tone.Normal, u.Mod.DisplayName, u.Local.ToLocalTime().ToString("d MMM"), u.Remote.ToLocalTime().ToString("d MMM"), string.Join(", ", risky));
        }
        w.AddButton("Open the change notes &page", () => { if (w.Selected is ModUpdate u) OpenUrl(UpdateNotes.Url(u.Mod.WorkshopId!)); }, needsRow: true);
        w.DoubleClicked = o => { if (o is ModUpdate u) OpenUrl(UpdateNotes.Url(u.Mod.WorkshopId!)); };
        w.AddClose();
        w.ShowDialog(this);
    }

    // ------------------------------------------------------------ before and after starting the game

    /// <summary>
    /// Offers a copy of the newest save when the game is about to start with mods it wasn't played with. False when the
    /// player cancels.
    /// </summary>
    bool OfferSaveBackup()
    {
        if (!_s.DropPodSaveBackups || _dpScan is not { } scan) return true;
        SaveInfo? newest;
        try { newest = Saves.List(_dpGame, scan.Install).FirstOrDefault(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
        var active = ActiveFromGame();
        if (newest == null || SaveBackups.Risk(newest, active, scan, _dpGame) is not { } risk) return true;
        var answer = MessageBox.Show(this, $"Your newest save, {newest.Name}, wasn't played with this mod list. {risk}\n\nCopy the save to the save backups before starting? (Yes copies it, No starts without a copy.)",
            "Back up the save?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes)
            try { Say("Backed up " + Path.GetFileName(SaveBackups.Backup(newest.Path, SaveBackupFolder))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not copy the save: " + e.Message); return false; }
        return true;
    }

    /// <summary>Watches the log after a start and records how long loading took (see <see cref="LoadTimes"/>).</summary>
    void MeasureLoadTime(Process game, string log, string setup, IReadOnlyList<string> active, Dictionary<string, string> names)
    {
        var start = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            var samples = new List<(double, long)>();
            double? settled = null;
            while (settled == null && DateTime.UtcNow - start < TimeSpan.FromMinutes(20))
            {
                await Task.Delay(1000);
                bool exited;
                try { exited = game.HasExited; } catch { exited = true; }
                if (exited) break;
                long len;
                try { var fi = new FileInfo(log); len = fi.Exists && fi.LastWriteTimeUtc >= start.AddSeconds(-5) ? fi.Length : 0; }
                catch (IOException) { len = 0; }
                samples.Add(((DateTime.UtcNow - start).TotalSeconds, len));
                settled = LoadTimes.Settled(samples);
            }
            if (settled is not { } secs || secs < 3) return;
            var run = DropPodLoadTimes.Add(setup, active, secs, names);
            var prev = DropPodLoadTimes.All().Where(t => t.List == setup).SkipLast(1).LastOrDefault();
            var line = $"RimWorld took about {secs:0} seconds to load with {active.Count} mods" + (prev != null ? $" ({secs - prev.Seconds:+0;-0;0} s on last time)" : "") +
                (run.Added.Count > 0 ? ", with " + string.Join(", ", run.Added.Take(5)) + " added since" : "") + ".";
            try { BeginInvoke(() => { if (_dpScan != null) ShowDropPodGame(line); }); } catch (InvalidOperationException) { }
        });
    }

    void ShowLoadTimes()
    {
        var all = DropPodLoadTimes.All();
        all.Reverse();
        using var w = new ListWindow("Load times",
            "How long RimWorld took to load each time the Drop Pod started it on this PC (until the log went quiet at the main menu), newest first. " +
            "A jump after adding mods shows which ones made loading slower. They are estimates, but the same estimate every time.",
            new[] { ("When", 140), ("Setup", 200), ("Mods", 60), ("Seconds", 70), ("Since the last time", 120), ("Added since the last time", 300) });
        for (var i = 0; i < all.Count; i++)
        {
            var t = all[i];
            var prev = all.Skip(i + 1).FirstOrDefault(p => p.List == t.List);
            var delta = prev == null ? "" : $"{t.Seconds - prev.Seconds:+0;-0;0} s";
            w.AddRow(t, prev != null && t.Seconds - prev.Seconds > Math.Max(10, prev.Seconds * 0.2) ? Tone.Warn : Tone.Normal,
                t.At.ToString("g"), t.List, t.Mods.ToString(), t.Seconds.ToString("0"), delta, string.Join(", ", t.Added));
        }
        if (all.Count == 0) w.Intro += "\n\nNone yet: start the game with Play or Test and leave it at the main menu for half a minute.";
        w.AddClose();
        w.ShowDialog(this);
    }

    // ------------------------------------------------------------ launch options and freezing

    void EditLaunchOptions()
    {
        var current = _dpInst?.LaunchArgs ?? _s.DropPodLaunchArgs;
        var common = string.Join(", ", LaunchArgs.Common.Select(c => $"{c.Arg} ({c.What})"));
        var text = Prompt($"Extra arguments for starting {(_dpInst != null ? "the instance " + _dpInst.Name : "the normal game")}. Common ones: {common}", current);
        if (text == null) return;
        var clash = LaunchArgs.Clashes(LaunchArgs.Split(text));
        if (clash.Count > 0) { ShowDropPodGame($"Leave out {string.Join(" and ", clash)}: the Drop Pod sets those itself for instances."); return; }
        if (_dpInst != null) { _dpInst.LaunchArgs = text.Trim(); DropPodInstances.Save(_dpInst); }
        else { _s.DropPodLaunchArgs = text.Trim(); _s.Save(); }
        ShowDropPodGame(text.Trim().Length == 0 ? "No extra launch options." : "Play and Test now add: " + text.Trim());
    }

    async Task FreezeInstanceAsync()
    {
        if (_dpScan is not { } scan || _dpInst is not { } inst) return;
        var steam = WorkshopUpdates.SteamClientTimes(scan.Install.WorkshopFolder, _dpGame.AppId);
        if (Freezer.IsFrozen(inst))
        {
            var waiting = Freezer.Waiting(inst, steam);
            if (MessageBox.Show(this, $"Unfreeze {inst.Name}? It then uses the Workshop's current copies again" + (waiting.Count > 0 ? $", including the {waiting.Count} update{(waiting.Count == 1 ? "" : "s")} waiting: {string.Join(", ", waiting.Take(8).Select(f => f.Name))}" : "") + ".",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Freezer.Unfreeze(inst); ShowDropPodGame($"{inst.Name} uses the Workshop's copies again."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not unfreeze it: " + e.Message); }
            ShowDropPodDetails();
            return;
        }
        var byId = scan.ById();
        var mods = _dpActive.Where(byId.ContainsKey).Select(k => byId[k]).Where(m => m.Source == ModSource.Workshop && m.WorkshopId != null).ToList();
        if (mods.Count == 0) { ShowDropPodGame("This instance has no active Workshop mods to freeze."); return; }
        var size = mods.Sum(m => Hangar.FolderSize(m.Folder));
        if (MessageBox.Show(this, $"Freeze {inst.Name}'s {mods.Count} Workshop mods? They are copied into the instance ({Hangar.Size(size)}), and while it plays the copies are used instead of Steam's, so updates can't change a running colony. Unfreeze it any time.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _dpBusy = true;
        UpdateDropPodButtons();
        try
        {
            var done = await Task.Run(() => Freezer.Freeze(inst, mods, steam, name => BeginInvoke(() => ShowDropPodGame($"Copying {name}..."))));
            ShowDropPodGame($"Froze {done.Count} Workshop mods for {inst.Name}. Play links them in while the game runs.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowDropPodGame("Could not freeze it: " + e.Message); }
        finally { _dpBusy = false; UpdateDropPodButtons(); ShowDropPodDetails(); }
    }

    /// <summary>The taskbar button's right-click list: Play for the normal game and each instance.</summary>
    void UpdateJumpList()
    {
        if (!HasDropPod) { JumpList.Set(Array.Empty<(string, string, string)>()); return; }
        var items = new List<(string Title, string Args, string Tip)> { ("Play RimWorld", "--play", "Start RimWorld normally") };
        foreach (var i in DropPodInstances.All(_dpGame.AppId).Take(8))
            items.Add(($"Play {i.Name}", "--play \"" + i.Name.Replace("\"", "") + "\"", $"Start RimWorld with the instance {i.Name}"));
        JumpList.Set(items);
    }
}
