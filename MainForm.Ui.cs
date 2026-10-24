using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>The main window's controls: building the tabs, cards, buttons and the Options page.</summary>
public sealed partial class MainForm
{
    void BuildUi()
    {
        Text = "Modwright - The Launch Pad";
        Width = 1000; Height = 800; MinimumSize = new Size(840, 640); // at 96 DPI; scaled and fitted to the screen after Theme.Apply
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Theme.Style(_tip);

        _browse = Btn("&Browse...", (_, _) => BrowseRepo());
        _editLayout = Btn("&Edit layout file...", (_, _) => EditLayout());
        _checkLogin = Btn("&Check login", async (_, _) => { SaveFields(); await VerifyLoginAsync(true); });
        _doLogin = Btn("Log &in...", async (_, _) => { SaveFields(); await LoginFlowAsync(); });
        _install = Btn("In&stall SteamCMD", async (_, _) => await InstallAsync());
        // The Mods card's title row: compact buttons, like the Log card's, so the row fits the default window width.
        _refresh = SmallBtn("&Refresh", (_, _) => RefreshAll());
        _all = SmallBtn("&Tick all", (_, _) => TickAll(true));
        _none = SmallBtn("Tick &none", (_, _) => TickAll(false));
        _changed = SmallBtn("Tick c&hanged", (_, _) => TickChanged());
        _groups = SmallBtn("Groups ▾", (_, _) => ShowGroupsMenu());
        _editPage = SmallBtn("&Workshop page...", (_, _) => EditWorkshopPage(SelectedMod()));
        _go = Btn("Upload selected", async (_, _) => await GoAsync()).Role(ThemeRole.Primary);
        _go.Font = new Font(Font, FontStyle.Bold);
        _go.Padding = new Padding(18, 6, 18, 6);
        _later = Btn("Later...", (_, _) => UploadLater());
        _cancel = Btn("Cancel", (_, _) => Cancel());
        _cancel.Visible = false;
        _retry = Btn("Retr&y failed", async (_, _) => await RetryFailedAsync());
        _retry.Visible = false;
        _emptyBrowse = Btn("Browse...", (_, _) => BrowseRepo()).Role(ThemeRole.Accent);
        _emptyLayout = Btn("Edit layout file...", (_, _) => EditLayout());
        _emptyNew = Btn("New mod...", async (_, _) => await NewModAsync());
        _emptyClear = Btn("Clear the filter", (_, _) => { _filter.Text = ""; _hideUpToDate.Checked = false; });
        _projectToggle = SmallBtn("Change...", (_, _) => { _projectOpen = !_projectOpen; UpdateProjectCard(); if (_projectOpen) _repo.Focus(); });
        _setupNext = Btn("", (_, _) => _stepActions.FirstOrDefault(a => a != null)?.Invoke()).Role(ThemeRole.Accent);

        _uploadPage = BuildUploadPage();
        _optionsPage = BuildOptionsPage();
        _dropPodPage = BuildDropPodPage();
        var pages = new Panel { Dock = DockStyle.Fill };
        pages.Controls.Add(_uploadPage);
        pages.Controls.Add(_dropPodPage);
        pages.Controls.Add(_optionsPage);

        // Fill first, then the top bar, so docking puts the bar above the pages.
        Controls.Add(pages);
        Controls.Add(BuildFooter());
        var header = BuildHeader();
        Controls.Add(header);
        // Tab starts at the top bar (tabs and ?), then the page, top to bottom.
        header.TabIndex = 0;
        pages.TabIndex = 1;
        ApplySides();

        Theme.Apply(this);
        // Scaled for the screen's DPI by now; keep the window (and its minimum) on a small or high-DPI screen.
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea.Size;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, screen.Width), Math.Min(MinimumSize.Height, screen.Height));
        Size = new Size(Math.Min(Width, screen.Width), Math.Min(Height, screen.Height));
        EnableFolderDrop(this);

        _repo.SelectionChangeCommitted += (_, _) => { if (_repo.SelectedItem is string p && !_busy) OpenRepo(p); };
        _account.Leave += (_, _) => { SaveFields(); UpdateLoginHint(); UpdateSetup(); };
        _account.TextChanged += (_, _) => ShowNote(_accountNote, null);
        _allowCreate.CheckedChanged += (_, _) => { if (!_populating) { SaveFields(); RefreshAll(); } };
        _appId.Leave += async (_, _) => await ApplyAppIdAsync();
        _appId.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyAppIdAsync(); } };
        _preset.SelectionChangeCommitted += (_, _) => ApplyPresetChoice();
        _theme.SelectedIndexChanged += (_, _) => { if (_theme.SelectedIndex >= 0) SetTheme(Display.Themes[_theme.SelectedIndex].Name); };
        _textSize.SelectionChangeCommitted += (_, _) => SetTextSize();
        _logFilter.SelectedIndexChanged += (_, _) => RenderLog();
        _autoScroll.CheckedChanged += (_, _) => { if (_autoScroll.Checked) _log.ScrollToCaret(); };
        _projectSummary.Click += (_, _) => { _projectOpen = true; UpdateProjectCard(); _repo.Focus(); };
        _loginLink.LinkClicked += (_, _) => ShowOptions(_account);
        _profile.SelectedIndexChanged += (_, _) => { if (!_populating) SwitchProfile(); };
        _webhook.Leave += (_, _) => SaveAnnounce();
        _afterCmd.Leave += (_, _) => { if (!_populating && Directory.Exists(_s.RepoPath)) { _s.SetAfter(_s.RepoPath, _afterCmd.Text); _s.Save(); } };
        _pushTags.CheckedChanged += (_, _) => { if (!_populating) { _s.PushTags = _pushTags.Checked; _s.Save(); } };
        _keepPackages.CheckedChanged += (_, _) => { if (!_populating) { _s.KeepPackages = _keepPackages.Checked; _s.Save(); } };
        _buildCmd.Leave += (_, _) => { if (!_populating && Directory.Exists(_s.RepoPath)) { _s.SetBuild(_s.RepoPath, _buildCmd.Text); _s.Save(); } };
        _packageRef.Leave += (_, _) =>
        {
            if (_populating || !Directory.Exists(_s.RepoPath)) return;
            var r = _packageRef.Text.Trim();
            if (r.Length > 0 && RepoIsGit && !GitOps.RefExists(_s.RepoPath, r))
                Append($"\"{r}\" is not a branch or tag in this repo yet; uploads will stop until it exists.", LogKind.Warn);
            _s.SetRef(_s.RepoPath, r);
            _s.Save();
            UpdateProjectCard();
        };
        _ghReleases.CheckedChanged += (_, _) => { if (!_populating) SaveAnnounce(); };
        foreach (var box in new[] { _tsTeam, _tsCommunity, _tsCategories, _tsToken }) box.Leave += (_, _) => SaveThunderstore();
        _tsAfter.CheckedChanged += (_, _) => SaveThunderstore();
        _tagUploads.CheckedChanged += (_, _) => { if (!_populating) { _s.TagUploads = _tagUploads.Checked; _s.Save(); } };
        _commitIds.CheckedChanged += (_, _) => { if (!_populating) { _s.CommitNewIds = _commitIds.Checked; _s.Save(); } };
        _uploadCredit.CheckedChanged += (_, _) => { if (!_populating) { _s.UploadCredit = _uploadCredit.Checked; _s.Save(); } };
        _markdownNotes.CheckedChanged += (_, _) => { if (!_populating) { _s.MarkdownNotes = _markdownNotes.Checked; _s.Save(); } };
        _noteTemplate.Leave += (_, _) =>
        {
            if (_populating || !Directory.Exists(_s.RepoPath)) return;
            _s.SetTemplate(_s.RepoPath, _noteTemplate.Text);
            _s.Save();
        };
        _checklist.Leave += (_, _) =>
        {
            if (_populating || !Directory.Exists(_s.RepoPath)) return;
            _s.SetChecklist(_s.RepoPath, _checklist.Text);
            _s.Save();
        };
        _checklistMust.Checked = _s.ChecklistMustTick;
        _checklistMust.CheckedChanged += (_, _) => { _s.ChecklistMustTick = _checklistMust.Checked; _s.Save(); };
        _descFooter.Leave += (_, _) =>
        {
            if (_populating || !Directory.Exists(_s.RepoPath)) return;
            _s.SetFooter(_s.RepoPath, _descFooter.Text);
            _s.Save();
        };
        _clock.Tick += (_, _) => ShowClock();
        _logTimer.Tick += (_, _) => FlushLog();
        _checkUpdates.CheckedChanged += (_, _) =>
        {
            _autoUpdate.Enabled = _checkUpdates.Checked;
            if (!_populating) { _s.CheckForUpdates = _checkUpdates.Checked; _s.Save(); }
        };
        _autoUpdate.CheckedChanged += (_, _) => { if (!_populating) { _s.AutoUpdate = _autoUpdate.Checked; _s.Save(); } };
        _updateReady.LinkClicked += (_, _) => RestartToUpdate();
        _filterPause = new Debouncer(120, ApplyFilter);
        Disposed += (_, _) => _filterPause.Dispose();
        _filter.TextChanged += (_, _) => { if (_filter.TextLength == 0) { _filterPause.Cancel(); ApplyFilter(); } else _filterPause.Poke(); }; // clearing shows everything at once
        _filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _filter.Text.Length > 0) { _filter.Text = ""; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { _filterPause.Flush(); e.SuppressKeyPress = true; }
        };
        _hideUpToDate.CheckedChanged += (_, _) => { if (_populating) return; _s.HideUpToDate = _hideUpToDate.Checked; _s.Save(); ApplyFilter(); };
    }

    /// <summary>The strip along the bottom: the app's name and version on the left, the "not affiliated" line on the right.</summary>
    static Control BuildFooter()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(16, 3, 16, 4) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.Controls.Add(new Label { Text = "Modwright " + UpdateCheck.CurrentVersion, AutoSize = true, Font = Theme.SmallFont, Anchor = AnchorStyles.Left, Margin = Padding.Empty }.Role(ThemeRole.Muted), 0, 0);
        bar.Controls.Add(new Label { Text = Disclaimer, AutoSize = true, Font = Theme.SmallFont, Anchor = AnchorStyles.Right, Margin = Padding.Empty }.Role(ThemeRole.Muted), 1, 0);
        var footer = new Panel { Dock = DockStyle.Bottom, AutoSize = true };
        footer.Controls.Add(bar);
        footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1 }.Role(ThemeRole.Divider));
        return footer;
    }

    /// <summary>
    /// The brand bar along the top, dark in both themes: the icon and app name on the left, the Upload / Options tabs
    /// and the "?" button on the right, over a Steam-blue line.
    /// </summary>
    /// <summary>Under the name: which side of the app is open ("THE LAUNCH PAD" or "THE DROP POD").</summary>
    readonly Label _side = new Label { Text = "THE LAUNCH PAD", AutoSize = true, Margin = new Padding(0, 10, 0, 0), UseMnemonic = false }.Role(ThemeRole.Muted);

    Control BuildHeader()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6, RowCount = 1, Padding = new Padding(16, 10, 12, 2) }.Role(ThemeRole.Header);
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 8), Anchor = AnchorStyles.Left };
        if (Theme.AppIcon != null)
            title.Controls.Add(new PictureBox { Image = new Icon(Theme.AppIcon, 64, 64).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(32, 32), Margin = new Padding(0, 0, 10, 0) });
        // The name in two weights, "Mod" bold and "wright" light in the accent colour, then the side of the app that is open.
        title.Controls.Add(new BrandName { Font = Theme.TitleFont, Margin = new Padding(0, 6, 14, 0) }.Role(ThemeRole.Title));
        _side.Font = Theme.SmallFont;
        title.Controls.Add(_side);
        bar.Controls.Add(title, 0, 0);

        _navUpload = new NavButton { Text = "LA&UNCH PAD", AccessibleName = "The Launch Pad: upload your mods" };
        _navDropPod = new NavButton { Text = "DROP POD", AccessibleName = "The Drop Pod: your RimWorld mods" };
        _navOptions = new NavButton { Text = "&OPTIONS", AccessibleName = "Options" };
        _navUpload.Click += (_, _) => ShowPage(_uploadPage);
        _navDropPod.Click += (_, _) => ShowPage(_dropPodPage);
        _navOptions.Click += (_, _) => ShowPage(_optionsPage);
        _navUpload.Anchor = _navDropPod.Anchor = _navOptions.Anchor = AnchorStyles.Bottom;
        Tip(_navUpload, "The Launch Pad: upload and update your own mods on the Steam Workshop");
        Tip(_navDropPod, "The Drop Pod: the RimWorld mods you play with");
        bar.Controls.Add(_navUpload, 2, 0);
        bar.Controls.Add(_navDropPod, 3, 0);
        bar.Controls.Add(_navOptions, 4, 0);

        var helpMenu = new ContextMenuStrip();
        helpMenu.Items.Add(new ToolStripMenuItem("&Keyboard shortcuts", null, (_, _) => ShowShortcuts()) { ShortcutKeyDisplayString = "F1" });
        helpMenu.Items.Add(new ToolStripMenuItem("&Send a request (bug, feature, another game)...", null, (_, _) => OpenRequest()));
        Theme.StyleMenu(helpMenu);
        _help = Btn("?", (_, _) => helpMenu.Show(_help, new Point(_help.Width, _help.Height), ToolStripDropDownDirection.BelowLeft));
        _help.Tag = helpMenu;
        _help.AccessibleName = "Help: keyboard shortcuts, or send a request (bug, feature, another game)";
        _help.AutoSize = false; _help.Size = new Size(30, 28); _help.Padding = Padding.Empty;
        _help.Font = new Font(Font, FontStyle.Bold);
        _help.Margin = new Padding(12, 0, 0, 8);
        _help.Anchor = AnchorStyles.None;
        Tip(_help, "Keyboard shortcuts (F1), or send a request: support another game, report a bug or suggest a feature");
        bar.Controls.Add(_help, 5, 0);
        return bar;
    }

    Control BuildUploadPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 12, 14, 12) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // never wider than the window, whatever a row would like
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Shown until SteamCMD, the account, the mods folder and the app id are all set: the steps on one line, how
        // many are done, the next one as a button, and a thin bar of the progress.
        var steps = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Tag = "fill" };
        foreach (var l in _steps)
        {
            l.LinkClicked += (_, _) => (l.Tag as Action)?.Invoke();
            steps.Controls.Add(l);
        }
        var setupTitle = new Label { Text = "GET SET UP", AutoSize = true, Font = Theme.SectionFont, AccessibleName = "Get set up" }.Role(ThemeRole.Title);
        Tip(setupTitle, "Uploads need all four steps. This box goes away once they are done.");
        var setupRow = Row(setupTitle, steps, _setupCount, _setupNext);
        setupTitle.Margin = new Padding(0, 3, 20, 3);
        setupRow.Margin = Padding.Empty;
        _setup = Card(null, null, setupRow, _setupBar);
        page.Controls.Add(_setup, 0, 0);

        _preset.Items.AddRange(ProjectConfig.PresetNames.Cast<object>().Append("Custom").ToArray());
        Tip(_repo, "Your mods folders; pick one you used before, press Browse, or drop a folder onto this window");
        Tip(_browse, "Pick the folder that holds your mods (Ctrl+O)");
        Tip(_projectToggle, "Show or hide the mods folder, app ID and layout");
        Tip(_projectSummary, "Click to change the mods folder, app ID or layout");
        _projectRows = new Control[]
        {
            Row(Lbl("Mods re&po"), _repo, _browse, _branch),
            Row(Lbl("Steam &app ID"), _appId, Lbl("&Layout"), _preset, _editLayout),
            Row(_game),
        };
        page.Controls.Add(Card("Project", new Control[] { _projectSummary, _projectToggle }, _projectRows.Append(Row(_projectNote)).ToArray()), 0, 1);

        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Dock = DockStyle.Fill;
        _list.ShowItemToolTips = true;
        // Taller rows leave room for the preview thumbnail and the status badge.
        _list.SmallImageList = new ImageList { ImageSize = new Size(1, U(28)) };
        int[] widths = { 200, 105, 110, 90, 105, 70, 190 };
        for (var i = 0; i < ColumnNames.Length; i++)
            _list.Columns.Add(Localizer.T(ColumnNames[i]), (int)(widths[i] * Theme.TextScale), i == PreviewCol ? HorizontalAlignment.Center : HorizontalAlignment.Left);
        _list.PaintCells(PaintCell);
        _list.ItemCheck += OnItemCheck;
        // Windows reports ticks while it is still taking rows in (when the list is filled or its window is created),
        // and a row's tick can't be read until it has its place; so count them once the current message is done.
        _list.ItemChecked += (_, _) => { QueueGoButton(); if (!_populating && SelectedMod() == null) UpdateDetail(); }; // the panel counts the ticks
        _list.MouseDoubleClick += OnListDoubleClick;
        _list.KeyDown += OnListKey;
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _list.SelectedIndexChanged += (_, _) => QueueDetail();
        _list.GotFocus += (_, _) => _list.Invalidate();
        _list.LostFocus += (_, _) => _list.Invalidate();
        _list.Resize += (_, _) => StretchLastColumn();
        _list.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex < _list.Columns.Count - 1) { StretchLastColumn(); PlaceDetails(); } };
        Tip(_editPage, "Change the selected mod's title, description or visibility on Steam with its next upload");
        Tip(_hideUpToDate, "Hide the mods whose version on Steam is already the one to upload");
        Tip(_refresh, "Read the mods folder again (F5)");
        _list.ContextMenuStrip = BuildRowMenu();

        // The list and its empty state share one spot; only one of them is visible.
        var emptyBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.None };
        var emptyButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.None, Margin = new Padding(0, 6, 0, 0) };
        foreach (var b in new[] { _emptyBrowse, _emptyNew, _emptyLayout, _emptyClear }) { b.Margin = new Padding(4, 0, 4, 0); emptyButtons.Controls.Add(b); }
        _emptyTitle.Margin = new Padding(0, 0, 0, 2);
        emptyBox.Controls.AddRange(new Control[] { _emptyTitle, _emptyText, emptyButtons });
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.Controls.Add(emptyBox, 0, 1);
        _empty.Controls.Add(center);
        var listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _detail.Height = 26; // scaled with the window
        // The list and the details panel side by side; drag the bar between them to give either more room.
        _listSplit = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6, Margin = Padding.Empty, AccessibleName = "Resize the details panel", Panel2Collapsed = true,
        };
        _listSplit.Panel1.Controls.Add(_list);
        _listSplit.Panel1.Controls.Add(_empty);
        _details.Dock = DockStyle.Fill;
        _listSplit.Panel2.Controls.Add(_details);
        _listSplit.SplitterMoved += (_, _) =>
        {
            if (_populating || _placingDetails || _listSplit.Panel2Collapsed || !_listSplit.IsHandleCreated) return;
            _s.DetailsWidth = (int)Math.Round(_listSplit.Panel2.Width * 96.0 / DeviceDpi / Theme.TextScale);
            _s.Save();
        };
        _listSplit.SizeChanged += (_, _) => PlaceDetails();
        Tip(_listSplit, "Drag to give the mod list or the details more room");
        listHost.Controls.Add(_listSplit);
        listHost.Controls.Add(_detail); // docked first, so it keeps its strip under the list
        AddDetailActions();
        _showDetails.Checked = _s.ShowDetails;
        _showDetails.CheckedChanged += (_, _) => { if (_populating) return; _s.ShowDetails = _showDetails.Checked; _s.Save(); PlaceDetails(); };

        var mods = Card("Mods", new Control[] { _filter, _hideUpToDate, _editPage, _all, _changed, _groups, _none, _refresh }, listHost);
        mods.AutoSize = false; mods.Dock = DockStyle.Fill;

        // Only while uploading and afterwards (with the result); nothing to show before the first run.
        _progress = Card(null, null,
            Row(_current, _stage),
            _modBar,
            Lbl("Overall").Role(ThemeRole.Muted),
            _overall);
        _progress.Visible = false;

        Tip(_loginLink, "Steam login settings are on the Options tab");
        Tip(_all, "Tick every mod that is ready to upload, then upload them in one go");
        Tip(_changed, "Tick only the mods whose version Steam does not have yet (Changed, or not known)");
        Tip(_groups, "Tick a saved group of mods, or save the ticked mods as a group");
        Tip(_testItems, "Send the ticked mods to their private test copies on Steam instead of the real items, to check them in game first");
        Tip(_dry, "Check everything and build the upload, but send nothing to Steam");
        Tip(_allowCreate, "Mods that are not on Steam yet are created as new private Workshop items");
        Tip(_go, "Upload the ticked mods (Ctrl+Enter)");
        Tip(_retry, "Upload again only the mods that failed or were not reached in the last run");
        Tip(_later, "Upload the ticked mods at a time you pick (Windows Task Scheduler), or see and cancel scheduled uploads");
        var actions = Row(_dry, _allowCreate, _testItems, _loginLink, _retry, _cancel, _later, _go);
        actions.Margin = new Padding(0, 2, 0, 8);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(mods, 0, 0);
        top.Controls.Add(_progress, 0, 1);
        top.Controls.Add(actions, 0, 2);

        _logFilter.Items.AddRange(new object[] { "Everything", "Warnings and errors", "Errors only" });
        _logFilter.SelectedIndex = 0;
        var copy = SmallBtn("Copy", (_, _) => CopyLog());
        var save = SmallBtn("&Save...", (_, _) => SaveLog());
        var clear = SmallBtn("Clear", (_, _) => { _logLines.Clear(); _pendingLog.Clear(); _logBoxLines = 0; _log.Clear(); });
        var steamLogs = SmallBtn("SteamCMD lo&gs", (_, _) => OpenSteamCmdLogs());
        var pastLogs = SmallBtn("&Past logs", (_, _) => ShowPastLogs());
        Tip(copy, "Copy the whole log");
        Tip(pastLogs, $"Read the logs of earlier uploads. Every upload run's log is saved; the newest {RunLogs.Keep} are kept.");
        Tip(_logFilter, "Show every line, or only the ones that need attention. Copy and Save always take the whole log.");
        Tip(_autoScroll, "Keep the newest line in view. Untick to read back while an upload runs.");
        Tip(steamLogs, "Open SteamCMD's own log folder (workshop_log.txt and others)");
        var log = Card("Log", new Control[] { _logFilter, _autoScroll, copy, save, clear, pastLogs, steamLogs }, _log);
        log.AutoSize = false; log.Dock = DockStyle.Fill;
        log.Margin = Padding.Empty;

        // The list and the log share the rest of the page; drag the bar between them (or Tab to it and use the arrow
        // keys) to give either more room. The log keeps its height when the window is resized.
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 8, Margin = Padding.Empty, AccessibleName = "Resize the log",
        };
        _split.Panel1.Controls.Add(top);
        _split.Panel2.Controls.Add(log);
        _split.Paint += PaintGrip;
        _split.SplitterMoved += (_, _) =>
        {
            if (_populating || !_split.IsHandleCreated) return;
            _s.LogHeight = (int)Math.Round(_split.Panel2.Height * 96.0 / DeviceDpi / Theme.TextScale);
            _s.Save();
        };
        Tip(_split, "Drag to give the mod list or the log more room");
        page.Controls.Add(_split, 0, 2);
        page.Layout += (_, _) => PlaceSplitter();
        return page;
    }

    bool _splitPlaced, _placingDetails;

    /// <summary>
    /// Shows the details panel beside the list when it is turned on and the list keeps room for its columns, at the
    /// width it was left at; otherwise the list takes the whole width.
    /// </summary>
    void PlaceDetails()
    {
        if (_listSplit == null || _placingDetails || _list.Columns.Count == 0) return;
        var area = _listSplit.Width;
        var columns = _list.Columns.Cast<ColumnHeader>().Take(_list.Columns.Count - 1).Sum(c => c.Width) + SystemInformation.VerticalScrollBarWidth;
        var show = _showDetails.Checked && _list.Items.Count > 0 && area > 0
            && ModDetails.Fits(area - _listSplit.SplitterWidth, columns, U(ModDetails.MinStatusWidth), U(ModDetails.MinPanelWidth));
        var wasShown = !_listSplit.Panel2Collapsed;
        _placingDetails = true;
        try
        {
            _listSplit.Panel2Collapsed = !show;
            if (show)
            {
                var width = ModDetails.Width(_s.DetailsWidth > 0 ? U(_s.DetailsWidth) : 0, area - _listSplit.SplitterWidth, columns, U(ModDetails.MinStatusWidth), U(ModDetails.MinPanelWidth));
                var distance = area - _listSplit.SplitterWidth - width;
                if (distance > 0 && _listSplit.SplitterDistance != distance) _listSplit.SplitterDistance = distance;
            }
        }
        catch (InvalidOperationException) { } // not laid out yet
        finally { _placingDetails = false; }
        if (show && !wasShown) UpdateDetail();
    }

    /// <summary>The buttons in the details panel: the things done most with the selected mod.</summary>
    void AddDetailActions()
    {
        _details.AddAction("Edit page", "Change the title, description or visibility on Steam with the next upload (Enter)", _tip, () => EditWorkshopPage(SelectedMod()));
        _details.AddAction("Change version", "Change the version to upload (F2)", _tip, () => { if (SelectedMod() is { } m) ChangeVersion(m); });
        _details.AddAction("Write change note", "Write the changelog entry this version still needs", _tip,
            () => { if (SelectedMod() is { } m && AskChangelogEntry(m, m.Version)) RefreshAll(); }, m => ModScanner.NeedsChangelogEntry(m, _cfg));
        _details.AddAction("Open on Steam", "Open the Workshop page in your browser", _tip, () => { if (SelectedMod() is { OnSteam: true } m) OpenUrl(WorkshopUrl(m)); }, m => m.OnSteam);
        _details.AddAction("Stats", "Subscribers over time and comments on Steam", _tip, () => ShowStats(SelectedMod()), m => m.OnSteam);
        _details.AddAction("Folder", "Open the mod's folder", _tip, () => { if (SelectedMod() is { } m) OpenPath(m.Dir); });
        _details.AddAction("Preview package", "See which files the upload would send, and their sizes", _tip, async () => { if (SelectedMod() is { } m) await PreviewPackageAsync(m); });
        _details.AddAction("History", "Every upload of this mod from this PC, and rolling back", _tip, () => { if (SelectedMod() is { } m) ShowHistory(m); }, m => m.OnSteam);
    }

    /// <summary>Puts the bar between list and log where it was left (or at a sensible default) once the page has its size.</summary>
    void PlaceSplitter()
    {
        if (_splitPlaced || _split.Height < U(300)) return;
        _splitPlaced = true;
        _populating = true;
        try
        {
            _split.Panel1MinSize = U(150);
            _split.Panel2MinSize = U(80);
            var log = _s.LogHeight > 0 ? U(_s.LogHeight) : Math.Max(U(100), _split.Height / 5);
            var distance = Math.Clamp(_split.Height - _split.SplitterWidth - log, _split.Panel1MinSize, _split.Height - _split.SplitterWidth - _split.Panel2MinSize);
            if (distance > 0) _split.SplitterDistance = distance;
        }
        catch (InvalidOperationException) { _splitPlaced = false; } // not laid out yet
        finally { _populating = false; }
    }

    /// <summary>Three dots in the middle of the bar between list and log, so it looks like something to drag.</summary>
    void PaintGrip(object? sender, PaintEventArgs e)
    {
        var r = _split.SplitterRectangle;
        using var b = new SolidBrush(Theme.Blend(Theme.Current.Back, Theme.Current.Muted, 0.6));
        var d = Math.Max(3, U(3));
        var cx = r.Left + r.Width / 2;
        var cy = r.Top + (r.Height - d) / 2;
        for (var i = -1; i <= 1; i++) e.Graphics.FillEllipse(b, cx + i * d * 3 - d / 2, cy, d, d);
        if (_split.Focused && FocusCues.Keyboard)
        {
            using var pen = new Pen(Theme.FocusColor, 2);
            e.Graphics.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
        }
    }

    /// <summary>A length in 96-DPI pixels at this screen's DPI and the chosen text size.</summary>
    int U(int logical) => LogicalToDeviceUnits((int)Math.Round(logical * Theme.TextScale));

    /// <summary>Sets which items of the mod's right-click menu apply to the selected mod (also before the command palette lists them).</summary>
    Action _refreshRowMenu = () => { };

    /// <summary>
    /// Ctrl+K: every button of the Upload tab, the selected mod's menu, the Options sections and their buttons and
    /// settings, searchable by name. The picked one runs once the palette has closed.
    /// </summary>
    void ShowPalette()
    {
        if (ActiveForm != this) return; // not over another dialog
        var entries = new List<PaletteEntry>();
        void AddButtons(Control root, string where, Func<ButtonBase, bool> include, Func<ButtonBase, Action> run)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ButtonBase { Enabled: true } b && include(b) && PaletteSearch.Clean(b.Text) is { Length: > 1 } name && !entries.Any(e => e.Name == name && e.Where == where))
                    entries.Add(new PaletteEntry(name, where, "", run(b)));
                AddButtons(c, where, include, run);
            }
        }
        // The Upload tab's buttons and boxes. While Options is showing nothing there reads as visible, so the two
        // buttons that come and go are judged by what they are for.
        bool OnUpload(ButtonBase b) => _uploadPage.Visible ? b.Visible : b == _cancel ? _busy : b == _retry ? _retrySet.Count > 0 && !_busy : true;
        if (HasUploader) AddButtons(_uploadPage, "Upload", OnUpload, b => () =>
        {
            ShowPage(_uploadPage);
            if (b is CheckBox x) x.Checked = !x.Checked;
            else if (b is Button btn) btn.PerformClick();
        });
        if (HasUploader && SelectedMod() is { } sel && _list.ContextMenuStrip is { } rowMenu && !_busy)
        {
            _refreshRowMenu();
            // Submenus are walked, and their items are listed with the submenu's name ("Copy: Workshop ID").
            void AddItems(ToolStripItemCollection items, string group)
            {
                foreach (ToolStripItem item in items)
                    if (item is ToolStripMenuItem { Available: true, Enabled: true } mi)
                    {
                        var name = PaletteSearch.Clean(mi.Text ?? "");
                        var full = group.Length > 0 ? group + ": " + name : name;
                        if (mi.HasDropDownItems) AddItems(mi.DropDownItems, full);
                        else entries.Add(new PaletteEntry(full, sel.Name, mi.ShortcutKeyDisplayString ?? "", mi.PerformClick));
                    }
            }
            AddItems(rowMenu.Items, "");
        }
        for (var i = 0; i < _sections.Count; i++)
        {
            var (tab, page) = _sections[i];
            if (!SectionShown(i)) continue;
            var index = i;
            entries.Add(new PaletteEntry("Options: " + tab.Text, "", "", () => ShowOptionSection(index)));
            AddButtons(page, "Options › " + tab.Text, _ => true, b => () =>
            {
                ShowOptions(b);
                if (b is Button btn) btn.PerformClick();
                else b.Focus(); // a setting: show it and let the user decide
            });
        }
        entries.Add(new PaletteEntry("Keyboard shortcuts", "", "F1", ShowShortcuts));
        using var f = new CommandPaletteForm(entries);
        f.Location = PointToScreen(new Point(Math.Max(0, (ClientSize.Width - f.Width) / 2), LogicalToDeviceUnits(60)));
        if (f.ShowDialog(this) == DialogResult.OK) f.Picked?.Run();
    }

    /// <summary>
    /// The mod list's right-click menu: the everyday actions at the top, the rest grouped into submenus by what they
    /// touch (Steam, the package, files, the clipboard, the ticked mods).
    /// </summary>
    ContextMenuStrip BuildRowMenu()
    {
        var menu = new ContextMenuStrip();
        ToolStripMenuItem Sub(string text) { var sub = new ToolStripMenuItem(text); menu.Items.Add(sub); return sub; }

        menu.Items.Add(new ToolStripMenuItem("Edit &Workshop page...", null, (_, _) => EditWorkshopPage(SelectedMod())) { ShortcutKeyDisplayString = "Enter" });
        menu.Items.Add(new ToolStripMenuItem("Change the &version to upload...", null, (_, _) => { if (SelectedMod() is { } m) ChangeVersion(m); }) { ShortcutKeyDisplayString = "F2" });
        var entry = menu.Items.Add("Write the &changelog entry...", null, (_, _) => { if (SelectedMod() is { } m && AskChangelogEntry(m, m.Version)) RefreshAll(); });
        menu.Items.Add(new ToolStripSeparator());

        var steam = Sub("On &Steam");
        var onSteam = steam.DropDownItems.Add("&Open on Steam", null, (_, _) => { if (SelectedMod() is { OnSteam: true } m) OpenUrl(WorkshopUrl(m)); });
        steam.DropDownItems.Add("Stats and comme&nts...", null, (_, _) => ShowStats(SelectedMod()));
        steam.DropDownItems.Add(new ToolStripSeparator());
        var tags = steam.DropDownItems.Add("Update Workshop &tags", null, async (_, _) => { if (SelectedMod() is { OnSteam: true } m) await UpdateTagsAsync(m); });
        var required = steam.DropDownItems.Add("Update &required items, gallery and languages", null, async (_, _) => { if (SelectedMod() is { OnSteam: true } m) await UpdatePageExtrasAsync(m); });
        steam.DropDownItems.Add(new ToolStripSeparator());
        var steamCopy = steam.DropDownItems.Add("Check what Steam &hands out", null, async (_, _) => { if (SelectedMod() is { OnSteam: true } m) await CheckSteamCopyAsync(m); });
        var download = steam.DropDownItems.Add("&Download from Steam...", null, async (_, _) => { if (SelectedMod() is { OnSteam: true } m) await DownloadFromSteamAsync(m); });
        steam.DropDownItems.Add("Try in a game &beta...", null, async (_, _) => await TryInGameBetaAsync(SelectedMod()));

        var package = Sub("&Package");
        package.DropDownItems.Add("&Preview package...", null, async (_, _) => { if (SelectedMod() is { } m) await PreviewPackageAsync(m); });
        package.DropDownItems.Add("Upload &history...", null, (_, _) => { if (SelectedMod() is { } m) ShowHistory(m); });
        var packages = package.DropDownItems.Add("Uploaded p&ackages", null, (_, _) => { if (SelectedMod() is { OnSteam: true } m) OpenPath(PackageArchive.FolderFor(PackageArchive.DefaultDir, m.WorkshopId!)); });
        var shrink = package.DropDownItems.Add("S&hrink preview image under 1 MB", null, (_, _) => { if (SelectedMod() is { } m) ShrinkPreview(m); });

        var open = Sub("&Open");
        open.DropDownItems.Add("Mod &folder", null, (_, _) => { if (SelectedMod() is { } m) OpenPath(m.Dir); });
        var changelog = open.DropDownItems.Add("Change&log", null, (_, _) => { if (ChangelogOf(SelectedMod()) is { } p) OpenPath(p); });

        var copy = Sub("Cop&y");
        var copyId = copy.DropDownItems.Add("Workshop &ID", null, (_, _) => { if (SelectedMod() is { OnSteam: true } m) Copy(m.WorkshopId!, "Workshop ID"); });
        var copyLink = copy.DropDownItems.Add("Workshop lin&k", null, (_, _) => { if (SelectedMod() is { OnSteam: true } m) Copy(WorkshopUrl(m), "Workshop link"); });
        var copyList = new ToolStripMenuItem("The mod &list as");
        foreach (var (label, format) in new[] { ("&Markdown table (README)", Reports.Markdown), ("&Steam BBCode (collection or profile)", Reports.BBCode), ("&CSV (spreadsheet)", Reports.Csv) })
            copyList.DropDownItems.Add(label, null, (_, _) => Copy(Reports.ModList(_rows.Select(it => (ModInfo)it.Tag!), format), "mod list"));
        copy.DropDownItems.Add(copyList);

        var ticked = Sub("&Ticked mods");
        var bulk = ticked.DropDownItems.Add("Workshop &pages...", null, async (_, _) => await EditTickedPagesAsync());
        var bulkVersion = ticked.DropDownItems.Add("Change the versi&on...", null, (_, _) => ChangeTickedVersions());
        var gameVersion = ticked.DropDownItems.Add("Add a &game version...", null, (_, _) => AddGameVersion());
        var thunder = menu.Items.Add("Publish to Th&understore", null, async (_, _) => await PublishThunderstoreAsync(TickedOrSelected().Where(x => Thunderstore.Has(x.Dir)).ToList()));
        var refreshLaunchPad = AddLaunchPadMenu(menu, package);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("&New mod...", null, async (_, _) => await NewModAsync()) { ShortcutKeyDisplayString = "Ctrl+N" });
        _refreshRowMenu = () =>
        {
            var onSteamNow = SelectedMod()?.OnSteam == true;
            steamCopy.Enabled = download.Enabled = onSteam.Enabled = copyId.Enabled = copyLink.Enabled = onSteamNow;
            tags.Available = WorkshopTags.LayoutHasTags(_cfg);
            tags.Enabled = required.Enabled = onSteamNow;
            bulk.Available = bulkVersion.Available = Selected().Count() > 1;
            gameVersion.Available = RimWorldLint.Applies(_cfg);
            ticked.Available = bulk.Available || gameVersion.Available;
            thunder.Available = TickedOrSelected().Any(x => Thunderstore.Has(x.Dir));
            changelog.Enabled = ChangelogOf(SelectedMod()) != null;
            entry.Available = SelectedMod() is { } m && ModScanner.NeedsChangelogEntry(m, _cfg);
            shrink.Available = SelectedMod() is { PreviewOk: false, PreviewPath.Length: > 0 };
            packages.Available = SelectedMod() is { OnSteam: true } pm && PackageArchive.List(PackageArchive.DefaultDir, pm.WorkshopId!).Count > 0;
            refreshLaunchPad();
        };
        menu.Opening += (_, e) =>
        {
            e.Cancel = _busy || SelectedMod() == null;
            _refreshRowMenu();
        };
        Theme.StyleMenu(menu);
        return menu;
    }

    /// <summary>"New mod...": makes a ready-to-upload mod folder for the current layout, then selects it.</summary>
    async Task NewModAsync()
    {
        if (_busy) return;
        if (!Directory.Exists(_s.RepoPath)) { BrowseRepo(); if (!Directory.Exists(_s.RepoPath)) return; }
        var root = ModScanner.ModsRoot(_s.RepoPath, _cfg);
        using var f = new NewModForm(root, _cfg, _s.ModAuthor, NewMod.NewestVersion(root));
        if (f.ShowDialog(this) != DialogResult.OK) return;
        string dir;
        try
        {
            Directory.CreateDirectory(root);
            dir = NewMod.Create(root, _cfg, f.Result);
        }
        catch (Exception e) { Append("Could not make the new mod: " + e.Message, LogKind.Error); return; }
        if (f.Result.Author.Length > 0) { _s.ModAuthor = f.Result.Author; _s.Save(); }
        Append($"Made {f.Result.Name} in {dir}." + (f.Result.CSharp ? " Build the project in Source to put the DLL in the version folder." : "") +
               " Replace the placeholder preview image before the first upload." +
               (NewMod.MissingMarker(dir, _cfg) is { } marker ? $" Add {marker} to it so the list shows it." : ""), LogKind.Ok);
        await RefreshAllAsync();
        foreach (ListViewItem it in _list.Items)
            if (it.Tag is ModInfo m && string.Equals(m.Dir, dir, StringComparison.OrdinalIgnoreCase))
            {
                _list.SelectedItems.Clear();
                it.Selected = it.Focused = true;
                it.EnsureVisible();
                _list.Focus();
                break;
            }
    }

    /// <summary>
    /// Sets the mod's Workshop tags on Steam without uploading it, for items uploaded before tags were set (or by
    /// SteamCMD, which leaves them alone).
    /// </summary>
    async Task UpdateTagsAsync(ModInfo m)
    {
        if (_busy || m.WorkshopId == null) return;
        var tags = WorkshopTags.For(m.Dir, _cfg);
        if (tags.Length == 0) { Append($"{m.Name}: the layout gives this mod no Workshop tags.", LogKind.Warn); return; }
        Append($"{m.Name}: setting Workshop tags {WorkshopTags.Describe(tags)} through the Steam client...", LogKind.Note);
        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var r = await WorkshopTags.UpdateOnSteamAsync(_cfg.AppId, m.WorkshopId, tags, _cts.Token);
            Append($"{m.Name}: " + (r.Ok ? r.Message : "the Workshop tags were not set: " + r.Message), r.Ok ? LogKind.Ok : LogKind.Error);
        }
        catch (OperationCanceledException) { Append("Cancelled.", LogKind.Warn); }
        finally { SetBusy(false); _cts.Dispose(); _cts = null; }
    }

    /// <summary>
    /// Sets the item's "Required items", gallery and translated descriptions on Steam from the mod's files, without
    /// uploading it (each part only when it is turned on in Options).
    /// </summary>
    async Task UpdatePageExtrasAsync(ModInfo m)
    {
        if (_busy || m.WorkshopId == null) return;
        var req = WorkshopExtras.For(_cfg.AppId, m.WorkshopId, _cfg, m.Dir, _s.UpdateRequired ? Dependencies.Find(m, _cfg) : Array.Empty<string>(), _s.UpdatePageExtras);
        if (req != null && !_s.UpdateRequired) req.Required = null;
        if (req == null || req.Required == null && req.Gallery == null && req.Languages == null)
        {
            Append($"{m.Name}: nothing to send. Required items come from Workshop links in its metadata" +
                   (string.IsNullOrWhiteSpace(_cfg.GalleryFolder) ? "" : $", the gallery from its {_cfg.GalleryFolder} folder") +
                   $", and translations from files like {Path.GetFileNameWithoutExtension(_cfg.SteamDescriptionFile)}.german{Path.GetExtension(_cfg.SteamDescriptionFile)} (unchanged ones are not sent again).", LogKind.Note);
            return;
        }
        Append($"{m.Name}: updating the Workshop page through the Steam client...", LogKind.Note);
        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var r = await WorkshopExtras.ApplyOnSteamAsync(req, _cts.Token);
            if (r.RequiredNow != null) WorkshopExtras.Remember(m.WorkshopId, r.RequiredNow);
            if (r.Ok && r.PageSent) WorkshopExtras.RememberSent(req);
            foreach (var d in r.Done) Append($"{m.Name}: {d}.", LogKind.Ok);
            if (!r.Ok) Append($"{m.Name}: the Workshop page was not fully updated: {r.Error}", LogKind.Error);
            else if (r.Done.Count == 0) Append($"{m.Name}: the Workshop page is already right.", LogKind.Ok);
        }
        catch (OperationCanceledException) { Append("Cancelled.", LogKind.Warn); }
        finally { SetBusy(false); _cts.Dispose(); _cts = null; }
    }

    /// <summary>Re-saves the mod's preview image under Steam's 1 MB limit; the original is backed up first.</summary>
    void ShrinkPreview(ModInfo m)
    {
        if (m.PreviewPath.Length == 0 || _busy) return;
        try
        {
            UseWaitCursor = true;
            Append($"{m.Name}: shrank " + PreviewImage.Shrink(m.PreviewPath, m.Name, ModScanner.MaxPreviewBytes), LogKind.Ok);
        }
        catch (Exception e) { Append($"{m.Name}: could not shrink the preview image: {e.Message}", LogKind.Error); }
        finally { UseWaitCursor = false; }
        RefreshAll();
    }

    static string WorkshopUrl(ModInfo m) => $"https://steamcommunity.com/sharedfiles/filedetails/?id={m.WorkshopId}";

    string? ChangelogOf(ModInfo? m)
    {
        if (m == null || string.IsNullOrWhiteSpace(_cfg.Changelog)) return null;
        var p = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.Changelog));
        return File.Exists(p) ? p : null;
    }

    /// <summary>The Options sections, in sidebar order: each one's button and the page of cards it shows.</summary>
    readonly List<(SideNavButton Tab, Control Page)> _sections = new();
    int _section;

    /// <summary>
    /// The Options tab, laid out like Steam's settings window: a list of sections down the left, and the selected
    /// section's cards on the right. Every setting keeps its control, so what is saved and how is unchanged.
    /// </summary>
    Control BuildOptionsPage()
    {
        // ---- Account
        _profileSave = Btn("Sa&ve as...", (_, _) => SaveProfile());
        _profileDelete = Btn("Delete", (_, _) => DeleteProfile());
        AddSection("Account", "The Steam account uploads go out under, and profiles for switching between projects.",
            Card("Steam account", null,
                Row(Lbl("Account &name"), _account, _checkLogin, _doLogin),
                Row(Lbl("Status"), _login),
                Row(_accountNote),
                Hint("You log in once in SteamCMD's own window and Steam keeps the login. This program never sees your password.")),
            Card("Profiles", null,
                Row(Lbl("&Profile"), _profile, _profileSave, _profileDelete),
                Hint("A profile remembers a mods folder, Steam account and SteamCMD together, for switching between projects or accounts in one click.")));

        // ---- Uploading: what happens before, during and after a run
        _autoRetry.Checked = _s.AutoRetry;
        _autoRetry.CheckedChanged += (_, _) => { _s.AutoRetry = _autoRetry.Checked; _s.Save(); };
        _updateTags.Checked = _s.UpdateTags;
        _updateTags.CheckedChanged += (_, _) => { _s.UpdateTags = _updateTags.Checked; _s.Save(); };
        _updateRequired.Checked = _s.UpdateRequired;
        _updateRequired.CheckedChanged += (_, _) => { _s.UpdateRequired = _updateRequired.Checked; _s.Save(); };
        _updatePageExtras.Checked = _s.UpdatePageExtras;
        _updatePageExtras.CheckedChanged += (_, _) => { _s.UpdatePageExtras = _updatePageExtras.Checked; _s.Save(); };
        _oneSession.Checked = _s.OneSession;
        _oneSession.CheckedChanged += (_, _) => { _s.OneSession = _oneSession.Checked; _s.Save(); };
        _notifyDone.Checked = _s.NotifyWhenDone;
        _notifyDone.CheckedChanged += (_, _) => { _s.NotifyWhenDone = _notifyDone.Checked; _s.Save(); };
        _strictAbout.Checked = _s.StrictAboutCheck;
        _strictAbout.CheckedChanged += (_, _) => { if (_populating) return; _s.StrictAboutCheck = _strictAbout.Checked; _s.Save(); RefreshAll(); };
        _verifyUploads.Checked = _s.VerifyUploads;
        _verifyUploads.CheckedChanged += (_, _) => { _s.VerifyUploads = _verifyUploads.Checked; _s.Save(); };
        _steamBack.Checked = _s.SteamBackOnline;
        _steamBack.CheckedChanged += (_, _) => { _s.SteamBackOnline = _steamBack.Checked; _s.Save(); };
        _webhookTest = Btn("Send t&est", async (_, _) => await TestWebhookAsync());
        _restartSteam = Btn("&Restart Steam now", async (_, _) => await RestartSteamNowAsync());
        AddSection("Uploading", "What runs before, during and after an upload.",
            Card("Before uploading", new Control[] { FolderTag() },
                Row(Lbl("&Build step"), _buildCmd),
                Hint("Run in the mods folder before anything is packaged, also for a dry run. If it fails, nothing is uploaded and its output is in the log. " +
                     "Kept in your settings, never in the repo."),
                Row(Lbl("Upload &from"), _packageRef),
                Hint("A git branch or tag to package instead of the checked-out commit (git packaging only), so a release branch can be uploaded while you work elsewhere. " +
                     "The version and change note are then read from that branch."),
                _strictAbout,
                Hint("A missing packageId or broken XML always blocks. With this on, so does what is only a warning otherwise: no author, description or supportedVersions, " +
                     "a dependency without a Workshop link, a load-order rule that names the mod itself or isn't a packageId.")),
            Card("Release checklist", new Control[] { FolderTag() },
                _checklist,
                _checklistMust,
                Hint("Your own steps before a release, one per line, for the current mods folder. The upload confirmation shows them as boxes to tick. " +
                     "Lines starting with # are left out. Dry runs never have to be ticked off.")),
            Card("While uploading", null,
                _autoRetry,
                _notifyDone,
                _oneSession,
                Hint("Without it, each mod gets its own SteamCMD run and login. With it, every ticked mod is packaged and checked first, then uploaded in one run."),
                _verifyUploads,
                Hint("Downloads each item again the way subscribers get it, so it doubles the traffic. A difference is only a warning; the upload has already gone through."),
                _keepPackages,
                Hint("For every mods folder. Right-click a mod > Package > Uploaded packages opens its zips: unzip one to look at or restore exactly what went out, without git."),
                _updateTags,
                Hint("SteamCMD cannot set tags, so they go through the Steam client: it has to be running and signed in as the account that owns the mod. " +
                     "Version tags the mod no longer lists are taken off; other tags are kept."),
                _updateRequired,
                Hint("Also through the Steam client. Players who subscribe are then offered the items the mod needs. Items this app added are taken off when " +
                     "the mod stops listing them; items you added by hand on Steam stay."),
                _updatePageExtras,
                Hint("Also through the Steam client. The gallery folder (RimWorld: Gallery in the mod, never packaged) holds PNG, JPG or GIF images and a videos.txt " +
                     "of YouTube links; the page's extra images and videos are made to match it. A file such as steam_description.german.txt (or .de.txt) " +
                     "is the German description, and a first line \"Title: ...\" sets the German title. Only changed images and translations are sent again."),
                _steamBack,
                Row(_restartSteam),
                Hint("Steam lets an account sign in once at a time, so SteamCMD's login leaves the Steam client offline. When Steam was running before the " +
                     "upload, it is closed and started again in the tray, and its saved login (\"Remember me\") signs it back in, before the tags and page " +
                     "steps above and once the batch ends. Not while a game is running, and not when Steam signs in as another account."),
                Hint("Upload to test items (next to Dry run) sends each ticked mod to its own private copy on Steam, created on first use and kept in " +
                     ModInfo.TestIdFile + " in the mod folder (keep it out of git). Subscribers get nothing; page changes and records wait for the real upload.")),
            Card("After uploading", new Control[] { FolderTag() },
                Row(Lbl("Discord &webhook"), _webhook, _webhookTest),
                Hint("After a successful update, its name, version, change note and Workshop link are posted to the Discord channel. " +
                     "New items are created private, so they are not announced. The webhook link is kept in your settings, never in the repo."),
                _ghReleases,
                Hint("GitHub releases are made on the uploaded commit, which must be pushed first."),
                Row(Lbl("&Then run"), _afterCmd),
                Hint("Run in the mods folder once for each mod that uploaded, for anything else you do after a release. It gets " +
                     string.Join(", ", AfterUpload.Variables) + " (the change note is in that file). A failure is only a warning: the upload already went through. " +
                     "Kept in your settings, never in the repo.")),
            Card("Thunderstore", new Control[] { FolderTag() },
                Row(Lbl("T&eam"), _tsTeam, Lbl("C&ommunity"), _tsCommunity),
                Row(Lbl("Categor&ies"), _tsCategories),
                Row(Lbl("To&ken"), _tsToken),
                _tsAfter,
                Hint("For BepInEx games (Sun Haven, Valheim, Lethal Company and others): a mod with Thunderstore's manifest.json, icon.png (256 x 256) and " +
                     "README.md in its folder can be published there too, from the same package as Steam, with the version to upload written into the manifest. " +
                     "Publish to Thunderstore is on the right-click menu. The team, community (the part after /c/ in the site's address) and categories are " +
                     "for this mods folder. The token is a service account token (thunderstore.io > Teams > Service accounts); it is kept encrypted for your " +
                     "Windows user, so an exported settings file does not carry a usable copy.")));

        // ---- Workshop page: the text that goes to Steam with an upload
        _uploadCredit.Checked = _s.UploadCredit;
        _markdownNotes.Checked = _s.MarkdownNotes;
        _countComments.Checked = _s.CountCommentsAtStartup;
        _countComments.CheckedChanged += (_, _) => { _s.CountCommentsAtStartup = _countComments.Checked; _s.Save(); };
        _descSync.Items.AddRange(new object[] { "Ask before sending it", "Always send it", "Never send it" });
        _descSync.SelectedIndex = _s.DescriptionSync switch { DescriptionSync.Always => 1, DescriptionSync.Never => 2, _ => 0 };
        _descSync.SelectedIndexChanged += (_, _) =>
        {
            _s.DescriptionSync = _descSync.SelectedIndex switch { 1 => DescriptionSync.Always, 2 => DescriptionSync.Never, _ => DescriptionSync.Ask };
            _s.Save();
        };
        AddSection("Workshop page", "The change note and description that go to each mod's Steam page with an upload.",
            Card("Change notes", null,
                _uploadCredit,
                _markdownNotes,
                Row(Lbl("&Template"), _noteTemplate),
                Hint("The template is for the current mods folder: {note} is the changelog entry, and {name}, {version}, {date} and {game} fill themselves in " +
                     "(e.g. \"{version} ({date})\" on the first line, then {note}). Without {note} the entry goes after it. For RimWorld, start with {version}, " +
                     "so the Steam version column can still read it. All of this changes only the note on Steam; the upload history and Discord announcements keep the entry as written.")),
            Card("Stats and comments", null,
                _countComments,
                Hint("Comments can only be counted by the Steam client, which then briefly shows you playing the game. Mods with new comments are named in the log " +
                     "and their row tooltip. Right-click a mod > On Steam > Stats and comments for charts of subscribers, favourites and visitors over time.")),
            Card("Description", null,
                Row(Lbl("When a mod's &description file differs from Steam"), _descSync),
                Hint("Checked when you upload a mod that is on Steam and has a description file (steam_description.txt for Generic). " +
                     "Public and unlisted items are compared with their page on Steam; other items with what this PC last sent. " +
                     "The new description goes out with the upload, like a change made in Workshop page..."),
                Row(Lbl("&Footer"), _descFooter),
                Hint("The footer is for the current mods folder: it goes at the end of every description sent from a description file, so a Discord or Ko-fi link " +
                     "is one edit for all your mods. {name}, {id} (the Workshop id) and {game} fill themselves in. Changing it counts as a changed description, " +
                     "so each mod offers its new description at its next upload. Steam BBCode works here.")));

        // ---- SteamCMD
        var openSteam = Btn("Open &folder", (_, _) => OpenPath(Path.GetDirectoryName(_s.EffectiveSteamCmd)!));
        var openLogs = Btn("Open &logs", (_, _) => OpenSteamCmdLogs());
        var branches = Btn("Game &branches", async (_, _) => await CheckGameBuildsAsync(true));
        var testInstall = Btn("Try in a game b&eta...", async (_, _) => await TryInGameBetaAsync(SelectedMod()));
        _watchBuilds.Checked = _s.WatchGameBuilds;
        _watchBuilds.CheckedChanged += (_, _) => { _s.WatchGameBuilds = _watchBuilds.Checked; _s.Save(); };
        AddSection("SteamCMD", "Valve's command-line Steam client, which every upload goes through.",
            Card("SteamCMD", null,
                Row(Lbl("Location"), _steamCmdPath, _install, openSteam, openLogs),
                Hint("Its own logs (workshop_log.txt and others) help when Steam turns an upload down."),
                Hint("Which login is used: game branches use Steam's anonymous login, so they work without an account. Uploading, downloading items and test installs " +
                     "use your saved login, and that account must own the game.")),
            Card("Game branches", null,
                Row(branches, testInstall),
                _watchBuilds,
                Hint("Game branches lists the game's public build and betas. Try in a game beta installs a separate copy of the game on a branch, with the selected mod " +
                     "and the Workshop items it needs, so you can test before the update reaches players.")));

        // ---- Git & GitHub
        _tagUploads.Checked = _s.TagUploads;
        _pushTags.Checked = _s.PushTags;
        _keepPackages.Checked = _s.KeepPackages;
        _commitIds.Checked = _s.CommitNewIds;
        var ghActions = Btn("Set up &GitHub Actions upload...", (_, _) => SetUpGitHubActions());
        var copySecret = Btn("Copy login &secret...", (_, _) => CopyLoginSecret());
        AddSection("Git & GitHub", "For a mods folder that is a git repository.",
            Card("Git", null,
                _tagUploads,
                _pushTags,
                _commitIds,
                Hint("Tags are made on the commit that was uploaded. Without Push they stay on this PC until you push them (git push --tags).")),
            Card("GitHub Actions upload", null,
                Row(ghActions, copySecret),
                Hint($"Writes {GitHubActions.WorkflowPath} into the mods folder: pushing a tag such as v1.2.0 then uploads every mod whose version " +
                     $"Steam does not have yet. It needs two repository secrets on GitHub: {GitHubActions.AccountSecret} (your login name) and {GitHubActions.LoginSecretName} " +
                     "(SteamCMD's saved login, which Copy login secret puts on the clipboard).")));

        // ---- Appearance
        _contrastNote = Hint("Windows High Contrast is on, so its colours are used. The theme picked here applies when it is off.");
        foreach (var size in Display.TextSizes) _textSize.Items.Add(size + "%");
        foreach (var t in Display.Themes) _theme.Items.Add(Localizer.T(t.Label));
        foreach (var c in Display.SidesChoices) _sidesChoice.Items.Add(Localizer.T(c.Label));
        _sidesChoice.SelectedIndex = Array.FindIndex(Display.SidesChoices, c => c.Name == Display.NormalizeSides(_s.Sides));
        _sidesChoice.SelectedIndexChanged += (_, _) => { if (_sidesChoice.SelectedIndex >= 0) SetSides(Display.SidesChoices[_sidesChoice.SelectedIndex].Name); };
        Tip(_textSize, "Makes all text, and the layout with it, larger than Windows' own scaling");
        ShowThemeChoice();
        AddSection("Appearance", "How Modwright looks for your Windows user.",
            Card("Theme and text", null,
                Row(Lbl("Theme"), _theme),
                _contrastNote,
                Row(Lbl("Te&xt size"), _textSize),
                Hint("The theme changes straight away; Match Windows follows Windows' light or dark app mode. " +
                     "A new text size is used the next time Modwright starts."),
                Row(_showDetails),
                Hint("Its preview image, numbers on Steam, change note, description and buttons, on the Launch Pad tab when the window is wide enough " +
                     "for the list's columns too. Drag the bar between the list and the details to resize them.")),
            Card("The Drop Pod and The Launch Pad", null,
                Row(Lbl("Sh&ow"), _sidesChoice),
                Hint("The mod manager (The Drop Pod) is for the RimWorld mods you play with; the uploader (The Launch Pad) puts your own mods on the Steam Workshop. " +
                     "Only what is picked here is shown, and switching changes nothing else: your settings, mods folder and modlists stay as they are.")),
            Card("Language", null,
                Row(Lbl("&Language"), _language),
                Hint("The windows' buttons, labels and menus in another language, from the next start. Log lines and messages stay in English. " +
                     "Anything a translation leaves out is shown in English."),
                Row(Btn("Make a &translation file...", (_, _) => SaveTranslationTemplate()), Btn("Open the lan&guages folder", (_, _) => OpenLanguagesFolder())),
                Hint("To fix or add a language, make a translation file, fill in the empty texts, and put it in the languages folder. " +
                     "Open each window you want covered first, so its text is in the file.")));
        FillLanguages();
        _language.SelectedIndexChanged += (_, _) => SetLanguage();

        // ---- About
        var version = (typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];
        _checkUpdates.Checked = _s.CheckForUpdates;
        _autoUpdate.Checked = _s.AutoUpdate;
        _autoUpdate.Enabled = _s.CheckForUpdates;
        _checkUpdateNow = Btn("Check now", async (_, _) => await CheckForUpdateAsync(true));
        var openData = Btn("Open &data folder", (_, _) => OpenPath(AppSettings.Folder));
        var forget = Btn("Forget recent folders", (_, _) =>
        {
            _s.RecentRepos.Clear();
            if (_s.RepoPath.Length > 0) _s.RecentRepos.Add(_s.RepoPath);
            _s.Save();
            FillRecent();
            Append("Recent mods folders forgotten (the current one is kept).", LogKind.Note);
        });
        AddSection("About", "Version and updates, where your data is kept, and help.",
            Card("Updates", null,
                Hint($"Modwright {version}."),
                Row(_checkUpdates, _checkUpdateNow, Btn("What's new...", (_, _) => ShowAllChanges())),
                _autoUpdate,
                _updateReady),
            Card("Setup check", null,
                Hint("Goes through everything an upload needs (the mods folder, its layout and app ID, SteamCMD and the saved login, git, gh, " +
                     "the Steam client, free disk space) and says what to fix. Nothing is changed."),
                Row(Btn("Check &setup", async (b, _) => await CheckSetupAsync((Control)b!)))),
            Card("Your data", null,
                Hint(AppSettings.Portable
                    ? $"Portable mode ({AppSettings.PortableMarker} is next to the program): settings, the recent folders list and the upload history are kept in {AppSettings.Folder}, so they travel with it."
                    : $"Settings, the recent folders list and the upload history are kept in %LocalAppData%\\SteamCmdHelper for your Windows user only. To keep them beside the program instead (on a USB stick, say), put an empty file named {AppSettings.PortableMarker} next to SteamCmdHelper.exe."),
                Row(openData, forget),
                Row(Btn("E&xport settings...", (_, _) => ExportSettings()), Btn("&Import settings...", (_, _) => ImportSettings()),
                    Btn("Export upload &history...", (_, _) => ExportHistory(UploadHistory.Load(), "upload-history.csv"))),
                Row(Btn("&Restore a backup...", (_, _) => RestoreSettingsBackup())),
                Hint($"A backup of the settings, upload history and the Drop Pod's modlists and notes is made once a day when Modwright starts (the newest {SettingsBackups.Keep} are kept)."),
                Hint("Export saves your settings, profiles, per-folder options (including Discord webhook links, so keep the file private), upload history and the Drop Pod's modlists and notes in one " +
                     "zip, to bring to another PC with Import. SteamCMD and its saved Steam login stay behind: log in once on the new PC.")),
            Card("Help", null,
                Row(Lbl("Everything works from the keyboard.").Role(ThemeRole.Muted), SmallBtn("Keyboard shortcuts (F1)", (_, _) => ShowShortcuts())),
                Hint(Disclaimer + " Steam and SteamCMD are trademarks of Valve Corporation.")));

        // The sidebar, then a hairline, then the selected section.
        var side = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(10, 14, 10, 12), Margin = Padding.Empty, AccessibleName = "Options sections", AccessibleRole = AccessibleRole.PageTabList };
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        foreach (var (tab, page) in _sections)
        {
            side.Controls.Add(tab);
            body.Controls.Add(page);
        }
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(side, 0, 0);
        root.Controls.Add(new Panel { Dock = DockStyle.Fill, Width = 1, Margin = Padding.Empty }.Role(ThemeRole.Divider), 1, 0);
        root.Controls.Add(body, 2, 0);
        side.TabIndex = 0;
        body.TabIndex = 1;
        ShowSection(0);
        return root;
    }

    /// <summary>One Options section: its sidebar button, and a scrolling page with its name, what it is for and its cards.</summary>
    void AddSection(string name, string blurb, params Control[] cards)
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 14, 14, 12), AutoScroll = true, Visible = false, AccessibleName = name };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(new Label { Text = name, AutoSize = true, Font = Theme.HeadingFont, Margin = new Padding(0, 0, 0, 2), UseMnemonic = false }.Role(ThemeRole.Title));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(new Label { Text = blurb, AutoSize = true, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false }.Role(ThemeRole.Muted));
        foreach (var c in cards)
        {
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Controls.Add(c);
        }
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Settings read best as one column; on a wide window keep it from stretching across the screen. The notes
        // wrap to the width the cards get.
        page.Resize += (_, _) =>
        {
            var right = Math.Max(14, page.ClientSize.Width - 16 - U(820));
            if (page.Padding.Right != right) page.Padding = new Padding(16, 14, right, 12);
            var text = Math.Max(U(240), page.ClientSize.Width - page.Padding.Horizontal - U(30) - (page.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
            foreach (var h in _hints) if (page.Contains(h) && h.MaximumSize.Width != text) h.MaximumSize = new Size(text, 0);
        };
        var index = _sections.Count;
        var tab = new SideNavButton { Text = name, Width = 170, AccessibleName = name };
        tab.Click += (_, _) => ShowSection(index);
        if (name is "Uploading" or "Workshop page" or "Git & GitHub") _uploaderSections.Add(tab);
        _sections.Add((tab, page));
    }

    /// <summary>A small note at the right of a card's title for settings kept per mods folder.</summary>
    static Label FolderTag() => new Label { Text = "For this mods folder", AutoSize = true, Font = Theme.SmallFont }.Role(ThemeRole.Muted);

    void ShowSection(int index)
    {
        _section = Math.Clamp(index, 0, _sections.Count - 1);
        if (!SectionShown(_section)) _section = 0; // the first section, Account, is always shown
        if (_windowRestored && _s.OptionsSection != _section) { _s.OptionsSection = _section; _s.Save(); }
        for (var i = 0; i < _sections.Count; i++)
        {
            _sections[i].Page.Visible = i == _section;
            _sections[i].Tab.Selected = i == _section;
        }
    }

    /// <summary>Opens the Options tab, on the section that holds <paramref name="target"/> when one is given.</summary>
    public void ShowOptions(Control? target = null)
    {
        ShowPage(_optionsPage);
        var i = target == null ? -1 : _sections.FindIndex(s => s.Page.Contains(target));
        if (i >= 0) ShowSection(i);
    }

    /// <summary>The Options section names, in order (for --shot).</summary>
    public IReadOnlyList<string> OptionSections => _sections.Select(s => s.Tab.Text).ToList();

    /// <summary>Opens the Options tab on the section with this index (for --shot).</summary>
    public void ShowOptionSection(int index) { ShowPage(_optionsPage); ShowSection(index); }

    /// <summary>Selects the first row, to show the selection colours (used by --shot).</summary>
    public void SelectFirstMod()
    {
        if (_list.Items.Count == 0) return;
        _list.Items[0].Selected = true;
        _list.Focus();
    }

    /// <summary>Whether the uploader (the Upload tab) is shown; see <see cref="AppSettings.Sides"/>.</summary>
    bool HasUploader => Display.ShowsUploader(_s.Sides);
    /// <summary>Whether the mod manager (the Drop Pod tab) is shown.</summary>
    bool HasDropPod => Display.ShowsDropPod(_s.Sides);
    /// <summary>The tab the window opens on: Upload, or the Drop Pod when the uploader is not picked.</summary>
    Control HomePage => HasUploader ? _uploadPage : _dropPodPage;

    bool SectionShown(int index) => HasUploader || !_uploaderSections.Contains(_sections[index].Tab);

    /// <summary>The next Options section that is shown, from <paramref name="from"/> one way or the other.</summary>
    int NextSection(int from, bool back)
    {
        var i = from;
        do i = (i + (back ? _sections.Count - 1 : 1)) % _sections.Count;
        while (!SectionShown(i) && i != from);
        return i;
    }

    /// <summary>Shows the tabs, Options sections and page for the sides picked.</summary>
    void ApplySides()
    {
        _navUpload.Visible = HasUploader;
        _navDropPod.Visible = HasDropPod;
        foreach (var (tab, _) in _sections) tab.Visible = !_uploaderSections.Contains(tab) || HasUploader;
        if (_optionsPage.Visible) { if (!SectionShown(_section)) ShowSection(0); }
        else ShowPage(HomePage);
    }

    /// <summary>Saves which sides to show (one of <see cref="Display.SidesChoices"/>) and switches to it live.</summary>
    void SetSides(string choice)
    {
        if (choice == Display.NormalizeSides(_s.Sides)) return;
        var hadUploader = HasUploader;
        _s.Sides = choice;
        _s.Save();
        ApplySides();
        if (HasUploader && !hadUploader) RefreshAll(); // not read while the uploader was off
        var label = Display.SidesChoices.First(c => c.Name == choice).Label;
        Append($"Showing: {label}.", LogKind.Note);
        Say(Localizer.T(label));
    }

    /// <summary>Which side was shown last (Options aside), kept for the next start in <see cref="AppSettings.OpenOnDropPod"/>.</summary>
    bool _onDropPod;

    void ShowPage(Control page)
    {
        if (page == _uploadPage && !HasUploader || page == _dropPodPage && !HasDropPod) page = HomePage;
        if (page == _optionsPage) SaveFields();
        _uploadPage.Visible = page == _uploadPage;
        _dropPodPage.Visible = page == _dropPodPage;
        _optionsPage.Visible = page == _optionsPage;
        _navUpload.Selected = page == _uploadPage;
        _navDropPod.Selected = page == _dropPodPage;
        _navOptions.Selected = page == _optionsPage;
        if (page != _optionsPage)
        {
            var dropPod = page == _dropPodPage;
            _onDropPod = dropPod;
            _side.Text = Localizer.T(dropPod ? "THE DROP POD" : "THE LAUNCH PAD");
            Text = Localizer.T(dropPod ? "Modwright - The Drop Pod" : "Modwright - The Launch Pad");
        }
        if (page == _dropPodPage && IsHandleCreated) DropPodShown(); // on opening, read once the window shows (see Shown)
    }

    /// <summary>Saves the theme choice (one of <see cref="Display.Themes"/>) and switches to it live.</summary>
    void SetTheme(string choice)
    {
        if (_populating || Theme.Choice == choice) return;
        _s.Theme = choice;
        _s.Save();
        if (Theme.Set(choice)) ReapplyTheme();
        ShowThemeChoice();
    }

    /// <summary>Ticks the saved theme, picks the text size, and says when High Contrast overrides the theme.</summary>
    void ShowThemeChoice()
    {
        _populating = true;
        _theme.SelectedIndex = Array.FindIndex(Display.Themes, t => t.Name == Theme.Choice);
        _textSize.SelectedItem = Display.NormalizeTextSize(_s.TextSize) + "%";
        _populating = false;
        _contrastNote.Visible = Theme.HighContrast;
    }

    void Tip(Control c, string text) => _tip.SetToolTip(c, Localizer.T(text));

    void FillLanguages()
    {
        _languages = Localizer.Available(Localizer.UserFolder);
        _populating = true;
        _language.Items.Clear();
        _language.Items.Add("English");
        foreach (var t in _languages) _language.Items.Add(t.Language);
        var at = _languages.FindIndex(t => t.Code.Equals(_s.Language, StringComparison.OrdinalIgnoreCase));
        _language.SelectedIndex = at + 1;
        _populating = false;
    }

    void SetLanguage()
    {
        if (_populating || _language.SelectedIndex < 0) return;
        var code = _language.SelectedIndex == 0 ? "" : _languages[_language.SelectedIndex - 1].Code;
        if (code == _s.Language) return;
        _s.Language = code;
        _s.Save();
        var name = _language.SelectedItem as string ?? code;
        Append($"Language set to {name}. It is used the next time Modwright starts.", LogKind.Note);
        Say($"{name} is used the next time Modwright starts.");
    }

    /// <summary>Writes a translation file with every window text seen so far, filled in from the language picked (or empty for a new one).</summary>
    void SaveTranslationTemplate()
    {
        var pick = _language.SelectedIndex > 0 ? _languages[_language.SelectedIndex - 1] : new Translation { Language = "New language", Code = "xx" };
        using var d = new SaveFileDialog { Filter = "Translation (*.json)|*.json", FileName = pick.Code + ".json", InitialDirectory = Directory.Exists(Localizer.UserFolder) ? Localizer.UserFolder : "" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        List<string> seen;
        lock (Localizer.Seen) seen = Localizer.Seen.ToList();
        try { File.WriteAllText(d.FileName, Localizer.Template(pick, seen, _languages)); }
        catch (Exception e) { Append("Could not write the translation file: " + e.Message, LogKind.Error); return; }
        Append($"Translation file written to {d.FileName}. Fill in the empty texts (keep the & before a letter, it marks the Alt key), " +
               $"set \"language\" and \"code\", and put it in {Localizer.UserFolder}.", LogKind.Ok);
    }

    void OpenLanguagesFolder()
    {
        try { Directory.CreateDirectory(Localizer.UserFolder); } catch { }
        OpenPath(Localizer.UserFolder);
    }

    void SetTextSize()
    {
        if (_populating || _textSize.SelectedItem is not string t || !int.TryParse(t.TrimEnd('%'), out var size)) return;
        if (size == Display.NormalizeTextSize(_s.TextSize)) return;
        _s.TextSize = size;
        _s.Save();
        Append($"Text size set to {size}%. It is used the next time Modwright starts.", LogKind.Note);
        Say($"Text size {size}% is used the next time Modwright starts.");
    }

    /// <summary>Colours everything again after the theme changed (a new choice, or Windows changed its mode or contrast).</summary>
    void ReapplyTheme()
    {
        SuspendLayout();
        Theme.Apply(this);
        Theme.StyleMenu(_list.ContextMenuStrip!);
        if (_help.Tag is ContextMenuStrip helpMenu) Theme.StyleMenu(helpMenu);
        _navUpload.Selected = _uploadPage.Visible;
        _navDropPod.Selected = _dropPodPage.Visible;
        _navOptions.Selected = _optionsPage.Visible;
        foreach (var it in _rows) ColorRow(it, (ModInfo)it.Tag!);
        ColorDropPodRows();
        UpdateDetail();
        SetLogin(_login.Text, _loginTone);
        ShowGame();
        UpdateSetup();
        ShowNote(_projectNote, _projectNote.Text);
        ShowNote(_accountNote, _accountNote.Text);
        UpdateDetail();
        RenderLog(); // High Contrast changes the log's colours too
        ResumeLayout(true);
        Invalidate(true);
    }

    /// <summary>Makes the Status column take the rest of the list's width, so there is no empty header strip.</summary>
    void StretchLastColumn()
    {
        if (_list.Columns.Count == 0) return;
        var last = _list.Columns[^1];
        var others = _list.Columns.Cast<ColumnHeader>().Where(c => c != last).Sum(c => c.Width);
        var w = Math.Max(120, _list.ClientSize.Width - others);
        if (last.Width != w) last.Width = w;
    }
}
