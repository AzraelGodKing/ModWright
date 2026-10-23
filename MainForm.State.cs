using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>What the window shows for the current mods folder: reading it, the selection and the buttons.</summary>
public sealed partial class MainForm
{
    void SaveFields()
    {
        _s.Account = _account.Text.Trim();
        _s.AllowCreate = _allowCreate.Checked;
        _s.Save();
    }

    /// <summary>Fills the mods folder drop-down from the recent list, the current folder selected.</summary>
    void FillRecent()
    {
        _populating = true;
        _repo.BeginUpdate();
        _repo.Items.Clear();
        foreach (var p in _s.RecentThatExist()) _repo.Items.Add(p);
        var i = _repo.Items.IndexOf(_s.RepoPath);
        if (i < 0 && _s.RepoPath.Length > 0) i = _repo.Items.Add(_s.RepoPath); // gone from disk: still show what is set
        _repo.SelectedIndex = i;
        _repo.EndUpdate();
        _populating = false;
        Tip(_repo, _s.RepoPath.Length > 0 ? _s.RepoPath : "Pick the folder that holds your mods");
    }

    void BrowseRepo()
    {
        if (_busy) return;
        using var d = new FolderBrowserDialog { Description = "Pick the folder that holds your mods (the repo)", SelectedPath = _s.RepoPath, UseDescriptionForTitle = true };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        OpenRepo(d.SelectedPath);
    }

    /// <summary>Makes <paramref name="path"/> the mods folder, remembers it in the recent list, and reads it.</summary>
    void OpenRepo(string path)
    {
        var changed = !string.Equals(path, _s.RepoPath, StringComparison.OrdinalIgnoreCase);
        _s.Remember(path);
        _s.Save();
        FillRecent();
        if (changed) { _failed.Clear(); _retrySet.Clear(); _progress.Visible = false; _filter.Text = ""; _projectOpen = false; }
        ShowNote(_projectNote, null);
        RefreshAll();
    }

    void EditLayout()
    {
        SaveFields();
        if (!Directory.Exists(_s.RepoPath)) { Nudge(_repo, _projectNote, "Pick the mods folder first; the layout file lives in it."); return; }
        if (_cfg.IsBuiltIn) { Nudge(_preset, _projectNote, "Built-in layouts are locked. Choose \"Custom\" in the Layout box to make your own."); return; }
        var path = _cfg.EnsureFile(_s.RepoPath);
        Append($"Opened {path}. Save it, then press Refresh.", LogKind.Note);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Append("Could not open the file: " + e.Message, LogKind.Error); }
    }

    /// <summary>What reading a mods folder found: everything <see cref="RefreshAll"/> shows that needs the disk or git.</summary>
    sealed record FolderScan(string Repo, ProjectConfig Cfg, string? Branch, List<ModInfo> Mods,
        Dictionary<string, string> Uploaded, Dictionary<string, UploadEntry> History, string Stamp, bool IsGit);

    /// <summary>Reads the mods folder, its layout, git branch and this PC's upload records. Touches no controls.</summary>
    static FolderScan ScanFolder(string repo, bool strictAbout)
    {
        var cfg = ProjectConfig.Load(repo);
        var branch = ModScanner.GitBranch(repo);
        var isGit = branch != null || ModScanner.IsGitRepo(repo); // a repo with no commits yet has no branch to name
        var mods = ModScanner.Scan(repo, cfg, strictAbout);
        foreach (var m in mods)
            if (WorkshopPage.Get(m.Dir) is { } page) m.Warnings.Add("Workshop page changes go out with the next upload: " + page.Summary());
        return new FolderScan(repo, cfg, branch, mods, UploadedVersions.LoadRecord(), UploadHistory.Latest(UploadHistory.Load()), ModScanner.FolderStamp(repo, cfg), isGit);
    }

    /// <summary>Reads the mods folder again without holding up the window (see <see cref="RefreshAllAsync"/>).</summary>
    void RefreshAll() => _ = RefreshAllAsync();

    /// <summary><see cref="RefreshAll"/> with the folder read right here, for --shot, which needs the list filled at once.</summary>
    void RefreshNow()
    {
        SaveFields();
        ShowProfiles();
        ShowAnnounce();
        ShowScan(ScanFolder(_s.RepoPath, _s.StrictAboutCheck));
    }

    bool _repoIsGit;

    /// <summary>Whether the mods folder is a git repo: known from the last scan, so git is not started again to ask.</summary>
    bool RepoIsGit => string.Equals(_shownRepo, _s.RepoPath, StringComparison.OrdinalIgnoreCase) ? _repoIsGit : ModScanner.IsGitRepo(_s.RepoPath);

    /// <summary>The fingerprint of the folder as the list shows it (<see cref="ModScanner.FolderStamp"/>).</summary>
    string _folderStamp = "";
    bool _stampChecking;

    /// <summary>
    /// When the window comes back to the front, refreshes the list if the mods folder changed while it was away (a mod
    /// edited in another program, a version raised, a commit made). Never during an upload or while a dialog is open.
    /// </summary>
    async Task RefreshIfChangedAsync()
    {
        if (_stampChecking || _busy || _inUpload || NoStartupPrompts || OwnedForms.Length > 0 || _folderStamp.Length == 0) return;
        var (repo, cfg) = (_s.RepoPath, _cfg);
        _stampChecking = true;
        string now;
        try { now = await Task.Run(() => ModScanner.FolderStamp(repo, cfg)); }
        finally { _stampChecking = false; }
        if (IsDisposed || _busy || _inUpload || repo != _s.RepoPath || now.Length == 0 || now == _folderStamp) return;
        Append("The mods folder changed, so the list was refreshed.", LogKind.Note);
        await RefreshAllAsync();
    }

    /// <summary>Counts refreshes, so a slow scan that finishes after a newer one has started is thrown away.</summary>
    int _scanNo;

    /// <summary>
    /// Reads the mods folder on a worker thread, so the window is drawn and responds while git and the disk are slow.
    /// Only the newest of several overlapping refreshes is shown.
    /// </summary>
    async Task RefreshAllAsync()
    {
        SaveFields();
        ShowProfiles();
        ShowAnnounce();
        var repo = _s.RepoPath;
        var no = ++_scanNo;
        UseWaitCursor = true;
        FolderScan scan;
        var strict = _s.StrictAboutCheck;
        try { scan = await Task.Run(() => ScanFolder(repo, strict)); }
        finally { if (no == _scanNo && !IsDisposed) UseWaitCursor = false; }
        if (IsDisposed || no != _scanNo || repo != _s.RepoPath) return; // closed, refreshed again, or another folder was opened meanwhile
        ShowScan(scan);
    }

    void ShowScan(FolderScan scan)
    {
        _cfg = scan.Cfg;
        _branch.Text = !Directory.Exists(scan.Repo) ? "" : scan.Branch == null ? "(not a git repo)" : "branch: " + scan.Branch;
        ShowGame();
        if (_cfg.Error != null) Append(_cfg.Error, LogKind.Warn);

        // A refresh of the same folder keeps what the reader had: the ticks, the selected mod and the scroll position.
        // The rescan makes new mod objects, so they are matched by folder.
        var sameFolder = string.Equals(_shownRepo, scan.Repo, StringComparison.OrdinalIgnoreCase);
        var ticks = sameFolder ? _rows.ToDictionary(it => ((ModInfo)it.Tag!).Dir, it => it.Checked, StringComparer.OrdinalIgnoreCase) : new();
        _keepSelected = sameFolder ? SelectedMod()?.Dir : null;
        var top = sameFolder && _list.Items.Count > 0 ? (_list.TopItem?.Tag as ModInfo)?.Dir : null;
        _shownRepo = scan.Repo;

        _folderStamp = scan.Stamp;
        _repoIsGit = scan.IsGit;
        _thumbsChecked.Clear(); // a changed preview image shows after a refresh
        _mods = scan.Mods;
        var uploaded = scan.Uploaded;
        var history = scan.History;
        _history = history;
        _rows.Clear();
        foreach (var m in _mods)
        {
            var it = new ListViewItem(m.Name) { UseItemStyleForSubItems = false };
            it.SubItems.Add(!m.OnSteam ? "-" : history.TryGetValue(m.WorkshopId!, out var last) ? last.Version : uploaded.TryGetValue(m.WorkshopId!, out var up) ? up : "...");
            it.SubItems.Add(m.OnSteam ? "..." : "-");
            it.SubItems.Add(m.Version.Length > 0 ? m.Version : "?");
            it.SubItems.Add(m.WorkshopId ?? "0"); // 0 = not on Steam yet, so uploading creates it
            it.SubItems.Add(m.PreviewOk == null ? "-" : m.PreviewOk == true ? "✔" : "✖");
            it.SubItems.Add("");
            it.ToolTipText = RowTip(m);
            it.Tag = m;
            it.Checked = CanTick(m) && (ticks.TryGetValue(m.Dir, out var was) ? was : m.OnSteam);
            UpdateRowState(it);
            _rows.Add(it);
            var problems = string.Join("; ", m.Problems);
            if (m.Ready) _loggedProblems.Remove(m.Dir);
            else if (!_loggedProblems.TryGetValue(m.Dir, out var logged) || logged != problems)
            {
                _loggedProblems[m.Dir] = problems;
                Append($"{m.Name}: fix {problems}", LogKind.Warn);
            }
        }
        ApplyFilter();
        _keepSelected = null;
        if (top != null && _list.Items.Cast<ListViewItem>().FirstOrDefault(i => SameDir(i, top)) is { } topRow)
            try { _list.TopItem = topRow; } catch { } // only works once the list has a handle and is in details view
        _ = LoadUploadedVersionsAsync(_mods.Where(m => m.OnSteam).ToList(), uploaded, history);

        UpdateLoginHint();
        UpdateSetup();
        UpdateProjectCard();
        UpdateGoButton();
        UpdateSelectionButtons();
        UpdateDetail();
        StretchLastColumn();
    }

    /// <summary>
    /// Once the mods folder, app ID and mods are all there, the Project card folds into one line ("RimWorld mods ·
    /// RimWorld (294100) · RimWorld layout") with Change... to open it again.
    /// </summary>
    void UpdateProjectCard()
    {
        var complete = Directory.Exists(_s.RepoPath) && _cfg.AppIdValid && _rows.Count > 0 && _cfg.Error == null;
        var open = !complete || _projectOpen;
        SuspendLayout();
        foreach (var r in _projectRows) r.Visible = open;
        _projectToggle.Visible = complete;
        _projectToggle.Text = open ? "Done" : "Change...";
        _projectToggle.AccessibleName = open ? "Hide the project settings" : "Change the mods folder, app ID or layout";
        _projectSummary.Visible = complete && !open;
        if (complete)
        {
            var game = _cfg.GameName.Length > 0 ? $"{_cfg.GameName} ({_cfg.AppId})" : "app " + _cfg.AppId;
            _projectSummary.Text = string.Join("   ·   ", new[] { Path.GetFileName(_s.RepoPath.TrimEnd('\\', '/')), game, _cfg.PresetName() + " layout", _branch.Text,
                _s.RefFor(_s.RepoPath) is { Length: > 0 } from ? "uploads from " + from : "" }.Where(t => t.Length > 0));
            Tip(_projectSummary, _s.RepoPath + "\n" + _game.Text + "\nClick to change the mods folder, app ID or layout.");
        }
        ResumeLayout(true);
    }

    /// <summary>The strip under the list: what the selected mod needs, in full, or what the keys do when nothing is selected.</summary>
    void UpdateDetail()
    {
        var m = SelectedMod();
        string text;
        Tone tone;
        if (m == null) { text = "Select a mod to see everything it needs. Space ticks it, Enter edits its Workshop page, F2 changes its version, F1 lists every shortcut."; tone = Tone.Muted; }
        else if (!m.Ready) { text = $"{m.Name}: fix " + string.Join("; ", m.Problems) + (ModScanner.NeedsChangelogEntry(m, _cfg) ? ". Double-click the status (or right-click) to write the changelog entry."
            : m.PreviewOk == false && m.PreviewPath.Length > 0 ? ". Right-click > Package > Shrink preview image to bring it under 1 MB." : "."); tone = Tone.Error; }
        else if (m.Warnings.Count > 0) { text = $"{m.Name}: " + string.Join("; ", m.Warnings) + "."; tone = Tone.Warn; }
        else
        {
            var row = _rows.FirstOrDefault(i => ReferenceEquals(i.Tag, m));
            var state = row != null ? StateOf(row) : RowState.Ready;
            text = state switch
            {
                RowState.UpToDate => $"{m.Name}: Steam already has {m.Version}.",
                RowState.New => _allowCreate.Checked
                    ? $"{m.Name}: not on Steam yet; uploading creates a new private item."
                    : $"{m.Name}: not on Steam yet. Turn on \"Create new items (private)\" to tick it and create it on Steam.",
                RowState.Changed => $"{m.Name}: {m.Version} is ready to upload (Steam has {row!.SubItems[UploadedCol].Text}).",
                RowState.Failed => $"{m.Name}: the last upload failed; see the log.",
                _ => $"{m.Name}: {m.Version} is ready to upload.",
            };
            tone = state == RowState.Failed ? Tone.Error : Tone.Muted;
        }
        _detail.Text = text;
        _detail.ForeColor = Theme.Of(tone);
        _detail.BackColor = Theme.Current.Input;
        _detail.AccessibleDescription = text;
        Tip(_detail, text);
        UpdateDetailsPanel(m, tone);
    }

    /// <summary>Fills the panel beside the list with the selected mod, or a summary of the list when none is selected.</summary>
    void UpdateDetailsPanel(ModInfo? m, Tone tone)
    {
        if (_listSplit == null || _listSplit.Panel2Collapsed) return; // filled when it shows
        if (m == null)
        {
            var states = _rows.Select(StateOf).ToList();
            var facts = new List<(string, string)> { ("Ticked", $"{_rows.Count(i => i.Checked)} of {_rows.Count}") };
            var known = _mods.Where(x => x.OnSteam && _live.TryGetValue(x.WorkshopId!, out var w) && w.Public).Select(x => _live[x.WorkshopId!]).ToList();
            if (known.Count > 0) facts.Add(("Subscribers", $"{known.Sum(w => w.Subscribers):N0} across {known.Count} item{(known.Count == 1 ? "" : "s")}"));
            if (_cfg.GameName.Length > 0) facts.Add(("Game", _cfg.GameName));
            _details.ShowList(Path.GetFileName(_s.RepoPath.TrimEnd('\\', '/')), ModDetails.Summary(states), facts);
            return;
        }
        var row = _rows.FirstOrDefault(i => ReferenceEquals(i.Tag, m));
        var state = row != null ? StateOf(row) : RowState.Ready;
        var onSteam = row?.SubItems[UploadedCol].Text;
        WorkshopItem? live = m.OnSteam && _live.TryGetValue(m.WorkshopId!, out var item) ? item : null;
        UploadEntry? last = m.OnSteam && _history.TryGetValue(m.WorkshopId!, out var h) ? h : null;
        string[] tags;
        try { tags = WorkshopTags.For(m.Dir, _cfg); } catch { tags = Array.Empty<string>(); }
        var note = "";
        if (ChangelogOf(m) is { } changelog)
            try { note = ModScanner.ReadChangelog(changelog, _cfg, m.Version).notes; } catch { }
        var description = "";
        var descFile = string.IsNullOrWhiteSpace(_cfg.SteamDescriptionFile) ? null : Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.SteamDescriptionFile));
        if (descFile != null && File.Exists(descFile))
            try { description = ModDetails.PlainText(File.ReadAllText(descFile), 600); } catch { }
        var line = state switch
        {
            RowState.UpToDate => "Steam already has this version",
            RowState.Changed => "this version is not on Steam yet",
            RowState.New => _allowCreate.Checked ? "uploading creates a new private item" : "not on Steam yet",
            RowState.Failed => "the last upload failed; see the log",
            RowState.Fix => "fix the problems below first",
            _ => "",
        };
        _details.ShowMod(m, ModRows.Label(state), ToneOf(state), line, ModDetails.Facts(m, onSteam, live, last, tags), ModDetails.PlainText(note, 900), description);
    }

    static bool SameDir(ListViewItem it, string dir) => it.Tag is ModInfo m && string.Equals(m.Dir, dir, StringComparison.OrdinalIgnoreCase);

    bool CanTick(ModInfo m) => m.Ready && (m.OnSteam || _allowCreate.Checked);

    RowState StateOf(ListViewItem it)
    {
        var m = (ModInfo)it.Tag!;
        return ModRows.State(m, it.SubItems[UploadedCol].Text, _failed.Contains(m.Dir));
    }

    /// <summary>Sets the Status text (the badge) and the row colours from the mod's current state.</summary>
    void UpdateRowState(ListViewItem it)
    {
        var m = (ModInfo)it.Tag!;
        // The badge is drawn from the state; the text is what screen readers (and sorting) see, so it carries the detail too.
        var detail = !m.Ready ? m.Problems[0] : m.Warnings.Count > 0 ? m.Warnings[0] : "";
        it.SubItems[StatusCol].Text = ModRows.Label(StateOf(it)) + (detail.Length > 0 ? ": " + detail : "");
        ColorRow(it, m);
    }

    static Tone ToneOf(RowState s) => s switch
    {
        RowState.Fix or RowState.Failed => Tone.Error,
        RowState.Changed => Tone.Warn,
        RowState.New => Tone.Info,
        RowState.UpToDate => Tone.Ok,
        _ => Tone.Muted,
    };

    /// <summary>Shows the rows that pass the filter box and "Hide up to date", in the chosen sort order.</summary>
    void ApplyFilter()
    {
        var visible = _rows.Where(it => ModRows.Show((ModInfo)it.Tag!, StateOf(it), _filter.Text, _hideUpToDate.Checked)).ToList();
        if (_sortCol >= 0)
        {
            visible.Sort(CompareRows);
            if (_sortDesc) visible.Reverse();
        }
        var selected = _keepSelected ?? SelectedMod()?.Dir;
        _populating = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(visible.ToArray());
        foreach (var it in visible) it.Selected = selected != null && SameDir(it, selected);
        _list.EndUpdate();
        _populating = false;
        UpdateEmptyState();
        UpdateGoButton();
        UpdateSelectionButtons();
        UpdateDetail();
    }

    int CompareRows(ListViewItem a, ListViewItem b)
    {
        var c = _sortCol switch
        {
            StatusCol => ModRows.CompareState(StateOf(a), StateOf(b)),
            SteamCol => Nullable.Compare(a.SubItems[SteamCol].Tag as DateTimeOffset?, b.SubItems[SteamCol].Tag as DateTimeOffset?),
            PreviewCol => Nullable.Compare(((ModInfo)a.Tag!).PreviewOk, ((ModInfo)b.Tag!).PreviewOk),
            _ => ModRows.CompareNatural(a.SubItems[_sortCol].Text, b.SubItems[_sortCol].Text),
        };
        return c != 0 ? c : ModRows.CompareNatural(a.Text, b.Text);
    }

    /// <summary>Clicking a column header sorts by it; clicking it again reverses; a third time goes back to folder order.</summary>
    void SortBy(int col)
    {
        if (_sortCol != col) { _sortCol = col; _sortDesc = false; }
        else if (!_sortDesc) _sortDesc = true;
        else _sortCol = -1;
        (_s.SortColumn, _s.SortDescending) = (_sortCol, _sortDesc);
        _s.Save();
        ShowSortArrows();
        ApplyFilter();
    }

    void ShowSortArrows()
    {
        for (var i = 0; i < _list.Columns.Count; i++)
            _list.Columns[i].Text = Localizer.T(ColumnNames[i]) + (i == _sortCol ? (_sortDesc ? "  ▼" : "  ▲") : "");
    }
}
