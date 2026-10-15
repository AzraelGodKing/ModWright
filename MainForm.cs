using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;

namespace SteamCmdHelper;

public sealed class MainForm : Form
{
    readonly AppSettings _s;
    ProjectConfig _cfg = new();

    readonly ComboBox _repo = new() { Tag = "fill", DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _account = new() { Width = 220 };
    readonly Label _branch = new Label { AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(240, 0) }.Role(ThemeRole.Muted);
    readonly TextBox _appId = new() { Width = 90 };
    readonly ComboBox _preset = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _game = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill" }.Role(ThemeRole.Keep);
    /// <summary>Says what to do when something can't be done yet ("pick the mods folder first"); hidden when empty.</summary>
    readonly Label _projectNote = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill", Visible = false }.Role(ThemeRole.Keep);
    readonly Label _accountNote = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill", Visible = false }.Role(ThemeRole.Keep);
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    readonly ToolTip _tip = new();
    readonly Label _login = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill" }.Role(ThemeRole.Keep);
    readonly LinkLabel _loginLink = new LinkLabel { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleRight, Tag = "fill", LinkBehavior = LinkBehavior.HoverUnderline }.Role(ThemeRole.Keep);
    readonly Label _steamCmdPath = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill" }.Role(ThemeRole.Muted);
    readonly RadioButton _themeDark = new() { AutoSize = true, Text = "Dark" };
    readonly RadioButton _themeLight = new() { AutoSize = true, Text = "Light" };
    readonly CheckBox _tagUploads = new() { AutoSize = true, Text = "Tag each upload in git as <mod folder>-v<version>" };
    readonly CheckBox _commitIds = new() { AutoSize = true, Text = "Commit a new item's Workshop ID file without asking" };
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    string _currentBase = "";
    DateTime _modStarted;
    double? _eta;
    readonly CheckBox _checkUpdates = new() { AutoSize = true, Text = "Check for a newer version at start-up" };
    readonly CheckBox _autoUpdate = new() { AutoSize = true, Text = "Install new versions automatically (downloaded in the background, installed when you close the app)" };
    readonly LinkLabel _updateReady = new LinkLabel { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Visible = false, Margin = new Padding(0, 4, 0, 0) }.Role(ThemeRole.Keep);
    readonly ListView _list = new();
    readonly TextBox _filter = new() { Width = 170, PlaceholderText = "Filter mods  (Ctrl+F)" };
    readonly CheckBox _hideUpToDate = new() { AutoSize = true, Text = "Hide up to date" };
    readonly FlatProgressBar _modBar = new() { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 6) };
    readonly FlatProgressBar _overall = new() { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 2) };
    readonly Label _current = new() { AutoSize = true, Text = "Idle", Tag = "fill" };
    readonly Label _stage = new Label { AutoSize = true }.Role(ThemeRole.Muted);
    readonly CheckBox _dry = new() { AutoSize = true, Text = "Dry run" };
    readonly CheckBox _allowCreate = new() { AutoSize = true, Text = "Create new items (private)" };
    readonly RichTextBox _log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None }.Role(ThemeRole.Log);

    // The empty mod list: what to do next, with the button that does it.
    readonly Panel _empty = new() { Dock = DockStyle.Fill, Visible = false };
    readonly Label _emptyTitle = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Anchor = AnchorStyles.None };
    readonly Label _emptyText = new Label { AutoSize = true, MaximumSize = new Size(520, 0), TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.None }.Role(ThemeRole.Muted);

    // "Get set up": the steps a new user still has to do, each a link that does it.
    readonly LinkLabel[] _steps = Enumerable.Range(0, 4).Select(_ => new LinkLabel { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 2, 24, 2) }.Role(ThemeRole.Keep)).ToArray();

    Button _help = null!, _checkUpdateNow = null!, _editPage = null!, _browse = null!, _refresh = null!, _all = null!, _none = null!, _go = null!, _cancel = null!, _retry = null!,
        _checkLogin = null!, _doLogin = null!, _install = null!, _editLayout = null!, _emptyBrowse = null!, _emptyLayout = null!, _emptyClear = null!;
    NavButton _navUpload = null!, _navOptions = null!;
    Control _uploadPage = null!, _optionsPage = null!, _setup = null!, _progress = null!;
    Tone _loginTone = Tone.Muted;

    List<ModInfo> _mods = new();
    /// <summary>Every row, shown or filtered out; the list view holds only the shown ones.</summary>
    readonly List<ListViewItem> _rows = new();
    /// <summary>Mod folders whose upload failed in the last run (badge "Failed"), and those to try again (failed or not run).</summary>
    readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase), _retrySet = new(StringComparer.OrdinalIgnoreCase);
    int _sortCol = -1;
    bool _sortDesc;
    readonly Dictionary<string, (DateTime Stamp, Image? Image)> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? _cts;
    bool _busy, _populating;
    int _idx, _count;

    /// <summary>Skips the start-up "install SteamCMD?" question (for --shot).</summary>
    public bool NoStartupPrompts { get; init; }

    /// <summary>The version this program replaced, when it was just started by an update.</summary>
    public string? UpdatedFrom { get; init; }

    /// <summary>A downloaded update that is installed when the window closes (or on "Restart now").</summary>
    PreparedUpdate? _pendingUpdate;

    public const string Disclaimer = "Not affiliated with or endorsed by Valve Corporation or Steam.";

    static readonly string[] ColumnNames = { "Mod", "Uploaded version", "Updated on Steam", "Version to upload", "Workshop ID", "Preview", "Status" };

    /// <param name="theme">Overrides the saved theme without saving it (used by --shot).</param>
    /// <param name="settings">Used instead of the saved settings (used by --shot).</param>
    public MainForm(string? theme = null, AppSettings? settings = null)
    {
        _s = settings ?? AppSettings.Load();
        if (_s.RecentRepos.Count == 0 && _s.RepoPath.Length > 0) _s.Remember(_s.RepoPath); // settings from before the recent list
        Theme.Set(theme ?? _s.Theme);
        BuildUi();
        FillRecent();
        _account.Text = _s.Account;
        _populating = true; // the Shown handler reads the folder once; these must not each trigger a read too
        _allowCreate.Checked = _s.AllowCreate;
        _hideUpToDate.Checked = _s.HideUpToDate;
        _populating = false;
        Shown += async (_, _) =>
        {
            RefreshAll();
            if (UpdatedFrom != null) Append($"Updated SteamCMD Helper from {UpdatedFrom} to {UpdateCheck.CurrentVersion}.", LogKind.Ok);
            if (!NoStartupPrompts) _ = Task.Run(() => SelfUpdate.CleanUp(SelfUpdate.AppDir)); // files the last update replaced
            if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { if (!NoStartupPrompts) await OfferInstallAsync(); }
            else if (SteamLogin.LooksCached(_s.EffectiveSteamCmd, _s.Account)) _ = VerifyLoginAsync(false);
            if (_s.CheckForUpdates && !NoStartupPrompts) await CheckForUpdateAsync(false);
        };
        Activated += (_, _) => { if (!_busy) Taskbar.Progress(this, 0, Taskbar.State.None); };
        FormClosing += (_, e) =>
        {
            if (!_busy) return;
            if (MessageBox.Show(this, "An upload is running. Stop it and close?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            Cancel();
        };
        FormClosed += (_, _) => InstallPendingUpdate(restart: false);
    }

    // ---------------------------------------------------------------- UI

    void BuildUi()
    {
        Text = "SteamCMD Helper - Workshop uploads";
        var screen = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1000, 800);
        Width = Math.Min(1000, screen.Width); Height = Math.Min(800, screen.Height); MinimumSize = new Size(840, 640);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Theme.Style(_tip);

        _browse = Btn("Browse...", (_, _) => BrowseRepo());
        _editLayout = Btn("Edit layout file...", (_, _) => EditLayout());
        _checkLogin = Btn("Check login", async (_, _) => { SaveFields(); await VerifyLoginAsync(true); });
        _doLogin = Btn("Log in...", async (_, _) => { SaveFields(); await LoginFlowAsync(); });
        _install = Btn("Install SteamCMD", async (_, _) => await InstallAsync());
        _refresh = Btn("Refresh", (_, _) => RefreshAll());
        _all = Btn("Tick all", (_, _) => TickAll(true));
        _none = Btn("Tick none", (_, _) => TickAll(false));
        _editPage = Btn("Workshop page...", (_, _) => EditWorkshopPage(SelectedMod()));
        _go = Btn("Upload selected", async (_, _) => await GoAsync()).Role(ThemeRole.Accent);
        _go.Font = new Font(Font, FontStyle.Bold);
        _go.Padding = new Padding(14, 4, 14, 4);
        _cancel = Btn("Cancel", (_, _) => Cancel());
        _cancel.Visible = false;
        _retry = Btn("Retry failed", async (_, _) => await RetryFailedAsync());
        _retry.Visible = false;
        _emptyBrowse = Btn("Browse...", (_, _) => BrowseRepo()).Role(ThemeRole.Accent);
        _emptyLayout = Btn("Edit layout file...", (_, _) => EditLayout());
        _emptyClear = Btn("Clear the filter", (_, _) => { _filter.Text = ""; _hideUpToDate.Checked = false; });

        _uploadPage = BuildUploadPage();
        _optionsPage = BuildOptionsPage();
        var pages = new Panel { Dock = DockStyle.Fill };
        pages.Controls.Add(_uploadPage);
        pages.Controls.Add(_optionsPage);

        // Fill first, then the top bar, so docking puts the bar above the pages.
        Controls.Add(pages);
        Controls.Add(new Label { Text = Disclaimer, Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 16, 2), Font = new Font("Segoe UI", 8.25f) }.Role(ThemeRole.Muted));
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1 }.Role(ThemeRole.Divider));
        Controls.Add(BuildHeader());
        ShowPage(_uploadPage);

        Theme.Apply(this);
        EnableFolderDrop(this);

        _repo.SelectionChangeCommitted += (_, _) => { if (_repo.SelectedItem is string p && !_busy) OpenRepo(p); };
        _account.Leave += (_, _) => { SaveFields(); UpdateLoginHint(); UpdateSetup(); };
        _account.TextChanged += (_, _) => ShowNote(_accountNote, null);
        _allowCreate.CheckedChanged += (_, _) => { if (!_populating) { SaveFields(); RefreshAll(); } };
        _appId.Leave += async (_, _) => await ApplyAppIdAsync();
        _appId.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyAppIdAsync(); } };
        _preset.SelectionChangeCommitted += (_, _) => ApplyPresetChoice();
        _themeDark.CheckedChanged += (_, _) => { if (_themeDark.Checked) SetTheme(Theme.DarkName); };
        _themeLight.CheckedChanged += (_, _) => { if (_themeLight.Checked) SetTheme(Theme.LightName); };
        _loginLink.LinkClicked += (_, _) => ShowPage(_optionsPage);
        _tagUploads.CheckedChanged += (_, _) => { if (!_populating) { _s.TagUploads = _tagUploads.Checked; _s.Save(); } };
        _commitIds.CheckedChanged += (_, _) => { if (!_populating) { _s.CommitNewIds = _commitIds.Checked; _s.Save(); } };
        _clock.Tick += (_, _) => ShowClock();
        _checkUpdates.CheckedChanged += (_, _) =>
        {
            _autoUpdate.Enabled = _checkUpdates.Checked;
            if (!_populating) { _s.CheckForUpdates = _checkUpdates.Checked; _s.Save(); }
        };
        _autoUpdate.CheckedChanged += (_, _) => { if (!_populating) { _s.AutoUpdate = _autoUpdate.Checked; _s.Save(); } };
        _updateReady.LinkClicked += (_, _) => RestartToUpdate();
        _filter.TextChanged += (_, _) => ApplyFilter();
        _filter.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && _filter.Text.Length > 0) { _filter.Text = ""; e.SuppressKeyPress = true; } };
        _hideUpToDate.CheckedChanged += (_, _) => { if (_populating) return; _s.HideUpToDate = _hideUpToDate.Checked; _s.Save(); ApplyFilter(); };
    }

    /// <summary>The top bar: app name on the left, the Upload / Options tabs and the "?" button on the right.</summary>
    Control BuildHeader()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, RowCount = 1, Padding = new Padding(16, 8, 12, 0) }.Role(ThemeRole.Header);
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 6), Anchor = AnchorStyles.Left };
        if (Theme.AppIcon != null)
            title.Controls.Add(new PictureBox { Image = new Icon(Theme.AppIcon, 32, 32).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(26, 26), Margin = new Padding(0, 1, 8, 0) });
        title.Controls.Add(new Label { Text = "SteamCMD Helper", AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 0, 8, 0) });
        title.Controls.Add(new Label { Text = "Steam Workshop uploads", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }.Role(ThemeRole.Muted));
        bar.Controls.Add(title, 0, 0);

        _navUpload = new NavButton { Text = "&Upload" };
        _navOptions = new NavButton { Text = "&Options" };
        _navUpload.Click += (_, _) => ShowPage(_uploadPage);
        _navOptions.Click += (_, _) => ShowPage(_optionsPage);
        _navUpload.Anchor = _navOptions.Anchor = AnchorStyles.Bottom;
        bar.Controls.Add(_navUpload, 2, 0);
        bar.Controls.Add(_navOptions, 3, 0);

        _help = Btn("?", (_, _) => OpenRequest());
        _help.AutoSize = false; _help.Size = new Size(30, 28); _help.Padding = Padding.Empty;
        _help.Font = new Font(Font, FontStyle.Bold);
        _help.Margin = new Padding(12, 0, 0, 6);
        _help.Anchor = AnchorStyles.None;
        _tip.SetToolTip(_help, "Send a request: support another game, report a bug or suggest a feature");
        bar.Controls.Add(_help, 4, 0);
        return bar;
    }

    Control BuildUploadPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14, 12, 14, 12) };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); // the list and the log share what is left, so neither vanishes on small screens

        // Shown until SteamCMD, the account, the mods folder and the app id are all set.
        // One line, so it costs little height: the title, then the steps.
        var steps = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Tag = "fill" };
        foreach (var l in _steps)
        {
            l.LinkClicked += (_, _) => (l.Tag as Action)?.Invoke();
            steps.Controls.Add(l);
        }
        var setupTitle = new Label { Text = "Get set up", AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f) }.Role(ThemeRole.Title);
        _tip.SetToolTip(setupTitle, "Uploads need all four steps. This box goes away once they are done.");
        var setupRow = Row(setupTitle, steps);
        setupTitle.Margin = new Padding(0, 3, 20, 3);
        setupRow.Margin = Padding.Empty;
        _setup = Card(null, null, setupRow);
        page.Controls.Add(_setup, 0, 0);

        _preset.Items.AddRange(ProjectConfig.PresetNames.Cast<object>().Append("Custom").ToArray());
        _tip.SetToolTip(_repo, "Your mods folders; pick one you used before, press Browse, or drop a folder onto this window");
        _tip.SetToolTip(_browse, "Pick the folder that holds your mods (Ctrl+O)");
        page.Controls.Add(Card("Project", null,
            Row(Lbl("Mods repo"), _repo, _browse, _branch),
            Row(Lbl("Steam app ID"), _appId, Lbl("Layout"), _preset, _editLayout),
            Row(_game),
            Row(_projectNote)), 0, 1);

        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Dock = DockStyle.Fill;
        _list.ShowItemToolTips = true;
        // Taller rows leave room for the preview thumbnail and the status badge.
        _list.SmallImageList = new ImageList { ImageSize = new Size(1, LogicalToDeviceUnits(28)) };
        int[] widths = { 200, 125, 120, 125, 105, 70, 190 };
        for (var i = 0; i < ColumnNames.Length; i++)
            _list.Columns.Add(ColumnNames[i], widths[i], i == PreviewCol ? HorizontalAlignment.Center : HorizontalAlignment.Left);
        _list.PaintCells(PaintCell);
        _list.ItemCheck += OnItemCheck;
        // Windows reports ticks while it is still taking rows in (when the list is filled or its window is created),
        // and a row's tick can't be read until it has its place; so count them once the current message is done.
        _list.ItemChecked += (_, _) => QueueGoButton();
        _list.MouseDoubleClick += OnListDoubleClick;
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _list.SelectedIndexChanged += (_, _) => UpdateSelectionButtons();
        _list.GotFocus += (_, _) => _list.Invalidate();
        _list.LostFocus += (_, _) => _list.Invalidate();
        _list.Resize += (_, _) => StretchLastColumn();
        _list.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex < _list.Columns.Count - 1) StretchLastColumn(); };
        _tip.SetToolTip(_editPage, "Change the selected mod's title, description or visibility on Steam with its next upload");
        _tip.SetToolTip(_hideUpToDate, "Hide the mods whose version on Steam is already the one to upload");
        _tip.SetToolTip(_refresh, "Read the mods folder again (F5)");
        _list.ContextMenuStrip = BuildRowMenu();

        // The list and its empty state share one spot; only one of them is visible.
        var emptyBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.None };
        var emptyButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.None, Margin = new Padding(0, 6, 0, 0) };
        foreach (var b in new[] { _emptyBrowse, _emptyLayout, _emptyClear }) { b.Margin = new Padding(4, 0, 4, 0); emptyButtons.Controls.Add(b); }
        _emptyTitle.Margin = new Padding(0, 0, 0, 2);
        emptyBox.Controls.AddRange(new Control[] { _emptyTitle, _emptyText, emptyButtons });
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.Controls.Add(emptyBox, 0, 1);
        _empty.Controls.Add(center);
        var listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_empty);

        var mods = Card("Mods", new Control[] { _filter, _hideUpToDate, _editPage, _all, _none, _refresh }, listHost);
        mods.AutoSize = false; mods.Dock = DockStyle.Fill;
        page.Controls.Add(mods, 0, 2);

        // Only while uploading and afterwards (with the result); nothing to show before the first run.
        _progress = Card(null, null,
            Row(_current, _stage),
            _modBar,
            Lbl("Overall").Role(ThemeRole.Muted),
            _overall);
        _progress.Visible = false;
        page.Controls.Add(_progress, 0, 3);

        _tip.SetToolTip(_loginLink, "Steam login settings are on the Options tab");
        _tip.SetToolTip(_all, "Tick every mod that is ready to upload, then upload them in one go");
        _tip.SetToolTip(_dry, "Check everything and build the upload, but send nothing to Steam");
        _tip.SetToolTip(_allowCreate, "Mods that are not on Steam yet are created as new private Workshop items");
        _tip.SetToolTip(_go, "Upload the ticked mods (Ctrl+Enter)");
        _tip.SetToolTip(_retry, "Upload again only the mods that failed or were not reached in the last run");
        var actions = Row(_dry, _allowCreate, _loginLink, _retry, _cancel, _go);
        actions.Margin = new Padding(0, 2, 0, 8);
        page.Controls.Add(actions, 0, 4);

        var copy = SmallBtn("Copy", (_, _) => CopyLog());
        var save = SmallBtn("Save...", (_, _) => SaveLog());
        var clear = SmallBtn("Clear", (_, _) => _log.Clear());
        var steamLogs = SmallBtn("SteamCMD logs", (_, _) => OpenSteamCmdLogs());
        _tip.SetToolTip(copy, "Copy the whole log");
        _tip.SetToolTip(steamLogs, "Open SteamCMD's own log folder (workshop_log.txt and others)");
        var log = Card("Log", new Control[] { copy, save, clear, steamLogs }, _log);
        log.AutoSize = false; log.Dock = DockStyle.Fill;
        log.Margin = Padding.Empty;
        page.Controls.Add(log, 0, 5);
        return page;
    }

    ContextMenuStrip BuildRowMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Edit Workshop page...", null, (_, _) => EditWorkshopPage(SelectedMod()));
        menu.Items.Add("Upload history...", null, (_, _) => { if (SelectedMod() is { } m) ShowHistory(m); });
        menu.Items.Add("Preview package...", null, async (_, _) => { if (SelectedMod() is { } m) await PreviewPackageAsync(m); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open mod folder", null, (_, _) => { if (SelectedMod() is { } m) OpenPath(m.Dir); });
        var changelog = menu.Items.Add("Open changelog", null, (_, _) => { if (ChangelogOf(SelectedMod()) is { } p) OpenPath(p); });
        menu.Items.Add(new ToolStripSeparator());
        var onSteam = menu.Items.Add("Open on Steam", null, (_, _) => { if (SelectedMod() is { OnSteam: true } m) OpenUrl(WorkshopUrl(m)); });
        var copyId = menu.Items.Add("Copy Workshop ID", null, (_, _) => { if (SelectedMod() is { OnSteam: true } m) Copy(m.WorkshopId!, "Workshop ID"); });
        var copyLink = menu.Items.Add("Copy Workshop link", null, (_, _) => { if (SelectedMod() is { OnSteam: true } m) Copy(WorkshopUrl(m), "Workshop link"); });
        menu.Opening += (_, e) =>
        {
            e.Cancel = _busy || SelectedMod() == null;
            onSteam.Enabled = copyId.Enabled = copyLink.Enabled = SelectedMod()?.OnSteam == true;
            changelog.Enabled = ChangelogOf(SelectedMod()) != null;
        };
        Theme.StyleMenu(menu);
        return menu;
    }

    static string WorkshopUrl(ModInfo m) => $"https://steamcommunity.com/sharedfiles/filedetails/?id={m.WorkshopId}";

    string? ChangelogOf(ModInfo? m)
    {
        if (m == null || string.IsNullOrWhiteSpace(_cfg.Changelog)) return null;
        var p = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.Changelog));
        return File.Exists(p) ? p : null;
    }

    Control BuildOptionsPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14, 12, 14, 12), AutoScroll = true };
        for (var i = 0; i < 6; i++) page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Settings read best as one column; on a wide window keep it from stretching across the screen.
        page.Resize += (_, _) =>
        {
            var right = Math.Max(14, page.ClientSize.Width - 14 - LogicalToDeviceUnits(820));
            if (page.Padding.Right != right) page.Padding = new Padding(14, 12, right, 12);
        };

        _themeDark.Checked = Theme.Current.Dark;
        _themeLight.Checked = !Theme.Current.Dark;
        page.Controls.Add(Card("Appearance", null,
            Row(Lbl("Theme"), _themeDark, _themeLight),
            Hint("Saved for your Windows user and applied straight away.")), 0, 0);

        page.Controls.Add(Card("Steam account", null,
            Row(Lbl("Account name"), _account, _checkLogin, _doLogin),
            Row(Lbl("Status"), _login),
            Row(_accountNote),
            Hint("You log in once in SteamCMD's own window and Steam keeps the login. This program never sees your password.")), 0, 1);

        var openSteam = Btn("Open folder", (_, _) => OpenPath(Path.GetDirectoryName(_s.EffectiveSteamCmd)!));
        var openLogs = Btn("Open logs", (_, _) => OpenSteamCmdLogs());
        page.Controls.Add(Card("SteamCMD", null,
            Row(Lbl("Location"), _steamCmdPath, _install, openSteam, openLogs),
            Hint("SteamCMD is Valve's command-line Steam client; every upload goes through it. Its own logs (workshop_log.txt and others) help when Steam turns an upload down.")), 0, 2);

        _tagUploads.Checked = _s.TagUploads;
        _commitIds.Checked = _s.CommitNewIds;
        page.Controls.Add(Card("Git", null,
            _tagUploads,
            _commitIds,
            Hint("Used when the mods folder is a git repository. Tags are made on the commit that was uploaded and stay on this PC until you push them (git push --tags).")), 0, 3);

        var openData = Btn("Open data folder", (_, _) => OpenPath(AppSettings.Folder));
        var forget = Btn("Forget recent folders", (_, _) =>
        {
            _s.RecentRepos.Clear();
            if (_s.RepoPath.Length > 0) _s.RecentRepos.Add(_s.RepoPath);
            _s.Save();
            FillRecent();
            Append("Recent mods folders forgotten (the current one is kept).", LogKind.Note);
        });
        page.Controls.Add(Card("Your data", null,
            Hint("Settings, the recent folders list and the upload history are kept in %LocalAppData%\\SteamCmdHelper for your Windows user only."),
            Row(openData, forget)), 0, 4);

        var version = (typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];
        _checkUpdates.Checked = _s.CheckForUpdates;
        _autoUpdate.Checked = _s.AutoUpdate;
        _autoUpdate.Enabled = _s.CheckForUpdates;
        _checkUpdateNow = Btn("Check now", async (_, _) => await CheckForUpdateAsync(true));
        page.Controls.Add(Card("About", null,
            Hint($"SteamCMD Helper {version}."),
            Row(_checkUpdates, _checkUpdateNow),
            _autoUpdate,
            _updateReady,
            Hint("Shortcuts: F5 refresh, Ctrl+O pick the mods folder, Ctrl+F filter the list, Ctrl+Enter upload."),
            Hint(Disclaimer + " Steam and SteamCMD are trademarks of Valve Corporation.")), 0, 5);
        return page;
    }

    public void ShowOptions() => ShowPage(_optionsPage);

    /// <summary>Selects the first row, to show the selection colours (used by --shot).</summary>
    public void SelectFirstMod()
    {
        if (_list.Items.Count == 0) return;
        _list.Items[0].Selected = true;
        _list.Focus();
    }

    void ShowPage(Control page)
    {
        if (page == _optionsPage) SaveFields();
        _uploadPage.Visible = page == _uploadPage;
        _optionsPage.Visible = page == _optionsPage;
        _navUpload.Selected = page == _uploadPage;
        _navOptions.Selected = page == _optionsPage;
    }

    /// <summary>Switches the theme live and saves the choice.</summary>
    void SetTheme(string name)
    {
        if (_populating || Theme.Current.Name == name) return;
        Theme.Set(name);
        _s.Theme = Theme.Current.Name;
        _s.Save();
        SuspendLayout();
        Theme.Apply(this);
        Theme.StyleMenu(_list.ContextMenuStrip!);
        _navUpload.Selected = _uploadPage.Visible;
        _navOptions.Selected = _optionsPage.Visible;
        foreach (var it in _rows) ColorRow(it, (ModInfo)it.Tag!);
        SetLogin(_login.Text, _loginTone);
        ShowGame();
        UpdateSetup();
        ShowNote(_projectNote, _projectNote.Text);
        ShowNote(_accountNote, _accountNote.Text);
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

    // ---------------------------------------------------------------- keyboard and drag-and-drop

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5 when !_busy:
                RefreshAll(); return true;
            case Keys.Control | Keys.O when !_busy:
                BrowseRepo(); return true;
            case Keys.Control | Keys.F:
                ShowPage(_uploadPage); _filter.Focus(); _filter.SelectAll(); return true;
            case Keys.Control | Keys.Enter when _go.Enabled:
                _go.PerformClick(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Dropping a folder (or a file inside one) anywhere on the window opens it as the mods folder.</summary>
    void EnableFolderDrop(Control c)
    {
        c.AllowDrop = true;
        c.DragEnter += (_, e) => e.Effect = !_busy && DroppedFolder(e.Data) != null ? DragDropEffects.Link : DragDropEffects.None;
        c.DragDrop += (_, e) => { if (!_busy && DroppedFolder(e.Data) is { } dir) OpenRepo(dir); };
        foreach (Control child in c.Controls) EnableFolderDrop(child);
    }

    static string? DroppedFolder(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return null;
        var p = paths[0];
        if (Directory.Exists(p)) return p;
        return File.Exists(p) ? Path.GetDirectoryName(p) : null;
    }

    // ---------------------------------------------------------------- game / layout

    /// <summary>The "?" button: a layout request, bug report or feature request, sent to Linear.</summary>
    void OpenRequest()
    {
        // Show one mod's file names (never contents) so the request says what a mod looks like.
        var mod = (_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag : _rows.Count > 0 ? _rows[0].Tag : null) as ModInfo;
        var names = mod != null ? RequestForm.ListFileNames(mod.Dir) : "";
        using var f = new RequestForm(_cfg.GameName, _cfg.AppId, _cfg.PresetName(), names);
        f.ShowDialog(this);
    }

    /// <summary>The user typed an app id: validate it, look the game name up on Steam, and save it to the layout file.</summary>
    async Task ApplyAppIdAsync()
    {
        if (_populating || _busy) return;
        if (_cfg.AppIdLocked) { _appId.Text = _cfg.AppId; return; }
        var id = _appId.Text.Trim();
        if (id == _cfg.AppId) return;
        if (id.Length > 0 && !id.All(char.IsDigit)) { Nudge(_appId, _projectNote, "The app ID is a number, e.g. 294100 for RimWorld (it is in the game's Steam store URL)."); return; }
        if (!Directory.Exists(_s.RepoPath)) { _appId.Text = _cfg.AppId; Nudge(_repo, _projectNote, "Pick the mods folder first; the app ID is saved in it."); return; }

        ShowNote(_projectNote, null);
        _cfg.AppId = id;
        _cfg.GameName = "";
        if (id.Length > 0) _cfg.GameName = await LookupGameNameAsync(id) ?? "";
        SaveLayout(id.Length > 0 ? $"App ID set to {id}{(_cfg.GameName.Length > 0 ? " (" + _cfg.GameName + ")" : "")}." : "App ID cleared.");
        ShowGame();
        UpdateSetup();
        UpdateGoButton();
    }

    void ApplyPresetChoice()
    {
        var name = _preset.SelectedItem as string ?? "";
        if (!Directory.Exists(_s.RepoPath)) { ShowGame(); Nudge(_repo, _projectNote, "Pick the mods folder first; the layout is saved in it."); return; }
        ShowNote(_projectNote, null);
        if (name == ProjectConfig.CustomName)
        {
            if (_cfg.PresetName() == ProjectConfig.CustomName) { ShowGame(); return; }
            _cfg.MakeCustom(); // keeps the current built-in layout's fields as the starting point
            SaveLayout("Layout set to Custom (editable).");
            ShowGame();
            return;
        }
        _cfg.ApplyPreset(name);
        SaveLayout($"Layout set to {name} (built in, locked).");
        RefreshAll();
    }

    void SaveLayout(string what)
    {
        try { _cfg.Save(_s.RepoPath); Append(what + " Saved to " + ProjectConfig.FileName + " in the repo.", LogKind.Note); }
        catch (Exception e) { Append("Could not save " + ProjectConfig.FileName + ": " + e.Message, LogKind.Error); }
    }

    static async Task<string?> LookupGameNameAsync(string appId)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId, out var e)) return null;
            if (!e.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
            return e.GetProperty("data").GetProperty("name").GetString();
        }
        catch { return null; }
    }

    void ShowGame()
    {
        _populating = true;
        _appId.Text = _cfg.AppId;
        var preset = _cfg.PresetName();
        _preset.SelectedItem = preset;
        _populating = false;
        if (!_cfg.AppIdValid)
        {
            _game.Text = "Enter the game's Steam app ID (the number in its store URL) before uploading.";
            _game.ForeColor = Theme.Of(Tone.Warn);
        }
        else
        {
            _game.Text = $"{(_cfg.GameName.Length > 0 ? _cfg.GameName : "App " + _cfg.AppId)}  -  layout: {_cfg.Source}"
                         + (_cfg.IsBuiltIn ? " (locked; choose Custom to make your own)" : "");
            _game.ForeColor = Theme.Of(Tone.Muted);
        }
        _tip.SetToolTip(_game, _game.Text); // full text when it is cut off with "..."
        ApplyLocks();
    }

    /// <summary>Built-in layouts are read-only: no editing the layout file, and no changing the app id when the layout fixes it.</summary>
    void ApplyLocks()
    {
        _editLayout.Enabled = !_busy && !_cfg.IsBuiltIn;
        _appId.Enabled = !_busy && !_cfg.AppIdLocked;
        _tip.SetToolTip(_editLayout, _cfg.IsBuiltIn ? "Built-in layouts are locked; choose Custom in the Layout box to edit one" : "Open the layout file (" + ProjectConfig.FileName + ") in your editor");
    }

    static Label Lbl(string t) => new() { Text = t, AutoSize = true };

    static Label Hint(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 2, 0, 2) }.Role(ThemeRole.Muted);

    /// <summary>A raised panel with an optional title (and controls to the right of it) over its rows.</summary>
    static TableLayoutPanel Card(string? title, Control[]? titleExtras, params Control[] rows)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12, 10, 12, 8), Margin = new Padding(0, 0, 0, 10) }.Role(ThemeRole.Surface);
        if (title != null)
        {
            var head = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Tag = "fill" }.Role(ThemeRole.Title);
            var r = Row(new Control[] { head }.Concat(titleExtras ?? Array.Empty<Control>()).ToArray());
            r.Margin = new Padding(0, 0, 0, 4);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(r);
        }
        foreach (var c in rows)
        {
            var fill = c.Dock == DockStyle.Fill;
            card.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            card.Controls.Add(c);
        }
        return card;
    }

    static Button Btn(string text, EventHandler h)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };
        b.Click += h;
        return b;
    }

    /// <summary>A compact button for a card's title row.</summary>
    static Button SmallBtn(string text, EventHandler h)
    {
        var b = Btn(text, h);
        b.Padding = new Padding(4, 0, 4, 0);
        b.Font = new Font("Segoe UI", 8.5f);
        return b;
    }

    static TableLayoutPanel Row(params Control[] cs)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = cs.Length, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
        foreach (var c in cs)
        {
            var fill = (c.Tag as string) == "fill";
            t.ColumnStyles.Add(fill ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            c.Anchor = fill ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
            c.Margin = new Padding(0, 3, 8, 3);
            t.Controls.Add(c);
        }
        return t;
    }

    // ---------------------------------------------------------------- inline notes instead of message boxes

    /// <summary>Shows <paramref name="text"/> in a note line under a card (warning colour), or hides the line when null.</summary>
    void ShowNote(Label note, string? text)
    {
        note.Text = text ?? "";
        note.Visible = !string.IsNullOrEmpty(text);
        note.ForeColor = Theme.Of(Tone.Warn);
        _tip.SetToolTip(note, note.Text);
    }

    /// <summary>
    /// Says what is missing next to where it is fixed, instead of a pop-up: switches to the right tab, shows the note,
    /// puts the cursor on <paramref name="target"/> and briefly lights it up.
    /// </summary>
    void Nudge(Control target, Label note, string text)
    {
        ShowPage(note == _accountNote ? _optionsPage : _uploadPage);
        ShowNote(note, text);
        if (target.CanFocus) target.Focus();
        Flash(target);
    }

    readonly HashSet<Control> _flashing = new();

    async void Flash(Control c)
    {
        if (!_flashing.Add(c)) return;
        var old = c.BackColor;
        try
        {
            for (var i = 0; i < 2; i++)
            {
                c.BackColor = Theme.Blend(old, Theme.Of(Tone.Warn), 0.45);
                await Task.Delay(220);
                if (c.IsDisposed) return;
                c.BackColor = old;
                await Task.Delay(160);
                if (c.IsDisposed) return;
            }
        }
        finally
        {
            _flashing.Remove(c);
            if (!c.IsDisposed) c.BackColor = old;
        }
    }

    // ---------------------------------------------------------------- state

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
        _tip.SetToolTip(_repo, _s.RepoPath.Length > 0 ? _s.RepoPath : "Pick the folder that holds your mods");
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
        if (changed) { _failed.Clear(); _retrySet.Clear(); _progress.Visible = false; _filter.Text = ""; }
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

    void RefreshAll()
    {
        SaveFields();
        _cfg = ProjectConfig.Load(_s.RepoPath);
        var branch = ModScanner.GitBranch(_s.RepoPath);
        _branch.Text = !Directory.Exists(_s.RepoPath) ? "" : branch == null ? "(not a git repo)" : "branch: " + branch;
        ShowGame();
        if (_cfg.Error != null) Append(_cfg.Error, LogKind.Warn);

        _mods = ModScanner.Scan(_s.RepoPath, _cfg);
        foreach (var m in _mods)
            if (WorkshopPage.Get(m.Dir) is { } page) m.Warnings.Add("Workshop page changes go out with the next upload: " + page.Summary());
        var uploaded = UploadedVersions.LoadRecord();
        var history = UploadHistory.Latest(UploadHistory.Load());
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
            it.Checked = CanTick(m) && m.OnSteam;
            UpdateRowState(it);
            _rows.Add(it);
            if (!m.Ready) Append($"{m.Name}: fix {string.Join("; ", m.Problems)}", LogKind.Warn);
        }
        ApplyFilter();
        _ = LoadUploadedVersionsAsync(_mods.Where(m => m.OnSteam).ToList(), uploaded, history);

        UpdateLoginHint();
        UpdateSetup();
        UpdateGoButton();
        UpdateSelectionButtons();
        StretchLastColumn();
    }

    bool CanTick(ModInfo m) => m.Ready && (m.OnSteam || _allowCreate.Checked);

    RowState StateOf(ListViewItem it)
    {
        var m = (ModInfo)it.Tag!;
        return ModRows.State(m, it.SubItems[UploadedCol].Text, _failed.Contains(m.Dir));
    }

    /// <summary>Sets the Status text (the badge) and the row colours from the mod's current state.</summary>
    void UpdateRowState(ListViewItem it)
    {
        it.SubItems[StatusCol].Text = ModRows.Label(StateOf(it));
        ColorRow(it, (ModInfo)it.Tag!);
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
        var selected = SelectedMod();
        _populating = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(visible.ToArray());
        foreach (var it in visible) it.Selected = ReferenceEquals(it.Tag, selected);
        _list.EndUpdate();
        _populating = false;
        UpdateEmptyState();
        UpdateGoButton();
        UpdateSelectionButtons();
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
        for (var i = 0; i < _list.Columns.Count; i++)
            _list.Columns[i].Text = ColumnNames[i] + (i == _sortCol ? (_sortDesc ? "  ▼" : "  ▲") : "");
        ApplyFilter();
    }

    /// <summary>The empty mod list says why it is empty and offers the button that fixes it.</summary>
    void UpdateEmptyState()
    {
        var empty = _list.Items.Count == 0;
        _empty.Visible = empty;
        _list.Visible = !empty;
        if (!empty) return;
        _emptyLayout.Visible = _emptyClear.Visible = false;
        _emptyBrowse.Visible = true;
        if (!Directory.Exists(_s.RepoPath))
        {
            _emptyTitle.Text = "Pick your mods folder";
            _emptyText.Text = "Choose the folder that holds your mods, one sub-folder per mod.\nYou can also drop the folder onto this window.";
        }
        else if (_rows.Count == 0)
        {
            _emptyTitle.Text = "No mods found in this folder";
            _emptyText.Text = string.IsNullOrWhiteSpace(_cfg.ModMarker)
                ? "Pick the folder that contains one sub-folder per mod, or edit the layout file to point at them."
                : $"Each mod folder needs {_cfg.ModMarker}. Pick the folder that contains the mod folders, or choose a different Layout above.";
            _emptyLayout.Visible = !_cfg.IsBuiltIn;
        }
        else
        {
            _emptyTitle.Text = "No mods match";
            var n = _rows.Count;
            _emptyText.Text = $"All {n} mod{(n == 1 ? " is" : "s are")} hidden by the filter{(_hideUpToDate.Checked ? " or by Hide up to date" : "")}.";
            _emptyBrowse.Visible = false;
            _emptyClear.Visible = true;
        }
        _emptyTitle.ForeColor = Theme.Current.Text;
    }

    /// <summary>The "Get set up" card: one line per step, a link while it still needs doing; hidden once all are done.</summary>
    void UpdateSetup()
    {
        var installed = SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd);
        var hasRepo = Directory.Exists(_s.RepoPath);
        Step(0, installed, "SteamCMD installed", "Install SteamCMD", async () => await InstallAsync());
        Step(1, _s.Account.Length > 0, "Steam account: " + _s.Account, "Set your Steam account", () => { ShowPage(_optionsPage); _account.Focus(); Flash(_account); });
        Step(2, hasRepo && _rows.Count > 0, $"{_rows.Count} mod{(_rows.Count == 1 ? "" : "s")} found",
            hasRepo ? "Point at the folder with your mods" : "Pick your mods folder", BrowseRepo);
        Step(3, _cfg.AppIdValid, "App ID " + _cfg.AppId, "Enter the game's app ID", () => { _appId.Focus(); Flash(_appId); });
        _setup.Visible = !(installed && _s.Account.Length > 0 && hasRepo && _rows.Count > 0 && _cfg.AppIdValid);
    }

    void Step(int i, bool done, string doneText, string todoText, Action act)
    {
        var l = _steps[i];
        var mark = done ? "✔  " : $"{i + 1}  ";
        l.Text = mark + (done ? doneText : todoText);
        l.Links.Clear();
        if (!done) l.Links.Add(mark.Length, todoText.Length);
        l.ForeColor = Theme.Of(done ? Tone.Ok : Tone.Muted);
        l.LinkColor = l.ActiveLinkColor = l.VisitedLinkColor = Theme.Current.Accent;
        l.Tag = done ? null : act;
        l.Cursor = done ? Cursors.Default : Cursors.Hand;
    }

    const int UploadedCol = 1, SteamCol = 2, VersionCol = 3, PreviewCol = 5, StatusCol = 6;

    /// <summary>Row text colour: red when it needs fixing, dimmed when it can't be picked.</summary>
    void ColorRow(ListViewItem it, ModInfo m)
    {
        var tone = !m.Ready ? Tone.Error : (!m.OnSteam && !_s.AllowCreate) ? Tone.Muted : Tone.Normal;
        for (var i = 0; i < it.SubItems.Count; i++)
        {
            var sub = it.SubItems[i];
            sub.BackColor = _list.BackColor;
            if (i != SteamCol || sub.Tag == null) sub.ForeColor = Theme.Of(tone);
        }
    }

    // ---------------------------------------------------------------- drawing the list

    bool PaintCell(DrawListViewSubItemEventArgs e)
    {
        if (e.Item?.Tag is not ModInfo m || e.SubItem == null) return false;
        var p = Theme.Current;
        var g = e.Graphics;
        var bounds = e.ColumnIndex == 0 ? new Rectangle(e.Bounds.X, e.Bounds.Y, _list.Columns[0].Width, e.Bounds.Height) : e.Bounds;
        var back = e.Item.Selected ? Theme.Blend(_list.BackColor, p.Accent, _list.Focused ? 0.40 : 0.22) : _list.BackColor;
        using (var b = new SolidBrush(back)) g.FillRectangle(b, bounds);

        if (e.ColumnIndex == 0)
        {
            // Our own tick box, drawn where Windows keeps the clickable one (left of the icon), so clicks still toggle it.
            var size = LogicalToDeviceUnits(14);
            var icon = e.Item.GetBounds(ItemBoundsPortion.Icon);
            var x = bounds.Left + Math.Max(LogicalToDeviceUnits(3), (icon.Left - bounds.Left - size) / 2);
            var box = new Rectangle(x, bounds.Top + (bounds.Height - size) / 2, size, size);
            DrawTick(g, box, e.Item.Checked, CanTick(m));
            bounds = Rectangle.FromLTRB(Math.Max(box.Right + LogicalToDeviceUnits(4), icon.Right), bounds.Top, bounds.Right, bounds.Bottom);
        }
        var text = Rectangle.FromLTRB(bounds.Left + LogicalToDeviceUnits(e.ColumnIndex == 0 ? 2 : 6), bounds.Top, bounds.Right - 2, bounds.Bottom);
        const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        if (e.ColumnIndex == StatusCol)
        {
            var state = StateOf(e.Item);
            var badge = DrawBadge(g, text, ModRows.Label(state), Theme.Of(ToneOf(state)), back);
            // Beside the badge, the first thing that needs doing or knowing; the tooltip has the rest.
            var (detail, tone) = !m.Ready ? (m.Problems[0], Tone.Error) : m.Warnings.Count > 0 ? (m.Warnings[0], Tone.Warn) : ("", Tone.Muted);
            if (detail.Length > 0)
                TextRenderer.DrawText(g, detail, _list.Font, Rectangle.FromLTRB(badge.Right + LogicalToDeviceUnits(8), text.Top, text.Right, text.Bottom), Theme.Of(tone), Flags);
            return true;
        }
        if (e.ColumnIndex == PreviewCol)
        {
            DrawPreview(g, bounds, m);
            return true;
        }
        TextRenderer.DrawText(g, e.SubItem.Text, _list.Font, text, e.SubItem.ForeColor, Flags);
        return true;
    }

    void DrawTick(Graphics g, Rectangle box, bool on, bool enabled)
    {
        var p = Theme.Current;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(box, LogicalToDeviceUnits(3));
        if (on)
        {
            using var fill = new SolidBrush(enabled ? p.Accent : p.Muted);
            g.FillPath(fill, path);
            using var pen = new Pen(p.AccentText, Math.Max(1.6f, LogicalToDeviceUnits(2) * 0.9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(box.Left + box.Width * 0.24f, box.Top + box.Height * 0.52f),
                new PointF(box.Left + box.Width * 0.43f, box.Top + box.Height * 0.70f),
                new PointF(box.Left + box.Width * 0.77f, box.Top + box.Height * 0.32f),
            });
        }
        else
        {
            using var pen = new Pen(enabled ? p.Muted : Theme.Blend(_list.BackColor, p.Muted, 0.45), 1.2f);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>A rounded pill with the text in the tone's colour on a faint tint of it. Returns where it was drawn.</summary>
    Rectangle DrawBadge(Graphics g, Rectangle area, string label, Color color, Color back)
    {
        var size = TextRenderer.MeasureText(g, label, _list.Font, Size.Empty, TextFormatFlags.NoPadding);
        var h = Math.Min(area.Height - LogicalToDeviceUnits(6), size.Height + LogicalToDeviceUnits(4));
        var r = new Rectangle(area.Left, area.Top + (area.Height - h) / 2, size.Width + LogicalToDeviceUnits(18), h);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Rounded(r, h / 2))
        using (var fill = new SolidBrush(Theme.Blend(back, color, 0.18)))
        using (var pen = new Pen(Theme.Blend(back, color, 0.55)))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = SmoothingMode.Default;
        TextRenderer.DrawText(g, label, _list.Font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return r;
    }

    void DrawPreview(Graphics g, Rectangle cell, ModInfo m)
    {
        const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        if (m.PreviewOk == null) { TextRenderer.DrawText(g, "-", _list.Font, cell, Theme.Of(Tone.Muted), Center); return; }
        var thumb = m.PreviewPath.Length > 0 ? Thumbnail(m.PreviewPath, cell.Height - LogicalToDeviceUnits(6), cell.Width - LogicalToDeviceUnits(10)) : null;
        if (thumb == null)
        {
            TextRenderer.DrawText(g, m.PreviewOk == true ? "✔" : "✖", _list.Font, cell, Theme.Of(m.PreviewOk == true ? Tone.Ok : Tone.Error), Center);
            return;
        }
        var r = new Rectangle(cell.Left + (cell.Width - thumb.Width) / 2, cell.Top + (cell.Height - thumb.Height) / 2, thumb.Width, thumb.Height);
        g.DrawImage(thumb, r);
        // Over Steam's 1 MB limit: a red frame (the tooltip says why).
        using var pen = new Pen(m.PreviewOk == true ? Theme.Current.Border : Theme.Of(Tone.Error), m.PreviewOk == true ? 1 : 2);
        g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    /// <summary>A small copy of the preview image, made once per file version and kept for the session.</summary>
    Image? Thumbnail(string path, int height, int maxWidth)
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { return null; }
        if (_thumbs.TryGetValue(path, out var c) && c.Stamp == stamp && (c.Image == null || c.Image.Height == height)) return c.Image;
        Image? img = null;
        try
        {
            using var fs = File.OpenRead(path);
            using var src = Image.FromStream(fs, false, false);
            var w = Math.Max(1, Math.Min(maxWidth, (int)Math.Round(src.Width * (double)height / src.Height)));
            var bmp = new Bitmap(w, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, height);
            }
            img = bmp;
        }
        catch { /* not an image Windows can read: the list shows a tick or cross instead */ }
        if (_thumbs.TryGetValue(path, out var old)) old.Image?.Dispose();
        _thumbs[path] = (stamp, img);
        return img;
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    string RowTip(ModInfo m)
    {
        var lines = new List<string>();
        lines.AddRange(m.Problems.Select(p => "Fix: " + p));
        lines.AddRange(m.Warnings.Select(w => "Note: " + w));
        if (m.PreviewNote.Length > 0) lines.Add("Preview: " + m.PreviewNote);
        lines.Add("Double-click the version to upload to change it, or the name to edit the Workshop page. Right-click for more.");
        if (ModScanner.NeedsChangelogEntry(m, _cfg)) lines.Add("Double-click the status to write the changelog entry for " + m.Version + ".");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Fills in "Updated on Steam" from Steam's public item details (one request for the whole list), then
    /// "Uploaded version" from each item's public changelog (the newest change note starts with the version when it
    /// was sent by this app or the Add Changenote mod), falling back to this PC's upload history.
    /// </summary>
    async Task LoadUploadedVersionsAsync(List<ModInfo> mods, Dictionary<string, string> uploaded, Dictionary<string, UploadEntry> history)
    {
        ListViewItem? Row(ModInfo m) => IsDisposed ? null : _rows.FirstOrDefault(i => ReferenceEquals(i.Tag, m));

        var live = await WorkshopStatus.FetchAsync(mods.Select(m => m.WorkshopId!));
        foreach (var m in mods)
        {
            var it = Row(m);
            if (it == null) return; // closed, or the list was refreshed meanwhile; that refresh fills it in
            var steam = it.SubItems[SteamCol];
            if (live.TryGetValue(m.WorkshopId!, out var item))
            {
                steam.Text = item.UpdatedText;
                steam.Tag = item.Updated;
                steam.ForeColor = Theme.Of(!m.Ready ? Tone.Error : item.Banned ? Tone.Error : item.Public ? Tone.Normal : Tone.Muted);
            }
            else steam.Text = "?";
        }
        _list.Invalidate();

        // One at a time: Steam rate-limits its community pages.
        foreach (var m in mods)
        {
            var fromNote = await UploadedVersions.FetchAsync(m.WorkshopId!);
            var it = Row(m);
            if (it == null) return;
            live.TryGetValue(m.WorkshopId!, out var item);
            var (text, note) = UploadedVersions.Resolve(fromNote, item, history.GetValueOrDefault(m.WorkshopId!), uploaded.GetValueOrDefault(m.WorkshopId!));
            it.SubItems[UploadedCol].Text = text;
            UpdateRowState(it);
            var extra = new List<string> { note };
            extra.Add(item?.Summary ?? "On Steam: could not be read (offline, or Steam did not answer).");
            it.ToolTipText = string.Join("\n", extra) + "\n" + RowTip(m);
            // Now known to be up to date: drop it from view when those are hidden.
            if (_hideUpToDate.Checked && StateOf(it) == RowState.UpToDate && it.ListView != null)
            {
                _populating = true;
                _list.Items.Remove(it);
                _populating = false;
                UpdateEmptyState();
            }
            UpdateSetup();
        }
    }

    void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Append("Could not open the page: " + e.Message, LogKind.Error); }
    }

    /// <summary>Opens a folder in Explorer or a file in its default program.</summary>
    void OpenPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) { Append("Not found: " + path, LogKind.Warn); return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Append("Could not open " + path + ": " + e.Message, LogKind.Error); }
    }

    void Copy(string text, string what)
    {
        try { Clipboard.SetText(text); Append($"{what} copied: {text}", LogKind.Note); }
        catch (Exception e) { Append("Could not copy: " + e.Message, LogKind.Error); }
    }

    /// <summary>Every upload of one mod made from this PC: when, which version and the change note sent.</summary>
    void ShowHistory(ModInfo m)
    {
        var entries = m.OnSteam ? UploadHistory.For(UploadHistory.Load(), m.WorkshopId!) : new List<UploadEntry>();
        using var f = new Form
        {
            Text = $"Upload history: {m.Name}", StartPosition = FormStartPosition.CenterParent, MinimizeBox = false,
            ClientSize = new Size(620, 420), MinimumSize = new Size(420, 300), Font = Font, ShowInTaskbar = false
        };
        var list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill };
        list.Columns.Add("When", 170);
        list.Columns.Add("Version", 110);
        list.Columns.Add("", 90);
        var note = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = true };
        foreach (var e in entries)
            list.Items.Add(new ListViewItem(new[] { e.Time.ToLocalTime().ToString("d MMM yyyy HH:mm"), e.Version, e.Created ? "created" : "update" }) { Tag = e });
        list.SelectedIndexChanged += (_, _) =>
            note.Text = list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is UploadEntry e ? e.ChangeNote.Replace("\n", Environment.NewLine) : "";
        var empty = new Label
        {
            Text = m.OnSteam ? "No uploads of this mod from this PC yet. Uploads are recorded from now on." : "This mod is not on Steam yet.",
            AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4)
        };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(note);
        f.Controls.Add(split);
        if (entries.Count == 0) f.Controls.Add(empty);
        else list.Items[0].Selected = true;
        f.Load += (_, _) => split.SplitterDistance = split.Height / 2;
        Theme.Apply(f);
        f.ShowDialog(this);
    }

    void OnListDoubleClick(object? sender, MouseEventArgs e)
    {
        if (_busy) return;
        var hit = _list.HitTest(e.Location);
        if (hit.Item?.Tag is not ModInfo m || hit.SubItem == null) return;
        var col = hit.Item.SubItems.IndexOf(hit.SubItem);
        if (col == 0) { EditWorkshopPage(m); return; }
        if (col == StatusCol && ModScanner.NeedsChangelogEntry(m, _cfg)) { if (AskChangelogEntry(m, m.Version)) RefreshAll(); return; }
        if (col != VersionCol) return;
        var entered = Prompt($"Version to upload for {m.Name}:", m.Version);
        if (entered == null || entered.Trim() == m.Version) return;
        var error = ModScanner.SetVersion(m, _cfg, entered);
        if (error != null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var version = entered.Trim();
        Append($"{m.Name}: version set to {version}.", LogKind.Note);
        // RimWorld refuses the upload until the new version has its own changelog entry, so offer to write it now.
        m.Version = version;
        if (ModScanner.NeedsChangelogEntry(m, _cfg) && !AskChangelogEntry(m, version))
            Append($"Add a {version} entry to {_cfg.Changelog} before uploading (double-click the Status to write it here).", LogKind.Warn);
        RefreshAll();
    }

    ModInfo? SelectedMod() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModInfo : null;

    void UpdateSelectionButtons() => _editPage.Enabled = !_busy && SelectedMod() != null;

    /// <summary>Opens the Workshop page editor; its changes are kept for the mod's next upload.</summary>
    void EditWorkshopPage(ModInfo? m)
    {
        if (_busy || m == null) return;
        var file = Publisher.DescriptionFor(m, _cfg);
        using var f = new WorkshopPageForm(m, _cfg.SteamDescriptionFile, file.Length > 0 ? file : null);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try { WorkshopPage.Set(m.Dir, f.Result); }
        catch (Exception e) { Append("Could not save the Workshop page changes: " + e.Message, LogKind.Error); return; }
        Append(f.Result.IsEmpty
            ? $"{m.Name}: Workshop page changes cleared."
            : $"{m.Name}: Workshop page changes ({f.Result.Summary()}) will be sent with the next upload.", LogKind.Note);
        RefreshAll();
    }

    /// <summary>Asks for the release notes of <paramref name="version"/> and adds them to the mod's changelog. True when written.</summary>
    bool AskChangelogEntry(ModInfo m, string version)
    {
        var notes = PromptLines($"What changed in {m.Name} {version}?",
            $"Added to {_cfg.Changelog} as the {version} entry; Steam shows it as the change note.", "- ");
        if (notes == null) return false;
        var error = ModScanner.AddChangelogEntry(m, _cfg, version, notes);
        if (error != null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
        Append($"{m.Name}: added the {version} entry to {_cfg.Changelog} (commit it).", LogKind.Note);
        return true;
    }

    /// <summary>A multi-line text box dialog. Null when cancelled.</summary>
    string? PromptLines(string label, string hint, string value)
    {
        using var f = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(480, 300), Font = Font };
        var l = new Label { Text = label, AutoSize = true, Location = new Point(12, 12) };
        var t = new TextBox { Text = value, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Location = new Point(12, 36), Size = new Size(456, 180) };
        var h = new Label { Text = hint, AutoSize = true, MaximumSize = new Size(456, 0), Location = new Point(12, 222) }.Role(ThemeRole.Muted);
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(312, 262), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(393, 262), Width = 75 };
        f.Controls.AddRange(new Control[] { l, t, h, ok, cancel });
        f.CancelButton = cancel; // no AcceptButton: Enter starts a new line
        ok.Role(ThemeRole.Accent);
        Theme.Apply(f);
        f.Shown += (_, _) => { t.SelectionStart = t.TextLength; t.SelectionLength = 0; };
        return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
    }

    /// <summary>A one-line text box dialog. Null when cancelled.</summary>
    string? Prompt(string label, string value)
    {
        using var f = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(360, 110), Font = Font };
        var l = new Label { Text = label, AutoSize = true, Location = new Point(12, 12) };
        var t = new TextBox { Text = value, Location = new Point(12, 36), Width = 336 };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(192, 72), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(273, 72), Width = 75 };
        f.Controls.AddRange(new Control[] { l, t, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        ok.Role(ThemeRole.Accent);
        Theme.Apply(f);
        return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
    }

    void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_populating || e.NewValue != CheckState.Checked) return;
        if (_list.Items[e.Index].Tag is ModInfo m && !CanTick(m)) e.NewValue = CheckState.Unchecked;
    }

    /// <summary>Ticks every shown mod that can be picked (the same rules as ticking by hand), or clears them all.</summary>
    void TickAll(bool on)
    {
        if (_busy) return;
        _list.BeginUpdate();
        foreach (ListViewItem it in _list.Items) it.Checked = on; // OnItemCheck turns down the ones that can't be picked
        if (!on) foreach (var it in _rows) it.Checked = false; // "none" also clears the rows the filter hides
        _list.EndUpdate();
        UpdateGoButton();
    }

    /// <summary>Ticked mods, including any the filter currently hides (the confirmation lists them all).</summary>
    IEnumerable<ModInfo> Selected() => _rows.Where(i => i.Checked).Select(i => (ModInfo)i.Tag!);

    bool _goQueued;

    void QueueGoButton()
    {
        if (_goQueued || !IsHandleCreated) return;
        _goQueued = true;
        BeginInvoke(() => { _goQueued = false; UpdateGoButton(); });
    }

    void UpdateGoButton()
    {
        var n = _rows.Count(i => i.Checked);
        var hidden = _rows.Count(i => i.Checked && i.ListView == null);
        _go.Text = n == 0 ? "Upload selected" : $"Upload {n} mod{(n == 1 ? "" : "s")}" + (hidden > 0 ? $" ({hidden} hidden)" : "");
        _go.Enabled = !_busy && n > 0 && _cfg.AppIdValid;
        _retry.Text = $"Retry failed ({_retrySet.Count})";
        _retry.Visible = !_busy && _retrySet.Count > 0 && _mods.Any(m => _retrySet.Contains(m.Dir));
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var c in new Control[] { _browse, _editLayout, _refresh, _all, _none, _checkLogin, _doLogin, _install, _repo, _account, _list, _dry, _allowCreate, _appId, _preset, _themeDark, _themeLight, _checkUpdateNow, _filter, _hideUpToDate })
            c.Enabled = !busy;
        ApplyLocks();
        _cancel.Visible = busy;
        _cancel.Enabled = busy;
        UpdateGoButton();
        UpdateSelectionButtons();
    }

    /// <summary>Shows the login state on the Options tab and, as a link to it, next to the Upload button.</summary>
    void SetLogin(string text, Tone tone)
    {
        _loginTone = tone;
        _login.Text = text;
        _login.ForeColor = Theme.Of(tone);
        // Missing SteamCMD or account are on the "Get set up" card; next to Upload only the login itself. Emptied
        // rather than hidden, so its column keeps pushing the Upload button to the right.
        _loginLink.Text = SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd) && !string.IsNullOrWhiteSpace(_s.Account) ? text : "";
        _loginLink.LinkColor = _loginLink.VisitedLinkColor = Theme.Of(tone);
        _tip.SetToolTip(_login, text);
    }

    void UpdateLoginHint()
    {
        var steam = _s.EffectiveSteamCmd;
        var installed = SteamLogin.SteamCmdInstalled(steam);
        _install.Visible = !installed;
        _steamCmdPath.Text = installed ? steam : "Not installed. Install SteamCMD puts it in " + Path.GetDirectoryName(steam);
        _tip.SetToolTip(_steamCmdPath, steam);
        if (!installed) SetLogin("SteamCMD is not installed - uploads won't work without it", Tone.Error);
        else if (string.IsNullOrWhiteSpace(_s.Account)) SetLogin("No Steam account set", Tone.Warn);
        else if (SteamLogin.LooksCached(steam, _s.Account)) SetLogin($"Saved login found for {_s.Account} (not checked yet)", Tone.Info);
        else SetLogin($"No saved login for {_s.Account} - you'll be asked to log in once", Tone.Warn);
    }

    // ---------------------------------------------------------------- log

    enum LogKind { Plain, Note, Ok, Warn, Error }

    static Color LogColor(LogKind k) => k switch
    {
        LogKind.Note => Color.DeepSkyBlue,
        LogKind.Ok => Color.LightGreen,
        LogKind.Warn => Color.Orange,
        LogKind.Error => Color.Tomato,
        _ => Theme.LogText,
    };

    static readonly Color LogTime = Color.FromArgb(118, 122, 134);

    /// <summary>Adds a line with the time in front, coloured by what kind of line it is.</summary>
    void Append(string line, LogKind kind = LogKind.Plain)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(line, kind)); return; }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = LogTime;
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  ");
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = LogColor(kind);
        _log.AppendText(line + "\n");
        _log.SelectionColor = _log.ForeColor;
        _log.ScrollToCaret();
    }

    static LogKind LineKind(string l)
    {
        if (l.StartsWith("SKIPPED") || l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || l.Contains("FAILED", StringComparison.OrdinalIgnoreCase)) return LogKind.Error;
        if (l.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)) return LogKind.Warn;
        if (l.StartsWith("Uploaded ") || l.StartsWith("Created ") || l.Contains("Success", StringComparison.OrdinalIgnoreCase)) return LogKind.Ok;
        if (l.StartsWith("===") || l.StartsWith("---")) return LogKind.Note;
        return LogKind.Plain;
    }

    void CopyLog()
    {
        if (_log.TextLength == 0) return;
        try { Clipboard.SetText(_log.Text.Replace("\n", Environment.NewLine)); }
        catch (Exception e) { Append("Could not copy the log: " + e.Message, LogKind.Error); }
    }

    void SaveLog()
    {
        using var d = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", FileName = $"SteamCmdHelper-log-{DateTime.Now:yyyy-MM-dd-HHmm}.txt" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(d.FileName, _log.Text.Replace("\n", Environment.NewLine)); Append("Log saved to " + d.FileName, LogKind.Note); }
        catch (Exception e) { Append("Could not save the log: " + e.Message, LogKind.Error); }
    }

    void OpenSteamCmdLogs()
    {
        var logs = Path.Combine(Path.GetDirectoryName(_s.EffectiveSteamCmd)!, "logs");
        if (!Directory.Exists(logs)) { Append("SteamCMD has no logs folder yet (" + logs + "); it appears after SteamCMD's first run.", LogKind.Warn); return; }
        OpenPath(logs);
    }

    // ---------------------------------------------------------------- login

    async Task<bool> VerifyLoginAsync(bool announce)
    {
        var steam = _s.EffectiveSteamCmd;
        if (!SteamLogin.SteamCmdInstalled(steam)) { UpdateLoginHint(); return false; }
        if (string.IsNullOrWhiteSpace(_s.Account)) { UpdateLoginHint(); if (announce) Nudge(_account, _accountNote, "Type your Steam account name first."); return false; }

        SetLogin("Checking login...", Tone.Muted);
        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        try
        {
            var (ok, _) = await SteamLogin.VerifyAsync(steam, _s.Account, CancellationToken.None);
            if (ok) SetLogin($"Logged in as {_s.Account}", Tone.Ok);
            else SetLogin($"Not logged in as {_s.Account}", Tone.Error);
            if (announce || !ok) Append(ok ? $"Steam login OK for {_s.Account}." : $"Steam login check failed for {_s.Account} (no saved login, wrong name, or no connection).", ok ? LogKind.Ok : LogKind.Warn);
            return ok;
        }
        catch (Exception e) { SetLogin("Login check failed", Tone.Error); Append("Login check error: " + e.Message, LogKind.Error); return false; }
        finally { if (!wasBusy) SetBusy(false); }
    }

    /// <summary>Checks the saved login; if there isn't one, opens SteamCMD so you can log in, waits, then checks again.</summary>
    async Task<bool> LoginFlowAsync()
    {
        if (string.IsNullOrWhiteSpace(_s.Account)) { Nudge(_account, _accountNote, "Type your Steam account name here first."); return false; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { Nudge(_install, _accountNote, "SteamCMD is not installed yet. Press Install SteamCMD below."); return false; }

        if (await VerifyLoginAsync(false)) return true;

        var r = MessageBox.Show(this,
            "Steam needs you to log in once.\n\nA SteamCMD window will open. Type your password, then the Steam Guard code, and wait until the window closes by itself.\n\nYour password goes to Steam only; this program never sees it.",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (r != DialogResult.OK) return false;

        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        SetLogin("Waiting for you to finish logging in...", Tone.Muted);
        try { await SteamLogin.InteractiveLoginAsync(_s.EffectiveSteamCmd, _s.Account, CancellationToken.None); }
        catch (Exception e) { Append("Could not open SteamCMD: " + e.Message, LogKind.Error); }
        finally { if (!wasBusy) SetBusy(false); }

        return await VerifyLoginAsync(true);
    }

    /// <summary>At start-up without SteamCMD: offer the one-click install (the "Get set up" card keeps the link if declined).</summary>
    async Task OfferInstallAsync()
    {
        var r = MessageBox.Show(this,
            "SteamCMD is not installed.\n\nThis program uploads through SteamCMD (Valve's command-line Steam client) and won't work without it.\n\n" +
            $"Install it now? It is downloaded from Valve to {Path.GetDirectoryName(_s.EffectiveSteamCmd)} and checked before it is used.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r == DialogResult.Yes) await InstallAsync();
    }

    async Task InstallAsync()
    {
        if (_busy) return;
        SaveFields();
        SetBusy(true);
        try { await SteamLogin.InstallAsync(_s.EffectiveSteamCmd, l => Append(l), CancellationToken.None); }
        catch (Exception e) { Append("Install failed: " + e.Message, LogKind.Error); }
        finally { SetBusy(false); UpdateLoginHint(); UpdateSetup(); }
    }

    // ---------------------------------------------------------------- updates

    /// <summary>
    /// Looks for a newer release on GitHub. At start-up (<paramref name="manual"/> false) it stays quiet when up to date,
    /// offline, or the release was skipped; "Check now" always says what it found. With automatic updates on, a start-up
    /// check downloads the release in the background and installs it when the window closes, without asking.
    /// </summary>
    async Task CheckForUpdateAsync(bool manual)
    {
        if (_pendingUpdate != null) { if (manual) RestartToUpdate(); return; }
        _checkUpdateNow.Enabled = false;
        ReleaseInfo? r;
        try { r = await UpdateCheck.FindNewerAsync(UpdateCheck.CurrentVersion); }
        finally { _checkUpdateNow.Enabled = !_busy; }
        if (IsDisposed) return;
        if (r == null)
        {
            if (manual) MessageBox.Show(this, $"No newer version found. You have SteamCMD Helper {UpdateCheck.CurrentVersion} (or GitHub could not be reached).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!manual && r.Version == _s.SkippedUpdate) return;
        Append($"SteamCMD Helper {r.Version} is available (you have {UpdateCheck.CurrentVersion}): {r.PageUrl}", LogKind.Note);

        var cannot = SelfUpdate.CannotUpdateReason(r, SelfUpdate.AppDir);
        if (!manual && _s.AutoUpdate && cannot == null)
        {
            try
            {
                _pendingUpdate = await SelfUpdate.PrepareAsync(r, SelfUpdate.KindOf(SelfUpdate.AppDir), null, CancellationToken.None);
                ShowUpdateReady();
            }
            catch (Exception e) { Append($"Could not download {r.Version} automatically: {e.Message}", LogKind.Warn); }
            return;
        }

        var update = new TaskDialogButton("Update now");
        var download = new TaskDialogButton("Download");
        var notes = new TaskDialogButton("Release notes");
        var later = new TaskDialogButton("Later");
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"SteamCMD Helper {r.Version} is available",
            Text = $"You have {UpdateCheck.CurrentVersion}. " + (cannot == null
                ? "Update now downloads it, checks it, and restarts the app."
                : r.DownloadUrl != null
                    ? cannot + " Download saves the new exe through your browser; close this one and run the new one."
                    : "Open the release page to download it."),
            Icon = TaskDialogIcon.Information,
            Buttons = { update, download, notes, later },
            Verification = new TaskDialogVerificationCheckBox($"Don't remind me about {r.Version} again"),
        };
        if (cannot != null) page.Buttons.Remove(update); else page.Buttons.Remove(download);
        if (r.DownloadUrl == null) page.Buttons.Remove(download);
        page.DefaultButton = cannot == null ? update : later;
        var picked = TaskDialog.ShowDialog(this, page);
        _s.SkippedUpdate = page.Verification.Checked ? r.Version : "";
        _s.Save();
        if (picked == update) { UpdateNow(r); return; }
        var url = picked == download ? r.DownloadUrl : picked == notes ? r.PageUrl : null;
        if (url == null) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Append("Could not open the browser: " + e.Message + " Download it from " + r.PageUrl, LogKind.Error); }
    }

    /// <summary>Downloads the release with a progress dialog, then offers to restart into it now or when the app closes.</summary>
    void UpdateNow(ReleaseInfo r)
    {
        using var cts = new CancellationTokenSource();
        var cancel = TaskDialogButton.Cancel;
        var progress = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"Downloading SteamCMD Helper {r.Version}...",
            Text = "The download is checked against the release's SHA-256 checksums before anything is changed.",
            ProgressBar = new TaskDialogProgressBar { Minimum = 0, Maximum = 100, Value = 0 },
            Buttons = { cancel },
            AllowCancel = true,
        };
        cancel.Click += (_, _) => cts.Cancel();
        Exception? error = null;
        var restart = new TaskDialogButton("Restart now");
        var onClose = new TaskDialogButton("When I close the app");
        var ready = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"SteamCMD Helper {r.Version} is ready",
            Text = _busy ? "An upload is running, so it will be installed when you close the app." : "Restart now to start using it, or it is installed when you close the app.",
            Icon = TaskDialogIcon.ShieldSuccessGreenBar,
            Buttons = { restart, onClose },
            DefaultButton = restart,
        };
        if (_busy) ready.Buttons.Remove(restart);
        progress.Created += async (_, _) =>
        {
            try
            {
                var p = new Progress<int>(v => { if (progress.BoundDialog != null) progress.ProgressBar!.Value = v; });
                var prepared = await SelfUpdate.PrepareAsync(r, SelfUpdate.KindOf(SelfUpdate.AppDir), p, cts.Token);
                if (cts.IsCancellationRequested || progress.BoundDialog == null) return; // closed while finishing
                _pendingUpdate = prepared;
                progress.Navigate(ready);
            }
            catch (Exception e)
            {
                error = e;
                progress.BoundDialog?.Close();
            }
        };
        var picked = TaskDialog.ShowDialog(this, progress);
        if (_pendingUpdate == null) cts.Cancel(); // closed with Cancel or the X while downloading
        if (error is OperationCanceledException || (error == null && _pendingUpdate == null)) { Append("Update cancelled.", LogKind.Note); return; }
        if (error != null)
        {
            Append($"Could not update to {r.Version}: {error.Message}", LogKind.Error);
            MessageBox.Show(this, $"Could not update to {r.Version}:\n\n{error.Message}\n\nNothing was changed. You can download it from {r.PageUrl}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ShowUpdateReady();
        if (picked == restart) RestartToUpdate();
    }

    void ShowUpdateReady()
    {
        if (_pendingUpdate == null) return;
        _updateReady.Text = $"SteamCMD Helper {_pendingUpdate.Version} is downloaded and is installed when you close the app. Restart now";
        _updateReady.LinkArea = new LinkArea(_updateReady.Text.Length - "Restart now".Length, "Restart now".Length);
        _updateReady.Visible = true;
        Append($"SteamCMD Helper {_pendingUpdate.Version} is downloaded; it is installed when you close the app (or Options > About > Restart now).", LogKind.Note);
    }

    /// <summary>Installs the downloaded update and starts the new version, unless an upload is running.</summary>
    void RestartToUpdate()
    {
        if (_pendingUpdate == null) return;
        if (_busy) { MessageBox.Show(this, "An upload is running. The update is installed when you close the app, or restart once the upload is done.", Text); return; }
        if (InstallPendingUpdate(restart: true)) Close();
    }

    /// <summary>Installs <see cref="_pendingUpdate"/> over this program's files. Returns false (and says why) when it could not.</summary>
    bool InstallPendingUpdate(bool restart)
    {
        var u = _pendingUpdate;
        if (u == null) return false;
        var exe = Environment.ProcessPath ?? Path.Combine(SelfUpdate.AppDir, SelfUpdate.ExeName);
        try
        {
            SelfUpdate.Apply(u, SelfUpdate.AppDir, Path.GetFileName(exe));
            _pendingUpdate = null;
            if (restart) SelfUpdate.Restart(exe, UpdateCheck.CurrentVersion);
            return true;
        }
        catch (Exception e)
        {
            _pendingUpdate = null;
            if (!IsDisposed && Visible)
            {
                _updateReady.Visible = false;
                Append($"Could not install {u.Version}: {e.Message} Nothing was changed.", LogKind.Error);
                MessageBox.Show(this, $"Could not install {u.Version}:\n\n{e.Message}\n\nNothing was changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
    }

    // ---------------------------------------------------------------- upload

    /// <summary>Ticks exactly the mods that failed or were not reached last time, and uploads them.</summary>
    async Task RetryFailedAsync()
    {
        if (_busy) return;
        foreach (var it in _rows)
        {
            var m = (ModInfo)it.Tag!;
            it.Checked = _retrySet.Contains(m.Dir) && CanTick(m);
        }
        UpdateGoButton();
        await GoAsync();
    }

    async Task GoAsync()
    {
        SaveFields();
        var picked = Selected().ToList();
        if (picked.Count == 0) return;
        if (!_cfg.AppIdValid) { Nudge(_appId, _projectNote, "Enter the game's Steam app ID first (the number in its store page URL)."); return; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { Nudge(_install, _accountNote, "SteamCMD is not installed yet. Press Install SteamCMD below."); return; }

        var dry = _dry.Checked;
        var actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in picked)
        {
            var pageEdit = WorkshopPage.Get(m.Dir);
            var create = "NEW " + WorkshopPage.VisibilityName(pageEdit?.Visibility ?? WorkshopPage.Private).ToLowerInvariant() + " item";
            actions[m.Dir] = (m.OnSteam ? "update " + m.WorkshopId : create)
                + (pageEdit != null && m.OnSteam ? "; also page: " + pageEdit.Summary() : "")
                + (m.Warnings.Count > 0 ? "  (" + m.Warnings[0] + ")" : "");
            notes[m.Dir] = Publisher.NotesFor(m, _cfg);
        }
        var heading = dry ? "Dry run for:" : $"Publish to the Steam Workshop ({(_cfg.GameName.Length > 0 ? _cfg.GameName : "app " + _cfg.AppId)}):";
        var gitPackaging = _cfg.UseGitArchive && ModScanner.IsGitRepo(_s.RepoPath);
        Dictionary<string, string> edited;
        bool commitFirst;
        using (var confirm = new ConfirmUploadForm(heading, picked, actions, notes, dry, gitPackaging))
        {
            if (confirm.ShowDialog(this) != DialogResult.OK) return;
            edited = confirm.EditedNotes;
            commitFirst = confirm.CommitFirst;
        }

        if (commitFirst && !CommitBeforeUpload(picked.Where(m => m.Dirty).ToList())) return;

        if (!dry && _cfg.Experimental)
        {
            var warn = MessageBox.Show(this,
                $"The \"{_cfg.PresetName()}\" layout is experimental: it has not been proven against the real game, and the game may expect its own in-game uploader's packaging.\n\n" +
                "Updating an EXISTING item replaces its content on Steam. Try a brand-new private item first (tick Create new items), and check it in the game before updating anything real.\n\nContinue?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (warn != DialogResult.Yes) return;
        }

        if (!dry && !await LoginFlowAsync()) { Append("Stopped: not logged in to Steam.", LogKind.Warn); return; }

        var pub = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account)
        {
            PageEdits = m => WorkshopPage.Get(m.Dir),
            NoteOverride = m => edited.TryGetValue(m.Dir, out var n) ? n : null,
            HistoryFile = UploadHistory.DefaultFile,
        };
        var history = UploadHistory.Load();
        pub.Log += l => BeginInvoke(() => Append(l, LineKind(l)));
        pub.Packaged += (m, report) => BeginInvoke(() =>
        {
            _eta = dry ? null : UploadHistory.EstimateSeconds(history, report.TotalBytes);
            _currentBase += "  ·  " + PackageCheck.Size(report.TotalBytes);
            ShowClock();
        });
        pub.Stage += (t, p) => BeginInvoke(() => SetStage(t, p));

        // One login check above covers the batch; each mod then reuses Steam's saved login.
        var batch = new BatchUpload(pub);
        batch.Starting += (m, i, n) =>
        {
            _idx = i;
            _currentBase = $"{m.Name} v{m.Version}  ({i + 1} of {n})";
            _modStarted = DateTime.Now;
            _eta = null;
            ShowClock();
            _modBar.Value = 0;
            SetStage("Starting...", 3);
            Append($"--- {m.Name} ---", LogKind.Note);
        };
        batch.Finished += (r, i, n) =>
        {
            if (r.Status == BatchStatus.NotRun) Append("Cancelled.", LogKind.Warn);
            else if (!r.Ok) Append($"SKIPPED {r.Mod.Name}: {r.Message}", LogKind.Error);
            SetStage(r.Ok ? r.Message : "Failed - see the log", r.Ok ? 100 : 0);
            _overall.Value = Math.Min(100, (int)((i + 1) * 100.0 / n));
            Taskbar.Progress(this, _overall.Value, r.Ok ? Taskbar.State.Normal : Taskbar.State.Error);
        };

        _cts = new CancellationTokenSource();
        _progress.Visible = true;
        SetBusy(true);
        _overall.Value = 0;
        _idx = 0;
        _count = picked.Count;
        Taskbar.Progress(this, 0);
        List<BatchItem> results;
        _clock.Start();
        try { results = await batch.RunAsync(picked, !dry, _cts.Token); }
        finally
        {
            _clock.Stop();
            _cts?.Dispose(); _cts = null;
            SetBusy(false);
        }
        // Page changes went out with each successful upload.
        if (!dry) foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created)) WorkshopPage.Clear(r.Mod.Dir);
        _failed.Clear(); _retrySet.Clear();
        foreach (var r in results.Where(r => !r.Ok))
        {
            _retrySet.Add(r.Mod.Dir);
            if (r.Status == BatchStatus.Failed) _failed.Add(r.Mod.Dir);
        }
        var counts = BatchUpload.Counts(results);
        var allOk = results.All(r => r.Ok);
        _currentBase = "";
        _current.Text = "Finished: " + counts;
        Append(BatchUpload.Summary(results), allOk ? LogKind.Ok : LogKind.Warn);
        Taskbar.Progress(this, 100, allOk ? Taskbar.State.None : Taskbar.State.Error);
        Taskbar.FlashIfInBackground(this);
        if (!dry) AfterUploadGit(results);
        RefreshAll();
        if (!allOk)
        {
            var retry = new TaskDialogButton($"Retry failed ({_retrySet.Count})");
            var relogin = new TaskDialogButton("Log in again and retry");
            var close = TaskDialogButton.Close;
            var loginTrouble = results.Any(r => r.Status == BatchStatus.Failed && BatchUpload.IsLoginFailure(r.Message));
            var page = new TaskDialogPage
            {
                Caption = Text, Heading = loginTrouble ? "Steam refused the login" : "Some mods were not uploaded", Text = BatchUpload.Summary(results),
                Icon = TaskDialogIcon.Warning, Buttons = { retry, close }, DefaultButton = close,
            };
            if (loginTrouble) { page.Buttons.Insert(0, relogin); page.DefaultButton = relogin; }
            var picked2 = TaskDialog.ShowDialog(this, page);
            if (picked2 == relogin)
            {
                if (await ForceLoginAsync()) await RetryFailedAsync();
            }
            else if (picked2 == retry) await RetryFailedAsync();
        }
        else if (picked.Count > 1)
            MessageBox.Show(this, BatchUpload.Summary(results), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>The current mod, its package size, how long it has run and, from past uploads, about how long is left.</summary>
    void ShowClock()
    {
        if (_currentBase.Length == 0) return;
        var ran = DateTime.Now - _modStarted;
        var text = _currentBase + "  ·  " + Clock(ran.TotalSeconds);
        if (_eta is double eta && _busy)
        {
            var left = eta - ran.TotalSeconds;
            text += left > 5 ? $"  ·  about {Clock(left)} left" : "  ·  finishing";
        }
        _current.Text = text;
    }

    static string Clock(double seconds) => seconds < 60 ? $"{seconds:0}s" : $"{(int)(seconds / 60)}m {seconds % 60:00}s";

    /// <summary>Opens SteamCMD to log in even when a saved login exists (it may be the one Steam just turned down).</summary>
    async Task<bool> ForceLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_s.Account)) { ShowPage(_optionsPage); Nudge(_account, _accountNote, "Type your Steam account name first."); return false; }
        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        SetLogin("Waiting for you to finish logging in...", Tone.Muted);
        try { await SteamLogin.InteractiveLoginAsync(_s.EffectiveSteamCmd, _s.Account, CancellationToken.None); }
        catch (Exception e) { Append("Could not open SteamCMD: " + e.Message, LogKind.Error); }
        finally { if (!wasBusy) SetBusy(false); }
        return await VerifyLoginAsync(true);
    }

    /// <summary>Commits each mod folder's changes (git packaging uploads only HEAD). False, after saying why, when git refused.</summary>
    bool CommitBeforeUpload(List<ModInfo> mods)
    {
        foreach (var m in mods)
        {
            var error = GitOps.CommitPaths(_s.RepoPath, new[] { m.RelPath }, $"{m.Name} {m.Version}");
            if (error != null)
            {
                Append($"Could not commit {m.Name}: {error}", LogKind.Error);
                MessageBox.Show(this, $"Could not commit the changes in {m.Name}, so nothing was uploaded:\n\n{error}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            m.Dirty = false;
            Append($"Committed the changes in {m.Name} ({m.RelPath}).", LogKind.Ok);
        }
        return true;
    }

    /// <summary>
    /// After an upload in a git repo: offers to commit the id files of newly created items (so they are never created
    /// twice), and tags what was uploaded when that option is on.
    /// </summary>
    void AfterUploadGit(List<BatchItem> results)
    {
        if (!ModScanner.IsGitRepo(_s.RepoPath)) return;
        var created = results.Where(r => r.Status == BatchStatus.Created).ToList();
        if (created.Count > 0)
        {
            var files = created.Select(r => (r.Mod.RelPath + "/" + _cfg.WorkshopIdFile).Replace('\\', '/')).ToList();
            var commit = _s.CommitNewIds;
            if (!commit)
            {
                var yes = new TaskDialogButton("Commit");
                var page = new TaskDialogPage
                {
                    Caption = Text,
                    Heading = "Commit the new Workshop ID" + (files.Count == 1 ? "?" : "s?"),
                    Text = "Steam gave " + string.Join(", ", created.Select(r => $"{r.Mod.Name} the id {r.WorkshopId}")) +
                        ". Committing " + string.Join(", ", files) + " keeps anyone from creating the item a second time.",
                    Icon = TaskDialogIcon.Information,
                    Buttons = { yes, new TaskDialogButton("Not now") },
                    DefaultButton = yes,
                    Verification = new TaskDialogVerificationCheckBox("Always commit new Workshop IDs without asking"),
                };
                commit = TaskDialog.ShowDialog(this, page) == yes;
                if (commit && page.Verification.Checked) { _s.CommitNewIds = true; _commitIds.Checked = true; _s.Save(); }
            }
            if (commit)
            {
                var names = string.Join(", ", created.Select(r => r.Mod.Name));
                var error = GitOps.CommitPaths(_s.RepoPath, files, $"Add Workshop ID for {names}");
                Append(error == null ? $"Committed the Workshop ID file for {names}." : "Could not commit the Workshop ID: " + error, error == null ? LogKind.Ok : LogKind.Error);
            }
        }

        if (!_s.TagUploads) return;
        if (!_cfg.UseGitArchive) { Append("Not tagging: this layout copies the working folder, so no commit matches what was uploaded.", LogKind.Note); return; }
        foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created))
        {
            var tag = GitOps.TagName(r.Mod.Folder, r.Mod.Version);
            var result = GitOps.Tag(_s.RepoPath, tag, $"{r.Mod.Name} {r.Mod.Version} uploaded to Steam Workshop item {r.WorkshopId}");
            if (result == null) Append($"Tagged {tag}.", LogKind.Ok);
            else if (result == "exists") Append($"Tag {tag} already exists; left as it is.", LogKind.Note);
            else Append($"Could not tag {tag}: {result}", LogKind.Warn);
        }
    }

    /// <summary>Packages one mod without uploading and shows what the package holds.</summary>
    async Task PreviewPackageAsync(ModInfo m)
    {
        if (_busy) return;
        PackageReport? report = null;
        var pub = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account);
        pub.Packaged += (_, r) => report = r;
        SetBusy(true);
        PublishResult res;
        try { res = await Task.Run(() => pub.RunAsync(m, false, CancellationToken.None)); }
        catch (Exception e) { res = new PublishResult(false, e.Message); }
        finally { SetBusy(false); }
        if (report == null) { MessageBox.Show(this, $"Could not build the package for {m.Name}:\n\n{res.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        using var f = new PackagePreviewForm(m, report);
        f.ShowDialog(this);
    }

    void SetStage(string text, int pct)
    {
        _stage.Text = text;
        _modBar.Value = Math.Clamp(Math.Max(_modBar.Value, pct), 0, 100);
        if (_count > 0) _overall.Value = Math.Clamp((int)((_idx * 100.0 + _modBar.Value) / _count), 0, 100);
        if (_busy) Taskbar.Progress(this, _overall.Value);
    }

    void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }
}
