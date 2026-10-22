using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

public sealed partial class MainForm : Form
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
    readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly ComboBox _sidesChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, AccessibleName = "Show" };
    /// <summary>The Options sections only the uploader uses, hidden when only the mod manager is picked.</summary>
    readonly HashSet<SideNavButton> _uploaderSections = new();
    readonly ComboBox _textSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = "Language" };
    List<Translation> _languages = new();
    /// <summary>Says that Windows High Contrast is on and wins over the theme choice; hidden otherwise.</summary>
    Label _contrastNote = null!;
    readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly TextBox _tsTeam = new() { Width = 200, AccessibleName = "Thunderstore team" };
    readonly TextBox _tsCommunity = new() { Width = 200, PlaceholderText = "e.g. sun-haven", AccessibleName = "Thunderstore community" };
    readonly TextBox _tsCategories = new() { Width = 260, PlaceholderText = "e.g. mods, tweaks", AccessibleName = "Thunderstore categories" };
    readonly TextBox _tsToken = new() { Width = 320, UseSystemPasswordChar = true, AccessibleName = "Thunderstore service account token" };
    readonly CheckBox _tsAfter = new() { AutoSize = true, Text = "After each Steam upload, also publish the mods that have a Thunderstore manifest.json" };
    readonly TextBox _webhook = new() { Width = 420, PlaceholderText = "https://discord.com/api/webhooks/..." };
    readonly CheckBox _ghReleases = new() { AutoSize = true, Text = "Make a GitHub &release for each upload (needs the gh tool, logged in with \"gh auth login\")" };
    Button _profileSave = null!, _profileDelete = null!, _webhookTest = null!;
    readonly CheckBox _tagUploads = new() { AutoSize = true, Text = "&Tag each upload in git as <mod folder>-v<version>" };
    readonly CheckBox _uploadCredit = new() { AutoSize = true, Text = "End each change note with \"Uploaded via Modwright\" and a link to its download page" };
    readonly CheckBox _markdownNotes = new() { AutoSize = true, Text = "Turn &Markdown in .md changelogs into Steam formatting (headings, bold, lists, links)" };
    readonly TextBox _noteTemplate = new()
    {
        Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 64, Font = new Font("Consolas", 9f),
        PlaceholderText = "{note}", AccessibleName = "Change note template for this mods folder",
    };
    readonly TextBox _descFooter = new()
    {
        Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 64, Font = new Font("Consolas", 9f),
        PlaceholderText = "e.g. [url=https://discord.gg/...]Discord[/url] | [url=https://ko-fi.com/...]Ko-fi[/url]", AccessibleName = "Footer added to every description in this mods folder",
    };
    readonly TextBox _checklist = new()
    {
        Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 80,
        PlaceholderText = "e.g.\r\nTested in a clean colony\r\nUpdated the preview image\r\nAnswered the open bug reports", AccessibleName = "Release checklist for this mods folder",
    };
    readonly CheckBox _checklistMust = new() { AutoSize = true, Text = "Don't upload until every step is &ticked" };
    readonly CheckBox _countComments = new() { AutoSize = true, Text = "At start-up, count new comments on your mods (at most every 6 hours, only while Steam is running)" };
    readonly ComboBox _descSync = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = "When a description file differs from Steam" };
    readonly CheckBox _testItems = new() { AutoSize = true, Text = "Upload to t&est items" };
    readonly TextBox _afterCmd = new() { Width = 420, PlaceholderText = "e.g. copy \"%SCH_MOD_DIR%\" to a backup drive", AccessibleName = "Command after each upload for this mods folder" };
    readonly CheckBox _pushTags = new() { AutoSize = true, Text = "&Push the tags to the remote straight away (git push origin <tag>)" };
    readonly CheckBox _keepPackages = new() { AutoSize = true, Text = "&Keep a zip of what each upload sent (the newest " + PackageArchive.Keep + " per mod, in the data folder)" };
    readonly TextBox _buildCmd = new() { Width = 420, PlaceholderText = "e.g. dotnet build -c Release", AccessibleName = "Build step for this mods folder" };
    readonly TextBox _packageRef = new() { Width = 200, PlaceholderText = "the checked-out commit", AccessibleName = "Branch or tag to upload from" };
    readonly CheckBox _updateTags = new() { AutoSize = true, Text = "Set Workshop tags after each upload (RimWorld: Mod and each supported version from About.xml)" };
    readonly CheckBox _updateRequired = new() { AutoSize = true, Text = "Set the Workshop page's \"Required items\" from the Workshop links in each mod's metadata (RimWorld: modDependencies)" };
    readonly CheckBox _updatePageExtras = new() { AutoSize = true, Text = "Send each mod's gallery (images and videos.txt in its gallery folder) and translated descriptions" };
    readonly CheckBox _autoRetry = new() { AutoSize = true, Text = "Try an update once more when Steam is busy or can't be reached (after 30 seconds)" };
    readonly CheckBox _commitIds = new() { AutoSize = true, Text = "Commit a new item's Workshop ID file without asking" };
    readonly CheckBox _oneSession = new() { AutoSize = true, Text = "Upload several mods with one SteamCMD login (faster batches)" };
    readonly CheckBox _notifyDone = new() { AutoSize = true, Text = "Show a Windows notification when an upload ends while the window is in the background" };
    readonly CheckBox _strictAbout = new() { AutoSize = true, Text = "RimWorld: About.xml warnings block the upload too" };
    readonly CheckBox _verifyUploads = new() { AutoSize = true, Text = "Check each upload by downloading it back from Steam and comparing the files" };
    readonly CheckBox _steamBack = new() { AutoSize = true, Text = "Bring the Steam client back &online after uploading (restarts it when SteamCMD's login took its place)" };
    Button _restartSteam = null!;
    readonly CheckBox _watchBuilds = new() { AutoSize = true, Text = "At start-up, say when the game gets a new build or beta (checked at most every 12 hours)" };
    /// <summary>The game's Steam branches, once SteamCMD has listed them this session.</summary>
    List<GameBranch>? _branches;
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    string _currentBase = "";
    DateTime _modStarted;
    double? _eta;
    readonly CheckBox _checkUpdates = new() { AutoSize = true, Text = "Check for a newer version at start-up" };
    readonly CheckBox _autoUpdate = new() { AutoSize = true, Text = "Install new versions automatically (downloaded in the background, installed when you close the app)" };
    readonly LinkLabel _updateReady = new LinkLabel { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Visible = false, Margin = new Padding(0, 4, 0, 0) }.Role(ThemeRole.Keep);
    readonly ListView _list = new() { AccessibleName = "Mods" };
    readonly TextBox _filter = new() { Width = 150, PlaceholderText = "Filter mods  (Ctrl+F)", AccessibleName = "Filter mods" };
    readonly CheckBox _hideUpToDate = new() { AutoSize = true, Text = "&Hide up to date" };
    /// <summary>Filters the list once typing pauses (see <see cref="Debouncer"/>).</summary>
    Debouncer _filterPause = null!;
    readonly CheckBox _showDetails = new() { AutoSize = true, Text = "Show the selected mod's details beside the mod list" };
    readonly FlatProgressBar _modBar = new() { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 6), AccessibleName = "Current mod progress" };
    readonly FlatProgressBar _overall = new() { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 2), AccessibleName = "Overall progress" };
    readonly Label _current = new() { AutoSize = true, Text = "Idle", Tag = "fill" };
    readonly Label _stage = new Label { AutoSize = true }.Role(ThemeRole.Muted);
    readonly CheckBox _dry = new() { AutoSize = true, Text = "&Dry run" };
    readonly CheckBox _allowCreate = new() { AutoSize = true, Text = "&Create new items (private)" };
    readonly RichTextBox _log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None, AccessibleName = "Log" }.Role(ThemeRole.Log);
    /// <summary>Every log line, so the filter can show a part of them and Copy and Save always have all of them.</summary>
    readonly List<(DateTime Time, string Text, LogKind Kind)> _logLines = new();
    const int MaxLogLines = 20000;
    readonly ComboBox _logFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, AccessibleName = "Show in the log" };
    readonly CheckBox _autoScroll = new() { AutoSize = true, Text = "Auto-scroll", Checked = true };
    SplitContainer _split = null!;

    /// <summary>Under the mod list: everything the selected mod needs, in full (the Status column has room for one line).</summary>
    readonly ModDetailsPanel _details = new();
    /// <summary>The list on the left, the selected mod's details on the right (when the window has room for both).</summary>
    SplitContainer _listSplit = null!;
    /// <summary>Steam's public details and this PC's last upload of each item, by Workshop ID, for the details panel.</summary>
    Dictionary<string, WorkshopItem> _live = new();
    Dictionary<string, UploadEntry> _history = new();
    readonly Label _detail = new Label { Dock = DockStyle.Bottom, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 6, 0), AccessibleName = "Selected mod", UseMnemonic = false }.Role(ThemeRole.Keep);

    // The Project card folds into one summary line once it is set up, which gives the mod list its height back.
    readonly Label _projectSummary = new Label { AutoSize = false, AutoEllipsis = true, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill", UseMnemonic = false, Cursor = Cursors.Hand }.Role(ThemeRole.Muted);
    Button _projectToggle = null!;
    Control[] _projectRows = Array.Empty<Control>();
    bool _projectOpen;

    // "Get set up" progress: how many steps are done, and the next one as a button.
    readonly Label _setupCount = new Label { AutoSize = true }.Role(ThemeRole.Muted);
    Button _setupNext = null!;
    readonly FlatProgressBar _setupBar = new() { Dock = DockStyle.Top, Height = 4, Margin = new Padding(0, 6, 0, 0), AccessibleName = "Setup progress" };
    readonly Action?[] _stepActions = new Action?[4];

    /// <summary>The long notes on the Options tab; they wrap to the width of their card.</summary>
    readonly List<Label> _hints = new();

    // The empty mod list: what to do next, with the button that does it.
    readonly Panel _empty = new() { Dock = DockStyle.Fill, Visible = false };
    readonly Label _emptyTitle = new() { AutoSize = true, Font = Theme.HeadingFont, Anchor = AnchorStyles.None };
    readonly Label _emptyText = new Label { AutoSize = true, MaximumSize = new Size(520, 0), TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.None }.Role(ThemeRole.Muted);

    // "Get set up": the steps a new user still has to do, each a link that does it.
    readonly LinkLabel[] _steps = Enumerable.Range(0, 4).Select(_ => new LinkLabel { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 2, 24, 2) }.Role(ThemeRole.Keep)).ToArray();

    Button _changed = null!;
    Button _groups = null!;
    Button _help = null!, _checkUpdateNow = null!, _editPage = null!, _browse = null!, _refresh = null!, _all = null!, _none = null!, _go = null!, _later = null!, _cancel = null!, _retry = null!,
        _checkLogin = null!, _doLogin = null!, _install = null!, _editLayout = null!, _emptyBrowse = null!, _emptyLayout = null!, _emptyClear = null!, _emptyNew = null!;
    NavButton _navUpload = null!, _navDropPod = null!, _navOptions = null!;
    Control _uploadPage = null!, _dropPodPage = null!, _optionsPage = null!, _setup = null!, _progress = null!;
    Tone _loginTone = Tone.Muted;

    List<ModInfo> _mods = new();
    /// <summary>Every row, shown or filtered out; the list view holds only the shown ones.</summary>
    readonly List<ListViewItem> _rows = new();
    /// <summary>Mod folders whose upload failed in the last run (badge "Failed"), and those to try again (failed or not run).</summary>
    /// <summary>The folder the list shows, and the mod to select again while a refresh rebuilds it.</summary>
    string? _shownRepo, _keepSelected;
    readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase), _retrySet = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Each mod's problems as last written to the log, so a refresh only logs what changed (by mod folder).</summary>
    readonly Dictionary<string, string> _loggedProblems = new(StringComparer.OrdinalIgnoreCase);
    int _sortCol = -1;
    bool _sortDesc;
    readonly Dictionary<string, (DateTime Stamp, Image? Image)> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Thumbnails whose file was checked since the folder was last read, so painting a row doesn't ask the disk.</summary>
    readonly HashSet<string> _thumbsChecked = new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? _cts;
    bool _busy, _populating;
    int _idx, _count;

    /// <summary>Skips the start-up "install SteamCMD?" question (for --shot).</summary>
    public bool NoStartupPrompts { get; init; }

    /// <summary>The version this program replaced, when it was just started by an update.</summary>
    public string? UpdatedFrom { get; init; }

    /// <summary>A downloaded update that is installed when the window closes (or on "Restart now").</summary>
    PendingUpdate? _pendingUpdate;

    public const string Disclaimer = "Not affiliated with or endorsed by Valve Corporation or Steam.";

    static readonly string[] ColumnNames = { "Mod", "Steam version", "Updated", "To upload", "Workshop ID", "Preview", "Status" };

    /// <param name="theme">Overrides the saved theme without saving it (used by --shot).</param>
    /// <param name="settings">Used instead of the saved settings (used by --shot).</param>
    public MainForm(string? theme = null, AppSettings? settings = null)
    {
        _s = settings ?? AppSettings.Load();
        if (_s.RecentRepos.Count == 0 && _s.RepoPath.Length > 0) _s.Remember(_s.RepoPath); // settings from before the recent list
        Theme.Set(theme ?? _s.Theme);
        Theme.TextScale = Display.NormalizeTextSize(_s.TextSize) / 100f;
        Localizer.Use(_s.Language);
        BuildUi();
        FillRecent();
        _account.Text = _s.Account;
        _populating = true; // the Shown handler reads the folder once; these must not each trigger a read too
        _allowCreate.Checked = _s.AllowCreate;
        _hideUpToDate.Checked = _s.HideUpToDate;
        _populating = false;
        RestoreWindow();
        Shown += async (_, _) =>
        {
            if (!NoStartupPrompts && HasUploader && HasDropPod && _s.OpenOnDropPod) ShowPage(_dropPodPage); // back where it was left
            if (_dropPodPage.Visible) DropPodShown();
            if (!HasUploader) { } // the mods folder is read when the uploader is switched on
            else if (NoStartupPrompts) RefreshNow(); // --shot expects the list filled as soon as the window shows
            else await RefreshAllAsync();
            if (IsDisposed) return;
            if (UpdatedFrom != null) Append($"Updated Modwright from {UpdatedFrom} to {UpdateCheck.CurrentVersion}.", LogKind.Ok);
            if (!NoStartupPrompts) ShowWhatsNewAfterUpdate();
            if (!NoStartupPrompts && HasUploader) _ = StartupCommentsAsync();
            if (!NoStartupPrompts) _ = Task.Run(() => { SelfUpdate.CleanUp(SelfUpdate.AppDir); SelfUpdate.CleanStaging(UpdateCheck.CurrentVersion); }); // what the last update left
            if (!HasUploader) { } // SteamCMD is only needed by the mod manager to download missing mods, and it asks then
            else if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { if (!NoStartupPrompts) await OfferInstallAsync(); }
            else _ = StartupSteamAsync(SteamLogin.LooksCached(_s.EffectiveSteamCmd, _s.Account));
            if (_s.CheckForUpdates && !NoStartupPrompts) await CheckForUpdateAsync(false);
        };
        Activated += (_, _) => { if (!_busy) Taskbar.Progress(this, 0, Taskbar.State.None); };
        Activated += async (_, _) => await RefreshIfChangedAsync();
        FormClosing += (_, e) =>
        {
            if (!_busy) return;
            if (MessageBox.Show(this, "An upload is running. Stop it and close?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            Cancel();
        };
        FormClosed += (_, _) => SaveWindow();
        FormClosed += (_, _) => InstallPendingUpdate(restart: false);
        FormClosed += (_, _) => Notify.Hide();
        Activated += (_, _) => Notify.Hide(); // back at the window: the notification has done its job
        // "Match Windows" and High Contrast follow Windows while the app is open.
        SystemEvents.UserPreferenceChanged += OnWindowsPreference;
        Disposed += (_, _) => SystemEvents.UserPreferenceChanged -= OnWindowsPreference;
    }

    void OnWindowsPreference(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility or UserPreferenceCategory.VisualStyle)) return;
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            if (Theme.Set(Theme.Choice)) ReapplyTheme();
            ShowThemeChoice();
        });
    }

    // ---------------------------------------------------------------- UI

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
                Hint("Export saves your settings, profiles, per-folder options (including Discord webhook links, so keep the file private) and upload history in one " +
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

    // ---------------------------------------------------------------- keyboard and drag-and-drop

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5 when _dropPodPage.Visible:
                _ = ScanDropPodAsync(); return true;
            case Keys.F5 when !_busy && HasUploader:
                RefreshAll(); return true;
            case Keys.Control | Keys.O when !_busy && HasUploader:
                BrowseRepo(); return true;
            case Keys.Control | Keys.N when !_busy && HasUploader:
                _ = NewModAsync(); return true;
            case Keys.Control | Keys.F when _dropPodPage.Visible:
                _dpFilter.Focus(); _dpFilter.SelectAll(); return true;
            case Keys.Control | Keys.F when HasUploader:
                ShowPage(_uploadPage); _filter.Focus(); _filter.SelectAll(); return true;
            case Keys.Control | Keys.Enter when _go.Enabled && HasUploader:
                _go.PerformClick(); return true;
            case Keys.F1:
                ShowShortcuts(); return true;
            case Keys.Control | Keys.K:
            case Keys.Control | Keys.Shift | Keys.P:
                ShowPalette(); return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.Tab:
            {
                // Upload, Drop Pod, Options (those shown), round and round (Shift goes backwards).
                var order = new (Control Page, NavButton Nav)[] { (_uploadPage, _navUpload), (_dropPodPage, _navDropPod), (_optionsPage, _navOptions) }
                    .Where(p => p.Nav.Visible).ToArray();
                var at = Array.FindIndex(order, p => p.Page.Visible);
                var next = order[(at + ((keyData & Keys.Shift) != 0 ? order.Length - 1 : 1)) % order.Length];
                ShowPage(next.Page);
                next.Nav.Focus();
                return true;
            }
            case Keys.Control | Keys.PageDown when _optionsPage.Visible:
            case Keys.Control | Keys.PageUp when _optionsPage.Visible:
                ShowSection(NextSection(_section, (keyData & Keys.KeyCode) == Keys.PageUp));
                _sections[_section].Tab.Focus();
                return true;
            case Keys.Escape when _busy && _cts != null && !_filter.Focused && !_dpFilter.Focused:
                Append("Stopping after the current step...", LogKind.Warn);
                Say("Stopping the upload.");
                Cancel(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Keyboard versions of the list's double-clicks: Enter edits the Workshop page, F2 changes the version.</summary>
    void OnListKey(object? sender, KeyEventArgs e)
    {
        if (_busy || SelectedMod() is not { } m) return;
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None) { e.SuppressKeyPress = e.Handled = true; EditWorkshopPage(m); }
        else if (e.KeyCode == Keys.F2 && e.Modifiers == Keys.None) { e.SuppressKeyPress = e.Handled = true; ChangeVersion(m); }
    }

    /// <summary>Options > About > Check setup: runs <see cref="Doctor"/> with the real login check and shows the results.</summary>
    async Task CheckSetupAsync(Control button)
    {
        button.Enabled = false;
        var old = button.Text;
        button.Text = "Checking...";
        try
        {
            // SteamCMD runs one at a time, so while an upload runs only the saved login file is looked at.
            var probes = _busy ? new Doctor.Probes() : new Doctor.Probes { Login = async (cmd, acc, ct) => (await SteamLogin.VerifyAsync(cmd, acc, ct)).ok };
            var checks = await Task.Run(() => Doctor.RunAsync(_s, _s.RepoPath, _s.EffectiveSteamCmd, _s.Account, probes));
            using var f = new SetupCheckForm(checks);
            f.ShowDialog(this);
        }
        finally { button.Text = old; button.Enabled = true; }
    }

    void ShowShortcuts()
    {
        using var f = new ShortcutsForm();
        f.ShowDialog(this);
    }

    /// <summary>
    /// Tells a screen reader something that happened without moving the focus (an upload finished, a step is missing).
    /// Silent when no screen reader listens.
    /// </summary>
    void Say(string text)
    {
        try
        {
            if (IsHandleCreated) AccessibilityObject.RaiseAutomationNotification(
                System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                System.Windows.Forms.Automation.AutomationNotificationProcessing.ImportantMostRecent, text);
        }
        catch { /* older Windows without UI Automation notifications */ }
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
            _game.Text = preset == ProjectConfig.ParadoxName
                ? "Enter the game's Steam app ID: " + string.Join(", ", ProjectConfig.ParadoxGames.Select(g => $"{g.Game} {g.AppId}")) + "."
                : "Enter the game's Steam app ID (the number in its store URL) before uploading.";
            _game.ForeColor = Theme.Of(Tone.Warn);
        }
        else
        {
            _game.Text = $"{(_cfg.GameName.Length > 0 ? _cfg.GameName : "App " + _cfg.AppId)}  -  layout: {_cfg.Source}"
                         + (_cfg.IsBuiltIn ? " (locked; choose Custom to make your own)" : "");
            _game.ForeColor = Theme.Of(Tone.Muted);
        }
        Tip(_game, _game.Text); // full text when it is cut off with "..."
        ApplyLocks();
    }

    /// <summary>Built-in layouts are read-only: no editing the layout file, and no changing the app id when the layout fixes it.</summary>
    void ApplyLocks()
    {
        _editLayout.Enabled = !_busy && !_cfg.IsBuiltIn;
        _appId.Enabled = !_busy && !_cfg.AppIdLocked;
        Tip(_editLayout, _cfg.IsBuiltIn ? "Built-in layouts are locked; choose Custom in the Layout box to edit one" : "Open the layout file (" + ProjectConfig.FileName + ") in your editor");
    }

    static Label Lbl(string t) => new() { Text = t, AutoSize = true };

    Label Hint(string t)
    {
        var h = new Label { Text = t, AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 2, 0, 2), UseMnemonic = false }.Role(ThemeRole.Muted);
        _hints.Add(h);
        return h;
    }

    /// <summary>A raised panel with an optional title (and controls to the right of it) over its rows.</summary>
    static TableLayoutPanel Card(string? title, Control[]? titleExtras, params Control[] rows)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 10) }.Role(ThemeRole.Surface);
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // as wide as its place, never wider
        if (title != null)
        {
            // The title takes the spare width, unless something next to it should (the Project card's summary).
            var extrasFill = titleExtras?.Any(c => (c.Tag as string) == "fill") == true;
            // In capitals with a fading accent line under it, like the section headings on Steam's own pages.
            var head = new Label { Text = title.ToUpperInvariant(), AccessibleName = Display.StripMnemonic(title), AutoSize = true, Font = Theme.SectionFont, Tag = extrasFill ? null : "fill", Margin = new Padding(0, 3, 16, 3) }.Role(ThemeRole.Title);
            var r = Row(new Control[] { head }.Concat(titleExtras ?? Array.Empty<Control>()).ToArray());
            r.Margin = new Padding(0, 0, 0, 2);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(r);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, Margin = new Padding(0, 0, 0, 8) }.Role(ThemeRole.Rule));
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
        b.Font = Theme.SmallFont;
        return b;
    }

    static TableLayoutPanel Row(params Control[] cs)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = cs.Length, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
        string? label = null;
        foreach (var c in cs)
        {
            // Screen readers name an input by the label before it in its row ("Account name", "Theme", ...).
            if (c is Label { Text.Length: > 0 } l and not LinkLabel) label = Display.StripMnemonic(l.Text);
            else if (label != null && c is TextBox or ComboBox && string.IsNullOrEmpty(c.AccessibleName)) c.AccessibleName = label;
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
        Tip(note, note.Text);
    }

    /// <summary>
    /// Says what is missing next to where it is fixed, instead of a pop-up: switches to the right tab, shows the note,
    /// puts the cursor on <paramref name="target"/> and briefly lights it up.
    /// </summary>
    void Nudge(Control target, Label note, string text)
    {
        if (note == _accountNote) ShowOptions(target); else ShowPage(_uploadPage);
        if (note == _projectNote && !_projectOpen) { _projectOpen = true; UpdateProjectCard(); }
        ShowNote(note, text);
        Say(text);
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

    // ---------------------------------------------------------------- remembering the window

    bool _windowRestored;

    string ColumnLayoutKey => $"{DeviceDpi}|{_s.TextSize}";

    /// <summary>Puts the window, the list's columns and sort, and the Options section back the way they were left.</summary>
    void RestoreWindow()
    {
        var b = _s.WindowBounds;
        if (b.Length == 4 && b[2] > 0 && b[3] > 0)
        {
            var rect = new Rectangle(b[0], b[1], b[2], b[3]);
            // Only where enough of its title bar is on a screen that is still there (a monitor may have been unplugged).
            var titleBar = new Rectangle(rect.X, rect.Y, rect.Width, 40);
            if (Screen.AllScreens.FirstOrDefault(sc => { var i = Rectangle.Intersect(sc.WorkingArea, titleBar); return i.Width >= 120 && i.Height >= 20; }) is { } on)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(rect.Location, new Size(Math.Min(rect.Width, on.WorkingArea.Width), Math.Min(rect.Height, on.WorkingArea.Height)));
            }
        }
        if (_s.WindowMaximized) WindowState = FormWindowState.Maximized;
        if (_s.ColumnWidths.Length == _list.Columns.Count && _s.ColumnLayout == ColumnLayoutKey)
            for (var i = 0; i < _list.Columns.Count - 1; i++) // the last column stretches to fill
                _list.Columns[i].Width = Math.Max(U(30), _s.ColumnWidths[i]);
        if (_s.SortColumn >= 0 && _s.SortColumn < _list.Columns.Count) (_sortCol, _sortDesc) = (_s.SortColumn, _s.SortDescending);
        ShowSortArrows();
        if (_sections.Count > 0) ShowSection(_s.OptionsSection);
        _windowRestored = true;
    }

    void SaveWindow()
    {
        var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _s.WindowBounds = new[] { r.X, r.Y, r.Width, r.Height };
        _s.WindowMaximized = WindowState == FormWindowState.Maximized;
        _s.ColumnWidths = _list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
        _s.ColumnLayout = ColumnLayoutKey;
        if (HasUploader && HasDropPod) _s.OpenOnDropPod = _onDropPod;
        _s.Save();
    }

    /// <summary>Once per update (whichever way it was installed), the changelog of the versions just installed.</summary>
    void ShowWhatsNewAfterUpdate()
    {
        var current = UpdateCheck.CurrentVersion;
        var last = _s.LastRunVersion;
        if (last == current) return;
        _s.LastRunVersion = current;
        _s.Save();
        if (!WhatsNew.ShouldShow(last, current)) return;
        var sections = WhatsNew.Between(WhatsNew.BuiltIn(), last, current);
        if (sections.Count == 0) return;
        using var f = new WhatsNewForm($"Updated from {last} to {current}. Here is what changed:", sections);
        f.ShowDialog(this);
    }

    void ShowAllChanges()
    {
        var all = WhatsNew.Between(WhatsNew.BuiltIn(), "0", UpdateCheck.CurrentVersion);
        using var f = new WhatsNewForm($"Every version up to {UpdateCheck.CurrentVersion}, newest first:", all);
        f.ShowDialog(this);
    }

    void ExportSettings()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Export settings", Filter = "Settings export (*.zip)|*.zip", FileName = $"SteamCmdHelper-settings-{DateTime.Now:yyyy-MM-dd}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SaveWindow(); // so the file holds what the window shows now
            var n = SettingsTransfer.Export(dlg.FileName);
            Append($"Exported settings and upload history ({n} files) to {dlg.FileName}. It includes any Discord webhook links, so keep it private.", LogKind.Ok);
        }
        catch (Exception e) { MessageBox.Show(this, "Could not export the settings: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>
    /// Imports an export from another PC: asks where each mods folder that does not exist here is now, keeps a backup of
    /// the current files, then takes the imported values.
    /// </summary>
    void ImportSettings()
    {
        if (_busy) return;
        using var dlg = new OpenFileDialog { Title = "Import settings", Filter = "Settings export (*.zip)|*.zip" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var (imported, error) = SettingsTransfer.Read(dlg.FileName);
        if (imported == null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (MessageBox.Show(this, "Replace this PC's settings and upload history with the ones in this file? The current ones are kept in a backup folder first.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        foreach (var folder in imported.Folders(recent: false).Where(f => !Directory.Exists(f)))
        {
            using var pick = new FolderBrowserDialog
            {
                Description = $"On the old PC, a mods folder was at {folder}. Where is it on this PC? (Cancel to leave it as it was.)",
                UseDescriptionForTitle = true,
            };
            if (pick.ShowDialog(this) == DialogResult.OK) imported.MoveFolder(folder, pick.SelectedPath);
        }
        SettingsTransfer.FitToThisPc(imported);
        try
        {
            var backup = SettingsTransfer.Import(dlg.FileName, imported);
            _s.CopyFrom(imported);
            Append($"Imported settings and upload history from {dlg.FileName}. The previous ones are in {backup}.", LogKind.Ok);
        }
        catch (Exception e) { MessageBox.Show(this, "Could not import the settings: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        FillRecent();
        ShowProfiles();
        ShowAnnounce();
        _ = RefreshAllAsync();
        MessageBox.Show(this, "Settings imported. Restart Modwright so every option shows its imported value.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>The empty mod list says why it is empty and offers the button that fixes it.</summary>
    void UpdateEmptyState()
    {
        var empty = _list.Items.Count == 0;
        _empty.Visible = empty;
        _list.Visible = !empty;
        _detail.Visible = !empty; // nothing to select; the empty state needs the room
        PlaceDetails();
        if (!empty) return;
        _emptyLayout.Visible = _emptyClear.Visible = _emptyNew.Visible = false;
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
            _emptyNew.Visible = !string.IsNullOrWhiteSpace(_cfg.ModMarker);
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
        Step(1, _s.Account.Length > 0, "Steam account: " + _s.Account, "Set your Steam account", () => { ShowOptions(_account); _account.Focus(); Flash(_account); });
        Step(2, hasRepo && _rows.Count > 0, $"{_rows.Count} mod{(_rows.Count == 1 ? "" : "s")} found",
            hasRepo ? "Point at the folder with your mods" : "Pick your mods folder", BrowseRepo);
        Step(3, _cfg.AppIdValid, "App ID " + _cfg.AppId, "Enter the game's app ID", () => { _projectOpen = true; UpdateProjectCard(); _appId.Focus(); Flash(_appId); });
        var done = _stepActions.Count(a => a == null);
        _setupCount.Text = Display.StepsDone(done, _steps.Length);
        _setupBar.Value = done * 100 / _steps.Length;
        var next = Array.FindIndex(_stepActions, a => a != null);
        _setupNext.Visible = next >= 0;
        if (next >= 0)
        {
            _setupNext.Text = (_steps[next].Text[(_steps[next].Text.IndexOf(' ') + 1)..]).Trim();
            Tip(_setupNext, "Next step: " + _setupNext.Text);
        }
        // The next step is the button, so its link steps aside; the others stay links (any order works).
        for (var i = 0; i < _steps.Length; i++) _steps[i].Visible = i != next;
        _setup.Visible = done < _steps.Length;
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
        l.AccessibleName = (done ? "Done: " : $"Step {i + 1}: ") + (done ? doneText : todoText);
        _stepActions[i] = done ? null : act;
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
            var size = U(14);
            var icon = e.Item.GetBounds(ItemBoundsPortion.Icon);
            var x = bounds.Left + Math.Max(U(3), (icon.Left - bounds.Left - size) / 2);
            var box = new Rectangle(x, bounds.Top + (bounds.Height - size) / 2, size, size);
            DrawTick(g, box, e.Item.Checked, CanTick(m));
            bounds = Rectangle.FromLTRB(Math.Max(box.Right + U(4), icon.Right), bounds.Top, bounds.Right, bounds.Bottom);
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
                TextRenderer.DrawText(g, detail, _list.Font, Rectangle.FromLTRB(badge.Right + U(8), text.Top, text.Right, text.Bottom), Theme.Of(tone), Flags);
            // The last cell of a row: outline the row the keyboard is on, so arrowing through the list is easy to follow.
            if (_list.Focused && e.Item.Focused && FocusCues.Keyboard)
            {
                var row = e.Item.Bounds;
                using var pen = new Pen(Theme.FocusColor, Math.Max(2, LogicalToDeviceUnits(2))) { Alignment = PenAlignment.Inset };
                g.DrawRectangle(pen, row.X, row.Y, row.Width - 1, row.Height - 1);
            }
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
        var h = Math.Min(area.Height - U(6), size.Height + U(4));
        var r = new Rectangle(area.Left, area.Top + (area.Height - h) / 2, size.Width + U(18), h);
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
        var thumb = m.PreviewPath.Length > 0 ? Thumbnail(m.PreviewPath, cell.Height - U(6), cell.Width - U(10)) : null;
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

    /// <summary>
    /// A small copy of the preview image, made once per file version and kept for the session. The file's date is
    /// checked once per read of the folder, not on every repaint.
    /// </summary>
    Image? Thumbnail(string path, int height, int maxWidth)
    {
        if (_thumbsChecked.Contains(path) && _thumbs.TryGetValue(path, out var known) && (known.Image == null || known.Image.Height == height)) return known.Image;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { return null; }
        _thumbsChecked.Add(path);
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

        var (all, cfg) = (_mods, _cfg);
        var deps = await Task.Run(() => all.ToDictionary(m => m, m => Dependencies.Find(m, cfg))); // reads every mod's metadata
        var live = await WorkshopStatus.FetchAsync(mods.Select(m => m.WorkshopId!).Concat(deps.Values.SelectMany(d => d)));
        if (!IsDisposed && ReferenceEquals(all, _mods)) { _live = live; UpdateDetail(); }
        var stats = SubscriberStats.Record(mods.Where(m => live.ContainsKey(m.WorkshopId!)).Select(m => live[m.WorkshopId!]), DateOnly.FromDateTime(DateTime.Now));
        NoteSubscriberChanges(mods, stats);
        foreach (var (m, ids) in deps)
            foreach (var w in Dependencies.Problems(ids, live))
                if (!m.Warnings.Contains(w)) { m.Warnings.Add(w); Append($"{m.Name} {w}.", LogKind.Warn); }
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

        var newComments = WorkshopStats.New(WorkshopStats.LoadComments());
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
            var last = history.GetValueOrDefault(m.WorkshopId!);
            if (SubscriberStats.Trend(stats.GetValueOrDefault(m.WorkshopId!), DateOnly.FromDateTime(DateTime.Now),
                    last == null ? null : DateOnly.FromDateTime(last.Time.LocalDateTime)) is { } trend) extra.Add(trend);
            if (newComments.TryGetValue(m.WorkshopId!, out var unread)) extra.Add($"{unread:N0} new comment{(unread == 1 ? "" : "s")} on Steam (right-click > On Steam > Stats and comments).");
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
            if (ReferenceEquals(SelectedMod(), m)) UpdateDetail();
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

        // Roll back: package the commit an old upload came from and send it again, marked as a roll-back.
        var canRollBack = _cfg.UseGitArchive && RepoIsGit;
        var again = new Button { Text = "&Upload this version again...", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Enabled = false };
        Tip(again, canRollBack ? "Re-upload the selected version from git, to roll back a bad update"
            : "Rolling back needs a git repo and git packaging, to get the old files back");
        list.SelectedIndexChanged += (_, _) => again.Enabled = canRollBack && !_busy && list.SelectedItems.Count == 1 && list.SelectedIndices[0] > 0;
        (string Ref, string Version, string Note)? chosen = null;
        again.Click += (_, _) =>
        {
            if (list.SelectedItems.Count != 1 || list.SelectedItems[0].Tag is not UploadEntry e) return;
            var found = GitOps.FindUpload(_s.RepoPath, m.Folder, e.Version, e.Time);
            if (found is not { } at) { MessageBox.Show(f, "git has no commit from the time of that upload, so it cannot be uploaded again.", Text); return; }
            var ask = $"Upload {m.Name} {e.Version} again from {at.Source}?\n\nSubscribers get those files with the note \"rolled back\". " +
                      "The working folder is not changed; raise the version afterwards for the next real update.";
            if (MessageBox.Show(f, ask, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            chosen = (at.Ref, e.Version, GitOps.RollbackNote(e.Version, e.ChangeNote));
            f.DialogResult = DialogResult.OK;
        };
        var export = new Button { Text = "E&xport CSV...", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        export.Click += (_, _) => ExportHistory(entries, m.Folder + "-history.csv", f);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        bar.Controls.Add(again);
        bar.Controls.Add(export);
        again.Visible = entries.Count > 1;
        if (entries.Count > 0) f.Controls.Add(bar);
        if (entries.Count == 0) f.Controls.Add(empty);
        else list.Items[0].Selected = true;
        f.Load += (_, _) => split.SplitterDistance = split.Height / 2;
        Theme.Apply(f);
        if (f.ShowDialog(this) == DialogResult.OK && chosen is { } go) _ = GoAsync(new List<ModInfo> { m }, go);
    }

    /// <summary>Saves <paramref name="entries"/> as CSV where the user picks.</summary>
    void ExportHistory(IReadOnlyCollection<UploadEntry> entries, string name, IWin32Window? owner = null)
    {
        if (entries.Count == 0) { MessageBox.Show(owner ?? this, "There are no uploads from this PC to export yet.", Text); return; }
        using var d = new SaveFileDialog { FileName = name, Filter = "CSV (*.csv)|*.csv", Title = "Export upload history" };
        if (d.ShowDialog(owner ?? this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(d.FileName, Reports.HistoryCsv(entries), new System.Text.UTF8Encoding(true)); // a BOM, so Excel reads accents right
            Append($"Exported {entries.Count} uploads to {d.FileName}.", LogKind.Ok);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(owner ?? this, "Could not save it: " + e.Message, Text); }
    }

    void OnListDoubleClick(object? sender, MouseEventArgs e)
    {
        if (_busy) return;
        var hit = _list.HitTest(e.Location);
        if (hit.Item?.Tag is not ModInfo m || hit.SubItem == null) return;
        var col = hit.Item.SubItems.IndexOf(hit.SubItem);
        if (col == 0) { EditWorkshopPage(m); return; }
        if (col == StatusCol && ModScanner.NeedsChangelogEntry(m, _cfg)) { if (AskChangelogEntry(m, m.Version)) RefreshAll(); return; }
        if (col == VersionCol) ChangeVersion(m);
    }

    /// <summary>Asks for a new version to upload and writes it into the mod's files (double-click the version, or F2).</summary>
    void ChangeVersion(ModInfo m)
    {
        if (_busy) return;
        var entered = PromptVersion(m);
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

    /// <summary>"Change the version of ticked mods...": patch, minor, major or one number for every ticked mod, with one changelog line.</summary>
    void ChangeTickedVersions()
    {
        if (_busy) return;
        var mods = TickedOrSelected();
        if (mods.Count == 0) return;
        using var f = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(480, 330), Font = Font };
        var l = new Label { Text = $"New version for {mods.Count} mod{(mods.Count == 1 ? "" : "s")}:", AutoSize = true, Location = new Point(12, 12) };
        var patch = new RadioButton { Text = "&Patch (1.2.3 to 1.2.4)", AutoSize = true, Location = new Point(12, 38), Checked = true };
        var minor = new RadioButton { Text = "Mi&nor (1.3.0)", AutoSize = true, Location = new Point(190, 38) };
        var major = new RadioButton { Text = "Ma&jor (2.0.0)", AutoSize = true, Location = new Point(320, 38) };
        var exact = new RadioButton { Text = "&All to", AutoSize = true, Location = new Point(12, 68) };
        var number = new TextBox { Location = new Point(90, 66), Width = 120, AccessibleName = "Version for all", PlaceholderText = "e.g. 1.4.0" };
        number.TextChanged += (_, _) => exact.Checked = number.TextLength > 0;
        var nl = new Label { Text = "&Changelog entry for the mods whose layout needs one (RimWorld):", AutoSize = true, Location = new Point(12, 102) };
        var note = new TextBox { Text = "- ", Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Location = new Point(12, 124), Size = new Size(456, 140) };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(312, 290), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(393, 290), Width = 75 };
        f.Controls.AddRange(new Control[] { l, patch, minor, major, exact, number, nl, note, ok, cancel });
        f.CancelButton = cancel;
        ok.Role(ThemeRole.Accent);
        Theme.Apply(f);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var spec = exact.Checked ? number.Text.Trim() : major.Checked ? "major" : minor.Checked ? "minor" : "patch";
        if (spec.Length == 0) return;
        var text = note.Text.Replace("\r\n", "\n").Trim();
        VersionBatch.Apply(mods, _cfg, spec, text is "" or "-" ? null : text, line => Append(line, line.Contains("needs one") || line.Contains("cannot") ? LogKind.Warn : LogKind.Note));
        RefreshAll();
    }

    /// <summary>"Add a game version to ticked mods...": a new RimWorld version in supportedVersions, version folders and loadFolders.xml.</summary>
    void AddGameVersion()
    {
        if (_busy) return;
        var mods = TickedOrSelected();
        if (mods.Count == 0) return;
        var suggested = GameVersions.Next(NewMod.NewestVersion(ModScanner.ModsRoot(_s.RepoPath, _cfg)));
        var version = Prompt($"Game version to add to {(mods.Count == 1 ? mods[0].Name : mods.Count + " mods")}:", suggested);
        if (string.IsNullOrWhiteSpace(version)) return;
        var copy = true;
        if (mods.Any(m => Directory.GetDirectories(m.Dir).Any(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^\d+\.\d+$"))))
        {
            var yes = new TaskDialogButton("Copy them");
            var page = new TaskDialogPage
            {
                Caption = Text, Heading = "Copy the newest version folders?", Icon = TaskDialogIcon.Information,
                Text = $"Some of these mods keep files in version folders (such as 1.6). Copy each mod's newest one to {version.Trim()} and add it to loadFolders.xml? " +
                       "Their Assemblies are copied as they are, so rebuild them against the new game version.",
                Buttons = { yes, new TaskDialogButton("Only add it to About.xml") }, DefaultButton = yes,
            };
            copy = TaskDialog.ShowDialog(this, page) == yes;
        }
        foreach (var m in mods)
        {
            var r = GameVersions.Add(m.Dir, version, copy);
            if (r.Error != null) Append($"{m.Name}: {r.Error}", LogKind.Warn);
            else Append($"{m.Name}: " + (r.Done.Count == 0 ? $"already supports {version.Trim()}." : string.Join("; ", r.Done) + "."), LogKind.Note);
        }
        RefreshAll();
    }

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

    void ShowStats(ModInfo? selected)
    {
        if (!_mods.Any(m => m.OnSteam)) { Append("None of these mods is on Steam yet, so there are no Workshop stats.", LogKind.Note); return; }
        using var f = new StatsForm(_mods, selected, SubscriberStats.Load(), () => CountCommentsAsync(quiet: false));
        f.ShowDialog(this);
    }

    /// <summary>
    /// Counts the comments on every mod through the Steam client and remembers them; logs which mods have new ones.
    /// Null when Steam could not be asked. <paramref name="quiet"/> keeps failures out of the log (the start-up check).
    /// </summary>
    async Task<Dictionary<string, long>?> CountCommentsAsync(bool quiet)
    {
        var mods = _mods.Where(m => m.OnSteam).ToList();
        if (mods.Count == 0 || !_cfg.AppIdValid) return null;
        var r = await WorkshopExtras.ApplyOnSteamAsync(new ExtrasRequest { AppId = _cfg.AppId, CountComments = mods.Select(m => m.WorkshopId!).ToArray() }, CancellationToken.None);
        if (!r.Ok || r.Comments == null)
        {
            if (!quiet) Append("Could not count comments: " + r.Error, LogKind.Warn);
            return null;
        }
        _s.CommentsCheckedAt = DateTime.UtcNow;
        _s.Save();
        var fresh = WorkshopStats.TakeCounts(r.Comments);
        foreach (var m in mods)
            if (fresh.TryGetValue(m.WorkshopId!, out var n)) Append($"{m.Name} has {n:N0} new comment{(n == 1 ? "" : "s")} on Steam (right-click > On Steam > Stats and comments).", LogKind.Note);
        if (!quiet && fresh.Count == 0) Append("No new comments on your mods.", LogKind.Ok);
        return fresh;
    }

    /// <summary>Options > Uploading > Restart Steam now: brings the Steam client back online after anything SteamCMD logged in for.</summary>
    async Task RestartSteamNowAsync()
    {
        if (_busy) return;
        _restartSteam.Enabled = false;
        UseWaitCursor = true;
        Append("Restarting the Steam client...", LogKind.Note);
        try
        {
            var said = await SteamRestart.RestartAsync(null);
            Append(said, said.StartsWith("Warning:") ? LogKind.Warn : said.StartsWith("Steam is back") ? LogKind.Ok : LogKind.Note);
        }
        finally { _restartSteam.Enabled = !_busy; UseWaitCursor = false; }
    }

    /// <summary>At start-up, when turned on and Steam is running: count comments, at most every six hours.</summary>
    async Task StartupCommentsAsync()
    {
        if (!_s.CountCommentsAtStartup || DateTime.UtcNow - _s.CommentsCheckedAt < TimeSpan.FromHours(6)) return;
        if (Process.GetProcessesByName("steam").Length == 0) return;
        await CountCommentsAsync(quiet: true);
    }

    /// <summary>
    /// One visibility, text replacement or addition for every ticked mod's Workshop page, saved as each mod's page
    /// changes for its next upload. Descriptions start from a change already waiting, else from Steam's page; an item
    /// whose page can't be read (private, offline) keeps its description, so nothing on Steam is lost.
    /// </summary>
    async Task EditTickedPagesAsync()
    {
        var mods = Selected().ToList();
        if (_busy || mods.Count == 0) return;
        BulkPageEdit edit;
        using (var f = new BulkPageForm(mods))
        {
            if (f.ShowDialog(this) != DialogResult.OK) return;
            edit = f.Result;
        }
        UseWaitCursor = true;
        var changed = 0;
        try
        {
            foreach (var m in mods)
            {
                var page = WorkshopPage.Get(m.Dir) ?? new WorkshopPageEdit();
                var parts = new List<string>();
                if (edit.Visibility is { } v && page.Visibility != v) { page.Visibility = v; parts.Add("visibility"); }
                if (edit.ChangesDescriptions)
                {
                    var current = page.Description
                        ?? (m.OnSteam ? (await WorkshopPage.FetchAsync(m.WorkshopId!))?.Description : Publisher.DescriptionFor(m, _cfg));
                    if (current == null) Append($"{m.Name}: Steam's page could not be read (private or offline), so its description was left alone.", LogKind.Warn);
                    else if (WorkshopPage.EditText(current, edit.Find, edit.Replace, edit.Append) is var text && text != current.Replace("\r\n", "\n"))
                    {
                        if (text.Length > WorkshopPage.MaxDescription) { Append($"{m.Name}: the description would be {text.Length} characters (Steam allows {WorkshopPage.MaxDescription}), so it was left alone.", LogKind.Warn); }
                        else { page.Description = text; parts.Add("description"); }
                    }
                }
                if (parts.Count == 0) continue;
                WorkshopPage.Set(m.Dir, page);
                changed++;
                Append($"{m.Name}: Workshop page changes ({page.Summary()}) will be sent with the next upload.", LogKind.Note);
            }
        }
        catch (Exception e) { Append("Could not save the Workshop page changes: " + e.Message, LogKind.Error); }
        finally { UseWaitCursor = false; }
        if (changed == 0) Append("None of the ticked mods' pages needed that change.", LogKind.Note);
        RefreshAll();
    }

    /// <summary>
    /// Drafts a mod's change note from the commits that touched it since its last upload from this PC (its upload tag,
    /// else the upload's time). Null when the repo is not a git repo.
    /// </summary>
    Func<ModInfo, GitOps.CommitNote?>? DraftNoteFn()
    {
        if (!RepoIsGit) return null;
        var repo = _s.RepoPath;
        var history = UploadHistory.Load();
        var record = UploadedVersions.LoadRecord();
        return m =>
        {
            var last = m.OnSteam ? UploadHistory.For(history, m.WorkshopId!).FirstOrDefault() : null;
            var version = last?.Version ?? (m.OnSteam ? record.GetValueOrDefault(m.WorkshopId!) : null);
            return GitOps.DraftNote(repo, m.RelPath, m.Folder, version, last?.Time);
        };
    }

    /// <summary>Asks for the release notes of <paramref name="version"/> and adds them to the mod's changelog. True when written.</summary>
    bool AskChangelogEntry(ModInfo m, string version)
    {
        var draft = DraftNoteFn();
        var notes = PromptLines($"What changed in {m.Name} {version}?",
            $"Added to {_cfg.Changelog} as the {version} entry; Steam shows it as the change note.", "- ",
            draft == null ? null : () => draft(m));
        if (notes == null) return false;
        var error = ModScanner.AddChangelogEntry(m, _cfg, version, notes);
        if (error != null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
        Append($"{m.Name}: added the {version} entry to {_cfg.Changelog} (commit it).", LogKind.Note);
        return true;
    }

    /// <summary>
    /// A multi-line text box dialog. Null when cancelled. With <paramref name="draft"/>, a "Draft from git commits" button
    /// fills the box from the mod's commits.
    /// </summary>
    string? PromptLines(string label, string hint, string value, Func<GitOps.CommitNote?>? draft = null)
    {
        using var f = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(480, 300), Font = Font };
        var l = new Label { Text = label, AutoSize = true, Location = new Point(12, 12) };
        var t = new TextBox { Text = value, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Location = new Point(12, 36), Size = new Size(456, 180) };
        var h = new Label { Text = hint, AutoSize = true, MaximumSize = new Size(456, 0), Location = new Point(12, 222) }.Role(ThemeRole.Muted);
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(312, 262), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(393, 262), Width = 75 };
        f.Controls.AddRange(new Control[] { l, t, h, ok, cancel });
        if (draft != null)
        {
            var fromGit = new Button { Text = "&Draft from git commits", AutoSize = true, Location = new Point(12, 262) };
            fromGit.Click += (_, _) =>
            {
                var n = draft();
                if (n == null) { h.Text = "No commits touched this mod since its last upload from this PC."; return; }
                t.Text = n.Note.Replace("\n", "\r\n");
                h.Text = $"Drafted from {n.Source}. Edit it before saving.";
                t.Focus();
            };
            f.Controls.Add(fromGit);
        }
        f.CancelButton = cancel; // no AcceptButton: Enter starts a new line
        ok.Role(ThemeRole.Accent);
        Theme.Apply(f);
        f.Shown += (_, _) => { t.SelectionStart = t.TextLength; t.SelectionLength = 0; };
        return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
    }

    /// <summary>
    /// The version box for <see cref="ChangeVersion"/>, with Patch, Minor and Major buttons that fill in the next version
    /// (Alt+P, Alt+N, Alt+J). Null when cancelled.
    /// </summary>
    string? PromptVersion(ModInfo m)
    {
        using var f = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(360, 146), Font = Font };
        var l = new Label { Text = $"Version to upload for {m.Name}:", AutoSize = true, Location = new Point(12, 12) };
        var t = new TextBox { Text = m.Version, Location = new Point(12, 36), Width = 336, AccessibleName = "Version" };
        var bumps = new List<Control>();
        var x = 12;
        // The Launch Pad's hint: what the commits since the last upload point to.
        var hint = SuggestedBump(m);
        Button? suggested = null;
        foreach (var (part, label) in new[] { (ModScanner.Bump.Patch, "&Patch"), (ModScanner.Bump.Minor, "Mi&nor"), (ModScanner.Bump.Major, "Ma&jor") })
        {
            var next = ModScanner.NextVersion(m.Version, part);
            var b = new Button { Text = next == null ? label : $"{label} {next}", Location = new Point(x, 68), Width = 108, AutoEllipsis = true, Enabled = next != null };
            b.Click += (_, _) => { t.Text = next; t.Focus(); t.SelectAll(); };
            if (hint?.Part == part) suggested = b;
            bumps.Add(b);
            x += 114;
        }
        var extra = 0;
        if (hint is { } h)
        {
            var why = new Label
            {
                AutoSize = true, MaximumSize = new Size(336, 0), Location = new Point(12, 102), UseMnemonic = false,
                Text = $"The commits since the last upload suggest a {h.Part.ToString().ToLowerInvariant()} bump (\"{(h.Reason.Length > 70 ? h.Reason[..70] + "..." : h.Reason)}\").",
            }.Role(ThemeRole.Muted);
            bumps.Add(why);
            extra = 40;
        }
        f.ClientSize = new Size(360, 146 + extra);
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(192, 108 + extra), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(273, 108 + extra), Width = 75 };
        f.Controls.Add(l); f.Controls.Add(t); f.Controls.AddRange(bumps.ToArray()); f.Controls.Add(ok); f.Controls.Add(cancel);
        f.AcceptButton = ok; f.CancelButton = cancel;
        ok.Role(ThemeRole.Accent);
        suggested?.Role(ThemeRole.Primary);
        Theme.Apply(f);
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
        if (_busy && !_populating) { e.NewValue = e.CurrentValue; return; } // the batch is already picked
        if (_populating || e.NewValue != CheckState.Checked) return;
        if (_list.Items[e.Index].Tag is ModInfo m && !CanTick(m)) e.NewValue = CheckState.Unchecked;
    }

    /// <summary>Ticks every shown mod that can be picked (the same rules as ticking by hand), or clears them all.</summary>
    /// <summary>Ticks the mods whose version Steam does not have yet (Changed, or on Steam with the version not known), like upload --changed.</summary>
    void TickChanged()
    {
        if (_busy) return;
        _list.BeginUpdate();
        foreach (var it in _rows)
        {
            var m = (ModInfo)it.Tag!;
            it.Checked = m.OnSteam && CanTick(m) && StateOf(it) is RowState.Changed or RowState.Ready;
        }
        _list.EndUpdate();
        UpdateGoButton();
    }

    /// <summary>The Groups button's menu: each saved group of this mods folder (click to tick it), then save and delete.</summary>
    void ShowGroupsMenu()
    {
        if (_busy) return;
        var groups = _s.GroupsFor(_s.RepoPath);
        var menu = new ContextMenuStrip();
        foreach (var (name, mods) in groups)
            menu.Items.Add($"{name}  ({mods.Count})", null, (_, _) => TickGroup(name));
        if (groups.Count > 0) menu.Items.Add(new ToolStripSeparator());
        var save = menu.Items.Add("&Save the ticked mods as a group...", null, (_, _) => SaveGroup());
        save.Enabled = Selected().Any();
        if (groups.Count > 0)
        {
            var delete = new ToolStripMenuItem("&Delete a group");
            foreach (var name in groups.Keys) delete.DropDownItems.Add(name, null, (_, _) =>
            {
                _s.SetGroup(_s.RepoPath, name, Array.Empty<string>());
                _s.Save();
                Append($"Deleted the group {name}.", LogKind.Note);
            });
            menu.Items.Add(delete);
        }
        Theme.StyleMenu(menu);
        menu.Show(_groups, new Point(0, _groups.Height));
    }

    /// <summary>Ticks exactly the mods of the group <paramref name="name"/>; mods that left the folder are named in the log.</summary>
    void TickGroup(string name)
    {
        if (!_s.GroupsFor(_s.RepoPath).TryGetValue(name, out var paths)) return;
        var want = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _list.BeginUpdate();
        var ticked = 0;
        foreach (var it in _rows)
        {
            var m = (ModInfo)it.Tag!;
            it.Checked = want.Contains(m.RelPath) && CanTick(m);
            if (it.Checked) ticked++;
        }
        _list.EndUpdate();
        UpdateGoButton();
        var gone = paths.Where(p => !_rows.Any(it => ((ModInfo)it.Tag!).RelPath.Equals(p, StringComparison.OrdinalIgnoreCase))).ToList();
        Append($"Ticked {ticked} of the {paths.Count} mods in {name}." + (gone.Count > 0 ? $" Not in this folder any more: {string.Join(", ", gone)}." : "")
            + (ticked < paths.Count - gone.Count ? " The others can't be uploaded right now (see their Status)." : ""), LogKind.Note);
    }

    void SaveGroup()
    {
        var ticked = Selected().ToList();
        if (ticked.Count == 0) return;
        var name = Prompt($"Name for this group of {ticked.Count} mod{(ticked.Count == 1 ? "" : "s")}:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        _s.SetGroup(_s.RepoPath, name, ticked.Select(m => m.RelPath));
        _s.Save();
        Append($"Saved the group {name.Trim()}: {string.Join(", ", ticked.Select(m => m.Name))}. From a script: SteamCmdHelper upload --group \"{name.Trim()}\"", LogKind.Ok);
    }

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

    bool _goQueued, _detailQueued;

    /// <summary>
    /// Updates the details panel once the selection settles: moving to another row first unselects the old one, and
    /// drawing the panel for that moment in between made it blink through the "nothing selected" summary.
    /// </summary>
    void QueueDetail()
    {
        if (_detailQueued) return;
        if (!IsHandleCreated) { UpdateSelectionButtons(); UpdateDetail(); return; }
        _detailQueued = true;
        BeginInvoke(() =>
        {
            _detailQueued = false;
            if (IsDisposed) return;
            UpdateSelectionButtons();
            UpdateDetail();
        });
    }

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
        _retry.Text = $"Retr&y failed ({_retrySet.Count})";
        _retry.Visible = !_busy && _retrySet.Count > 0 && _mods.Any(m => _retrySet.Contains(m.Dir));
    }

    /// <summary>
    /// Locks what would change a running upload. The mod list, its search and Hide up to date stay usable, so the list
    /// can be read while an upload runs; ticks and the list's edit actions wait for it.
    /// </summary>
    void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var c in new Control[] { _browse, _editLayout, _refresh, _all, _none, _checkLogin, _doLogin, _install, _repo, _account, _dry, _allowCreate, _appId, _preset, _theme, _textSize, _checkUpdateNow, _profile, _profileSave, _webhook, _webhookTest, _ghReleases, _tsTeam, _tsCommunity, _tsCategories, _tsToken, _tsAfter, _uploadCredit, _markdownNotes, _noteTemplate, _descFooter, _checklist, _checklistMust, _testItems, _changed, _groups, _afterCmd, _pushTags, _keepPackages, _buildCmd, _packageRef, _autoRetry, _updateTags, _updateRequired, _updatePageExtras, _oneSession, _verifyUploads, _steamBack, _restartSteam, _watchBuilds, _strictAbout })
            c.Enabled = !busy;
        ApplyLocks();
        _cancel.Visible = busy;
        _cancel.Enabled = busy;
        _details.SetBusy(busy);
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
        Tip(_login, text);
    }

    void UpdateLoginHint()
    {
        var steam = _s.EffectiveSteamCmd;
        var installed = SteamLogin.SteamCmdInstalled(steam);
        _install.Visible = !installed;
        _steamCmdPath.Text = installed ? steam : "Not installed. Install SteamCMD puts it in " + Path.GetDirectoryName(steam);
        Tip(_steamCmdPath, steam);
        if (!installed) SetLogin("SteamCMD is not installed - uploads won't work without it", Tone.Error);
        else if (string.IsNullOrWhiteSpace(_s.Account)) SetLogin("No Steam account set", Tone.Warn);
        else if (SteamLogin.LooksCached(steam, _s.Account)) SetLogin($"Saved login found for {_s.Account} (not checked yet)", Tone.Info);
        else SetLogin($"No saved login for {_s.Account} - you'll be asked to log in once", Tone.Warn);
    }

    // ---------------------------------------------------------------- log

    static Color LogColor(LogKind k) => Theme.HighContrast ? Theme.LogText : k switch
    {
        LogKind.Note => Color.DeepSkyBlue,
        LogKind.Ok => Color.LightGreen,
        LogKind.Warn => Color.Orange,
        LogKind.Error => Color.Tomato,
        _ => Theme.LogText,
    };

    static Color LogTime => Theme.HighContrast ? SystemColors.GrayText : Color.FromArgb(118, 122, 134);

    LogFilter CurrentLogFilter => (LogFilter)Math.Max(0, _logFilter.SelectedIndex);

    /// <summary>Lines added since the log box was last drawn; they are drawn together (see <see cref="FlushLog"/>).</summary>
    readonly List<(DateTime Time, string Text, LogKind Kind)> _pendingLog = new();
    readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 100 };
    /// <summary>Lines in the log box; it is trimmed back to <see cref="MaxLogLines"/> once it holds this many more.</summary>
    int _logBoxLines;
    const int LogTrimSlack = 2000;

    /// <summary>
    /// Adds a line with the time in front, coloured by what kind of line it is. The line is kept at once, but the log box
    /// draws new lines about ten times a second, so SteamCMD printing hundreds of lines doesn't slow the window down.
    /// </summary>
    void Append(string line, LogKind kind = LogKind.Plain)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(line, kind)); return; }
        var entry = (DateTime.Now, line, kind);
        _logLines.Add(entry);
        if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        if (!Display.Shows(CurrentLogFilter, kind)) return;
        _pendingLog.Add(entry);
        // --shot and a window not shown yet draw straight away; otherwise the timer gathers lines.
        if (NoStartupPrompts || !_log.IsHandleCreated) FlushLog();
        else _logTimer.Enabled = true;
    }

    /// <summary>Draws the lines <see cref="Append"/> gathered, then trims the log box if it grew too long.</summary>
    void FlushLog()
    {
        _logTimer.Enabled = false;
        if (_pendingLog.Count == 0) return;
        if (!_log.IsHandleCreated)
        {
            foreach (var e in _pendingLog) Write(e);
            _logBoxLines += _pendingLog.Count;
            _pendingLog.Clear();
            return;
        }
        var follow = _autoScroll.Checked;
        // With auto-scroll off, the new lines go at the end but the view (and any selection) stays where the reader has it.
        var first = SendMessage(_log.Handle, EmGetFirstVisibleLine, 0, 0);
        var (start, length) = (_log.SelectionStart, _log.SelectionLength);
        SendMessage(_log.Handle, WmSetRedraw, 0, 0);
        try
        {
            foreach (var e in _pendingLog) Write(e);
            _logBoxLines += _pendingLog.Count;
            _pendingLog.Clear();
            if (_logBoxLines > MaxLogLines + LogTrimSlack)
            {
                var cut = _log.GetFirstCharIndexFromLine(_logBoxLines - MaxLogLines);
                if (cut > 0)
                {
                    _log.Select(0, cut);
                    _log.SelectedText = ""; // works on a read-only box, like AppendText
                    first = Math.Max(0, first - (_logBoxLines - MaxLogLines));
                    start = Math.Max(0, start - cut);
                    _logBoxLines = MaxLogLines;
                }
            }
            if (follow)
            {
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
            }
            else
            {
                _log.Select(start, length);
                SendMessage(_log.Handle, EmLineScroll, 0, first - SendMessage(_log.Handle, EmGetFirstVisibleLine, 0, 0));
            }
        }
        finally
        {
            SendMessage(_log.Handle, WmSetRedraw, 1, 0);
            _log.Invalidate();
        }
        if (follow) _log.ScrollToCaret();
    }

    void Write((DateTime Time, string Text, LogKind Kind) e)
    {
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = LogTime;
        _log.AppendText(e.Time.ToString("HH:mm:ss") + "  ");
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = LogColor(e.Kind);
        _log.AppendText(e.Text + "\n");
        _log.SelectionColor = _log.ForeColor;
    }

    /// <summary>Shows the lines the log filter lets through (after the filter or the colours changed).</summary>
    void RenderLog()
    {
        if (!_log.IsHandleCreated) return;
        SendMessage(_log.Handle, WmSetRedraw, 0, 0);
        try
        {
            _log.Clear();
            _pendingLog.Clear();
            _logBoxLines = 0;
            foreach (var e in _logLines)
                if (Display.Shows(CurrentLogFilter, e.Kind)) { Write(e); _logBoxLines++; }
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        finally
        {
            SendMessage(_log.Handle, WmSetRedraw, 1, 0);
            _log.Invalidate();
        }
    }

    const int WmSetRedraw = 0x000B, EmGetFirstVisibleLine = 0x00CE, EmLineScroll = 0x00B6;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hwnd, int msg, int wParam, int lParam);

    /// <summary>The whole log as text, whatever the filter shows.</summary>
    string LogText() => string.Join(Environment.NewLine, _logLines.Select(e => e.Time.ToString("HH:mm:ss") + "  " + e.Text));

    void CopyLog()
    {
        if (_logLines.Count == 0) return;
        try { Clipboard.SetText(LogText()); }
        catch (Exception e) { Append("Could not copy the log: " + e.Message, LogKind.Error); }
    }

    void SaveLog()
    {
        using var d = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", FileName = $"SteamCmdHelper-log-{DateTime.Now:yyyy-MM-dd-HHmm}.txt" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(d.FileName, LogText() + Environment.NewLine); Append("Log saved to " + d.FileName, LogKind.Note); }
        catch (Exception e) { Append("Could not save the log: " + e.Message, LogKind.Error); }
    }

    /// <summary>Saves the log lines from <paramref name="from"/> on as this upload run's own log.</summary>
    void SaveRunLog(int from, DateTime started, string label)
    {
        var lines = _logLines.Skip(Math.Min(from, _logLines.Count))
            .Select(e => e.Time.ToString("HH:mm:ss") + "  " + e.Text);
        RunLogs.Save(RunLogs.DefaultDir, label, started, string.Join(Environment.NewLine, lines));
    }

    void ShowPastLogs()
    {
        using var f = new PastLogsForm(RunLogs.DefaultDir);
        f.ShowDialog(this);
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
            NoteLogin(ok);
            if (ok) SetLogin($"Logged in as {_s.Account}", Tone.Ok);
            else SetLogin($"Not logged in as {_s.Account}", Tone.Error);
            if (announce || !ok) Say(_login.Text);
            if (announce || !ok) Append(ok ? $"Steam login OK for {_s.Account}." : $"Steam login check failed for {_s.Account} (no saved login, wrong name, or no connection).", ok ? LogKind.Ok : LogKind.Warn);
            return ok;
        }
        catch (Exception e) { SetLogin("Login check failed", Tone.Error); Append("Login check error: " + e.Message, LogKind.Error); return false; }
        finally { if (!wasBusy) SetBusy(false); }
    }

    /// <summary>When this account's saved login last worked (a login check or an upload), so the next upload can skip checking.</summary>
    (string Account, DateTime At)? _loginOk;

    /// <summary>A login that worked this recently is trusted without starting SteamCMD to check it again.</summary>
    static readonly TimeSpan TrustLoginFor = TimeSpan.FromMinutes(15);

    void NoteLogin(bool ok) => _loginOk = ok ? (_s.Account, DateTime.UtcNow) : null;

    bool LoginRecentlyOk => _loginOk is { } l && string.Equals(l.Account, _s.Account, StringComparison.OrdinalIgnoreCase)
        && DateTime.UtcNow - l.At < TrustLoginFor;

    /// <summary>
    /// Checks the saved login; if there isn't one, opens SteamCMD so you can log in, waits, then checks again. With
    /// <paramref name="trustRecent"/>, a login that worked in the last few minutes is not checked again: the SteamCMD
    /// run that follows logs in anyway, and a refused login there offers "Log in again and retry".
    /// </summary>
    async Task<bool> LoginFlowAsync(bool trustRecent = false)
    {
        if (string.IsNullOrWhiteSpace(_s.Account)) { Nudge(_account, _accountNote, "Type your Steam account name here first."); return false; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { Nudge(_install, _accountNote, "SteamCMD is not installed yet. Press Install SteamCMD below."); return false; }
        if (trustRecent && LoginRecentlyOk) return await OwnershipOkAsync();

        if (await VerifyLoginAsync(false)) return await OwnershipOkAsync();

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

        return await VerifyLoginAsync(true) && await OwnershipOkAsync();
    }

    /// <summary>
    /// Once per account and game: asks SteamCMD whether the account has a license for the game, since Steam refuses
    /// Workshop uploads and downloads from accounts that don't (Family Sharing does not count). True to go on.
    /// </summary>
    async Task<bool> OwnershipOkAsync()
    {
        if (!_cfg.AppIdValid) return true;
        var key = $"{_s.Account}|{_cfg.AppId}".ToLowerInvariant();
        if (_s.OwnershipChecked.Contains(key)) return true;
        bool? owns;
        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        SetLogin("Checking the account owns the game...", Tone.Muted);
        try { owns = await SteamCmd.OwnsAsync(_s.EffectiveSteamCmd, _s.Account, _cfg.AppId, CancellationToken.None); }
        catch { owns = null; }
        finally { if (!wasBusy) SetBusy(false); SetLogin($"Logged in as {_s.Account}", Tone.Ok); }
        if (owns == true) { _s.OwnershipChecked.Add(key); _s.Save(); return true; }
        if (owns == null) return true; // could not tell; Steam says so itself if it refuses
        Append($"Steam's license list for {_s.Account} does not include {GameLabel()} (app {_cfg.AppId}).", LogKind.Warn);
        return MessageBox.Show(this,
            $"Steam's license list for {_s.Account} does not include {GameLabel()} (app {_cfg.AppId}).\n\n" +
            "Steam only takes Workshop uploads and downloads from an account that owns the game. A game borrowed through Family Sharing does not count.\n\nContinue anyway?",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    string GameLabel() => _cfg.GameName.Length > 0 ? _cfg.GameName : "app " + _cfg.AppId;

    /// <summary>Writes a log line from any thread.</summary>
    void Log(string line)
    {
        if (InvokeRequired) BeginInvoke(() => Append(line, Display.KindOf(line)));
        else Append(line, Display.KindOf(line));
    }

    /// <summary>Runs a SteamCMD job with the window busy and Esc able to stop it.</summary>
    async Task RunSteamJobAsync(string what, Func<CancellationToken, Task> job)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        try { await job(_cts.Token); }
        catch (OperationCanceledException) { Append(what + " stopped.", LogKind.Warn); }
        catch (Exception e) { Append($"{what} failed: {e.Message}", LogKind.Error); }
        finally { SetBusy(false); _cts.Dispose(); _cts = null; }
    }

    /// <summary>At start-up: the saved login (when there seems to be one), then whether the game updated.</summary>
    async Task StartupSteamAsync(bool loginCached)
    {
        if (loginCached) await VerifyLoginAsync(false);
        if (_s.WatchGameBuilds && !NoStartupPrompts && !IsDisposed) await CheckGameBuildsAsync(false);
    }

    /// <summary>
    /// Reads the game's Steam branches (anonymous login) and logs what changed since the last look. With
    /// <paramref name="list"/> every branch is listed too; without it nothing runs until <see cref="GameBuilds.CheckEvery"/> has passed.
    /// </summary>
    async Task CheckGameBuildsAsync(bool list)
    {
        if (_busy || !_cfg.AppIdValid || !SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd))
        {
            if (list) Append(_busy ? "Busy; try again when the current job ends." : !_cfg.AppIdValid ? "Set the game's app ID first." : "Install SteamCMD first.", LogKind.Warn);
            return;
        }
        if (!list && !GameBuilds.Due(_cfg.AppId, DateTimeOffset.UtcNow)) return;
        var appId = _cfg.AppId;
        if (list) Append($"Asking Steam for {GameLabel()}'s branches (anonymous login, no account needed)...", LogKind.Note);
        List<GameBranch>? found = null;
        await RunSteamJobAsync("Reading the game's branches", async ct => found = await GameBuilds.FetchAsync(_s.EffectiveSteamCmd, appId, ct));
        if (found == null) { if (list) Append("SteamCMD did not return the game's branches. Steam may be down, or the game hides them.", LogKind.Warn); return; }
        _branches = found;
        if (list) foreach (var b in found) Append("  " + GameBuilds.Describe(b));
        var before = GameBuilds.Load().GetValueOrDefault(appId);
        var changes = GameBuilds.Changes(found, before, _cfg.GameName);
        foreach (var c in changes) Append(c, LogKind.Warn);
        if (list && before != null && changes.Count == 0) Append($"Nothing changed since {before.Checked.ToLocalTime():d MMM yyyy HH:mm}.", LogKind.Note);
        GameBuilds.Remember(appId, found);
    }

    /// <summary>Downloads the mod the way subscribers get it and compares it with this PC's last upload of it.</summary>
    async Task CheckSteamCopyAsync(ModInfo m)
    {
        if (_busy || m.WorkshopId == null) return;
        var sent = PackageDiffs.Load(PackageDiffs.DefaultDir, m.WorkshopId);
        if (sent == null) { Append($"{m.Name}: this PC has no record of what its last upload sent, so there is nothing to compare with. Upload it once from here first.", LogKind.Warn); return; }
        if (!await LoginFlowAsync(trustRecent: true)) return;
        Append($"{m.Name}: downloading Workshop item {m.WorkshopId} as subscribers get it...", LogKind.Note);
        await RunSteamJobAsync("Checking Steam's copy", async ct =>
        {
            var d = await WorkshopDownload.DownloadAsync(_s.EffectiveSteamCmd, _s.Account, _cfg.AppId, m.WorkshopId, Log, ct);
            if (!d.Ok) { Append($"{m.Name}: {d.Message}", LogKind.Error); return; }
            try
            {
                var check = await Task.Run(() => WorkshopDownload.Compare(sent.Files, d.Folder!), ct);
                foreach (var line in WorkshopDownload.Describe(check, $"the upload of v{sent.Version} on {sent.Time.ToLocalTime():d MMM yyyy}"))
                    Append($"{m.Name}: {line}", check.Matches ? LogKind.Ok : Display.KindOf(line));
            }
            finally { WorkshopDownload.Forget(_s.EffectiveSteamCmd, d.Folder); }
        });
    }

    /// <summary>Saves Steam's copy of the mod into a folder, for when the local copy is lost or broken.</summary>
    async Task DownloadFromSteamAsync(ModInfo m)
    {
        if (_busy || m.WorkshopId == null) return;
        using var pick = new FolderBrowserDialog { Description = $"Pick where to save {m.Name} from Steam", UseDescriptionForTitle = true };
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        var dest = pick.SelectedPath;
        if (Directory.EnumerateFileSystemEntries(dest).Any()) dest = Path.Combine(dest, $"{m.Folder} (from Steam)");
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
        {
            MessageBox.Show(this, $"{dest} is not empty. Pick an empty folder, so nothing is overwritten.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!await LoginFlowAsync(trustRecent: true)) return;
        Append($"{m.Name}: downloading Workshop item {m.WorkshopId}...", LogKind.Note);
        await RunSteamJobAsync("Downloading from Steam", async ct =>
        {
            var d = await WorkshopDownload.DownloadAsync(_s.EffectiveSteamCmd, _s.Account, _cfg.AppId, m.WorkshopId, Log, ct);
            if (!d.Ok) { Append($"{m.Name}: {d.Message}", LogKind.Error); return; }
            try
            {
                var n = await Task.Run(() => WorkshopDownload.CopyTo(d.Folder!, dest), ct);
                Append($"{m.Name}: saved {n} files from Steam to {dest}.", LogKind.Ok);
                OpenPath(dest);
            }
            finally { WorkshopDownload.Forget(_s.EffectiveSteamCmd, d.Folder); }
        });
    }

    /// <summary>Installs a test copy of the game on a branch, optionally with the selected mod and what it needs.</summary>
    async Task TryInGameBetaAsync(ModInfo? m)
    {
        if (_busy) return;
        if (!_cfg.AppIdValid) { Append("Set the game's app ID first.", LogKind.Warn); return; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { Append("Install SteamCMD first (Options > SteamCMD > Install SteamCMD).", LogKind.Warn); return; }
        var appId = _cfg.AppId;
        if (_branches == null)
            await RunSteamJobAsync("Reading the game's branches", async ct => _branches = await GameBuilds.FetchAsync(_s.EffectiveSteamCmd, appId, ct));
        using var f = new TestInstallForm(_cfg.GameName, appId, _branches, Path.Combine(AppSettings.Folder, "test-installs", appId), m, _cfg.GameModsFolder);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        if (!await LoginFlowAsync(trustRecent: true)) return;
        var folder = Path.GetFullPath(f.Folder);
        Append($"Installing {GameLabel()} ({f.Branch} branch) into {folder}. The first time downloads the whole game.", LogKind.Note);
        await RunSteamJobAsync("The test install", async ct =>
        {
            var last = -10.0;
            var (ok, msg) = await TestInstall.InstallGameAsync(_s.EffectiveSteamCmd, _s.Account, appId, folder, f.Branch, f.Password, Log,
                (state, pct) => { if (pct >= last + 10 || pct >= 100) { last = pct; Log($"{state} {pct:0}%"); } }, ct);
            Append(msg, ok ? LogKind.Ok : LogKind.Error);
            if (!ok) return;
            if (f.AddMod && m != null)
            {
                var packager = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account);
                var problems = await TestInstall.AddModsAsync(packager, _s.EffectiveSteamCmd, _s.Account, _cfg, folder, new[] { m }, Log, ct);
                foreach (var p in problems) Append(p, LogKind.Warn);
            }
            Append($"The test install is in {folder}. Start the game's program there; turn the mods on in the game's mod list.", LogKind.Ok);
            OpenPath(folder);
        });
    }

    /// <summary>Writes the GitHub Actions workflow into the mods folder and says what else it needs.</summary>
    void SetUpGitHubActions()
    {
        if (_busy || _s.RepoPath.Length == 0) { Append("Pick the mods folder first.", LogKind.Warn); return; }
        var path = Path.Combine(_s.RepoPath, ProjectConfig.Norm(GitHubActions.WorkflowPath));
        if (File.Exists(path) && MessageBox.Show(this, $"{GitHubActions.WorkflowPath} already exists. Replace it with a fresh copy?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            GitHubActions.Write(_s.RepoPath, overwrite: true);
            Append("Wrote " + path, LogKind.Ok);
            MessageBox.Show(this, $"Wrote {GitHubActions.WorkflowPath}.\n\n{GitHubActions.NextSteps}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e) { Append("Could not write the workflow: " + e.Message, LogKind.Error); }
    }

    /// <summary>Puts SteamCMD's saved login on the clipboard, for the workflow's secret, after a warning.</summary>
    void CopyLoginSecret()
    {
        var secret = GitHubActions.LoginSecret(_s.EffectiveSteamCmd);
        if (secret == null)
        {
            MessageBox.Show(this, "SteamCMD has no saved login yet. Log in once (Log in... above), then try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"This copies SteamCMD's saved login to the clipboard, for the {GitHubActions.LoginSecretName} secret on GitHub.\n\n" +
                "Anyone who gets this text can use your Steam login until it expires. Paste it only into that secret, then copy something else to clear the clipboard.\n\nCopy it?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Clipboard.SetText(secret);
        Append($"Copied SteamCMD's saved login for the {GitHubActions.LoginSecretName} secret. Clear the clipboard once it is pasted.", LogKind.Note);
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
            if (manual) MessageBox.Show(this, $"No newer version found. You have Modwright {UpdateCheck.CurrentVersion} (or GitHub could not be reached).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!manual && r.Version == _s.SkippedUpdate) return;
        Append($"Modwright {r.Version} is available (you have {UpdateCheck.CurrentVersion}): {r.PageUrl}", LogKind.Note);

        var cannot = AppUpdates.CannotUpdateReason(r);
        if (!manual && _s.AutoUpdate && cannot == null)
        {
            try
            {
                _pendingUpdate = await AppUpdates.DownloadAsync(r, null, CancellationToken.None);
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
            Heading = $"Modwright {r.Version} is available",
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
            Heading = $"Downloading Modwright {r.Version}...",
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
            Heading = $"Modwright {r.Version} is ready",
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
                var prepared = await AppUpdates.DownloadAsync(r, p, cts.Token);
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
        _updateReady.Text = $"Modwright {_pendingUpdate.Version} is downloaded and is installed when you close the app. Restart now";
        _updateReady.LinkArea = new LinkArea(_updateReady.Text.Length - "Restart now".Length, "Restart now".Length);
        _updateReady.Visible = true;
        Append($"Modwright {_pendingUpdate.Version} is downloaded; it is installed when you close the app (or Options > About > Restart now).", LogKind.Note);
    }

    /// <summary>Installs the downloaded update and starts the new version, unless an upload is running.</summary>
    void RestartToUpdate()
    {
        if (_pendingUpdate == null) return;
        if (_busy) { MessageBox.Show(this, "An upload is running. The update is installed when you close the app, or restart once the upload is done.", Text); return; }
        if (InstallPendingUpdate(restart: true)) Close();
    }

    /// <summary>
    /// Hands <see cref="_pendingUpdate"/> to the program that installs it over this one's files once this
    /// window has closed (and starts it when <paramref name="restart"/> is true). Returns false (and says why) when it
    /// could not.
    /// </summary>
    bool InstallPendingUpdate(bool restart)
    {
        var u = _pendingUpdate;
        if (u == null) return false;
        try
        {
            u.Install(UpdateCheck.CurrentVersion, restart);
            _pendingUpdate = null;
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

    /// <param name="only">Upload these mods instead of the ticked ones.</param>
    /// <param name="rollback">Re-upload a past version: the commit to package and the note to send.</param>
    /// <summary>True from pressing Upload until the upload and its follow-ups are done, so nothing refreshes by itself meanwhile.</summary>
    bool _inUpload;

    async Task GoAsync(List<ModInfo>? only = null, (string Ref, string Version, string Note)? rollback = null)
    {
        var outer = !_inUpload; // "Retry failed" at the end of an upload starts another one from inside it
        _inUpload = true;
        try { await UploadAsync(only, rollback); }
        finally { if (outer) _inUpload = false; }
    }

    async Task UploadAsync(List<ModInfo>? only, (string Ref, string Version, string Note)? rollback)
    {
        SaveFields();
        var picked = only ?? Selected().ToList();
        if (picked.Count == 0) return;
        if (!_cfg.AppIdValid) { Nudge(_appId, _projectNote, "Enter the game's Steam app ID first (the number in its store page URL)."); return; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { Nudge(_install, _accountNote, "SteamCMD is not installed yet. Press Install SteamCMD below."); return; }

        var dry = _dry.Checked;
        var test = _testItems.Checked && rollback == null;
        // Test items keep their own page, and a roll back sends the old version as it was.
        if (!test && rollback == null && _s.DescriptionSync != DescriptionSync.Never && Directory.Exists(_s.RepoPath)) await OfferDescriptionsAsync(picked);
        var gitPackaging = _cfg.UseGitArchive && RepoIsGit;
        var gitRef = rollback?.Ref ?? (_s.RefFor(_s.RepoPath) is { Length: > 0 } fromRef ? fromRef : null);
        if (gitRef != null && gitPackaging && !GitOps.RefExists(_s.RepoPath, gitRef))
        {
            ShowOptions(_packageRef);
            MessageBox.Show(this, $"\"{gitRef}\" is not a branch, tag or commit in this repo, so nothing can be packaged from it. Change Upload from under Options > Uploading.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Steam already has the version to upload: usually the version was not raised after the last upload.
        var sameVersion = only != null || test ? new List<ModInfo>()
            : _rows.Where(it => it.Checked && StateOf(it) == RowState.UpToDate).Select(it => (ModInfo)it.Tag!).ToList();
        var checkFirst = sameVersion.Select(m => $"Steam already has {m.Name} v{m.Version}. Raise the version first (F2) if this upload changes anything, or players can't tell the updates apart.").ToList();
        if (!test && rollback == null) checkFirst.AddRange(await DependencyUpdatesAsync(picked));
        if (gitPackaging && rollback == null && gitRef == null)
            checkFirst.AddRange(GitOps.SyncWarnings(await Task.Run(() => GitOps.Sync(_s.RepoPath)), !dry && !test && _s.GitHubReleasesFor(_s.RepoPath)));
        foreach (var m in picked)
        {
            var pageEdit = WorkshopPage.Get(m.Dir);
            var create = "NEW " + WorkshopPage.VisibilityName(pageEdit?.Visibility ?? WorkshopPage.Private).ToLowerInvariant() + " item";
            actions[m.Dir] = test
                ? (m.TestWorkshopId != null ? "update test item " + m.TestWorkshopId : "NEW private test item")
                : (m.OnSteam ? "update " + m.WorkshopId : create)
                    + (pageEdit != null && m.OnSteam ? "; also page: " + pageEdit.Summary() : "")
                    + (rollback is { } rb ? $"; roll back to v{rb.Version}" : gitRef != null && gitPackaging ? "; from " + gitRef : "")
                    + (m.Warnings.Count > 0 ? "  (" + m.Warnings[0] + ")" : "");
            notes[m.Dir] = rollback?.Note ?? Publisher.NotesFor(m, _cfg);
            if (sameVersion.Contains(m)) actions[m.Dir] = "same version as on Steam; " + actions[m.Dir];
            foreach (var bad in SteamText.Problems(notes[m.Dir])) checkFirst.Add($"{m.Name}'s change note {bad}, so its formatting will look broken on Steam.");
        }
        var heading = dry ? "Dry run for:"
            : test ? "Upload to TEST items (private copies on Steam; subscribers get nothing):"
            : $"Publish to the Steam Workshop ({(_cfg.GameName.Length > 0 ? _cfg.GameName : "app " + _cfg.AppId)}):";
        Dictionary<string, string> edited;
        bool commitFirst;
        var shaper = new Publisher(_s.RepoPath, _cfg, "", "")
        {
            Credit = _s.UploadCredit, NoteTemplate = _s.TemplateFor(_s.RepoPath), MarkdownToSteam = _s.MarkdownNotes,
        };
        if (rollback == null) checkFirst.AddRange(await LaunchPadChecksAsync(picked, test));
        var checklist = test ? null : ReleaseChecklist.Items(_s.ChecklistFor(_s.RepoPath));
        using (var confirm = new ConfirmUploadForm(heading, picked, actions, notes, dry, gitPackaging, DraftNoteFn(), checkFirst, shaper.AsSent, checklist, _s.ChecklistMustTick))
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

        if (_s.BuildFor(_s.RepoPath) is { Length: > 0 } build)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            bool built;
            try { built = await BuildStep.RunCheckedAsync(_s.RepoPath, build, l => Append(l, Display.KindOf(l)), _cts.Token); }
            catch (OperationCanceledException) { built = false; Append("Build step cancelled; nothing was uploaded.", LogKind.Warn); }
            finally { SetBusy(false); _cts.Dispose(); _cts = null; }
            if (!built) { Say("The build step failed, so nothing was uploaded."); return; }
            await RefreshAllAsync();
        }

        // Looked at before SteamCMD's login check, since that login is what takes the Steam client's place.
        var steamBack = !dry && _s.SteamBackOnline ? SteamRestart.Prepare(_s.Account) : null;
        if (!dry && !await LoginFlowAsync(trustRecent: true)) { Append("Stopped: not logged in to Steam.", LogKind.Warn); return; }

        var pub = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account)
        {
            PageEdits = m => WorkshopPage.Get(m.Dir),
            NoteOverride = m => edited.TryGetValue(m.Dir, out var n) ? n : rollback?.Note,
            GitRef = gitRef,
            TestItems = test,
            HistoryFile = UploadHistory.DefaultFile,
            ManifestDir = PackageDiffs.DefaultDir,
            UploadUnchanged = AskUploadUnchanged,
            SentDescriptions = DescriptionSync.DefaultFile,
            SteamCheck = Publisher.LiveSteamCheck,
            TagUpdater = _s.UpdateTags ? WorkshopTags.UpdateOnSteamAsync : null,
            PageExtras = _s.UpdateRequired || _s.UpdatePageExtras ? (r, ct) => WorkshopExtras.ApplyOnSteamAsync(r, ct) : null,
            SteamClientBack = steamBack,
            SetRequired = _s.UpdateRequired,
            SetGalleryAndLanguages = _s.UpdatePageExtras,
            Credit = _s.UploadCredit,
            NoteTemplate = _s.TemplateFor(_s.RepoPath),
            DescriptionFooter = _s.FooterFor(_s.RepoPath),
            MarkdownToSteam = _s.MarkdownNotes,
            VerifyUploads = _s.VerifyUploads,
            PackageArchiveDir = _s.KeepPackages ? PackageArchive.DefaultDir : null,
        };
        var history = UploadHistory.Load();
        pub.Log += l => BeginInvoke(() => Append(l, Display.KindOf(l)));
        pub.Packaged += (m, report) => BeginInvoke(() =>
        {
            _eta = dry ? null : UploadHistory.EstimateSeconds(history, report.TotalBytes);
            _currentBase += "  ·  " + PackageCheck.Size(report.TotalBytes);
            ShowClock();
        });
        pub.Stage += (t, p) => BeginInvoke(() => SetStage(t, p));

        // One login check above covers the batch; each mod then reuses Steam's saved login.
        var batch = new BatchUpload(pub) { RetryAfter = _s.AutoRetry ? TimeSpan.FromSeconds(30) : null, OneSession = _s.OneSession, Family = FamilyOf() };
        batch.Retrying += (m, why, wait) => Append($"{m.Name}: Steam was busy or could not be reached ({why}). Trying again in {wait.TotalSeconds:0} seconds...", LogKind.Warn);
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
        var logFrom = _logLines.Count;
        var started = DateTime.Now;
        Append($"{(dry ? "Dry run" : "Upload")} of {picked.Count} mod{(picked.Count == 1 ? "" : "s")} started.", LogKind.Note);
        var uploadedCommit = gitPackaging ? ModScanner.Git(_s.RepoPath, $"rev-parse {gitRef ?? "HEAD"}^{{commit}}").Trim() : "";
        string NoteSent(ModInfo m) => edited.TryGetValue(m.Dir, out var n) ? n : notes[m.Dir];
        List<BatchItem> results;
        _clock.Start();
        try { results = await batch.RunAsync(picked, !dry, _cts.Token); }
        finally
        {
            _clock.Stop();
            _cts?.Dispose(); _cts = null;
            SetBusy(false);
        }
        // Page changes went out with each successful upload, and the mod is unticked so the next press doesn't send it again.
        if (!dry && !test)
            foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created))
            {
                WorkshopPage.Clear(r.Mod.Dir);
                foreach (var it in _rows.Where(it => SameDir(it, r.Mod.Dir))) it.Checked = false;
            }
        _failed.Clear(); _retrySet.Clear();
        foreach (var r in results.Where(r => !r.Ok))
        {
            _retrySet.Add(r.Mod.Dir);
            if (r.Status == BatchStatus.Failed) _failed.Add(r.Mod.Dir);
        }
        if (!dry)
        {
            if (results.Any(r => r.Status == BatchStatus.Failed && BatchUpload.IsLoginFailure(r.Message))) NoteLogin(false);
            else if (results.Any(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created)) NoteLogin(true);
        }
        var counts = BatchUpload.Counts(results);
        var allOk = results.All(r => r.Ok);
        _currentBase = "";
        _current.Text = "Finished: " + counts;
        Say((dry ? "Dry run finished: " : "Upload finished: ") + counts);
        Append(BatchUpload.Summary(results), allOk ? LogKind.Ok : LogKind.Warn);
        Taskbar.Progress(this, 100, allOk ? Taskbar.State.None : Taskbar.State.Error);
        Taskbar.FlashIfInBackground(this);
        if (_s.NotifyWhenDone)
            Notify.IfInBackground(this, (dry ? "Dry run finished" : test ? "Test upload finished" : "Upload finished") + (allOk ? "" : " with problems"),
                counts + (picked.Count <= 3 ? ": " + string.Join(", ", picked.Select(m => m.Name)) : ""), allOk);
        var tagged = !dry && !test ? AfterUploadGit(results, uploadedCommit) : new List<string>();
        if (_s.PushTags && tagged.Count > 0)
        {
            Append($"Pushing {string.Join(", ", tagged)}...", LogKind.Note);
            var pushError = await Task.Run(() => GitOps.PushTags(_s.RepoPath, tagged));
            Append(pushError == null ? "Pushed the tags." : "Could not push the tags: " + pushError, pushError == null ? LogKind.Ok : LogKind.Warn);
        }
        if (!dry && !test) await AnnounceAsync(results, NoteSent, uploadedCommit);
        if (!dry && !test && _s.AfterFor(_s.RepoPath) is { Length: > 0 } after)
            await AfterUpload.RunAsync(_s.RepoPath, after, results, _cfg.AppId, uploadedCommit, NoteSent, l => BeginInvoke(() => Append(l, Display.KindOf(l))), CancellationToken.None);
        if (!dry && !test && _s.ThunderstoreAfterUpload(_s.RepoPath))
        {
            var sent = results.Where(r => r.Status == BatchStatus.Uploaded && Thunderstore.Has(r.Mod.Dir)).Select(r => r.Mod).ToList();
            if (sent.Count > 0) await PublishThunderstoreAsync(sent);
        }
        SaveRunLog(logFrom, started, RunLogs.Label(dry ? "dry run" : test ? "test upload" : "upload", picked.Select(m => m.Name).ToList()));
        await RefreshAllAsync();
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

    /// <summary>Asks whether to upload a package that is identical to the mod's last upload (true = upload anyway).</summary>
    bool AskUploadUnchanged(ModInfo m, PackageDiff same)
    {
        if (InvokeRequired) return (bool)Invoke(() => AskUploadUnchanged(m, same));
        var upload = new TaskDialogButton("Upload anyway");
        var skip = new TaskDialogButton("Skip it");
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"Nothing in {m.Name} changed",
            Text = $"Every file in the package is the same as in {same.LastUpload}. Uploading it again only posts the change note to subscribers.",
            Icon = TaskDialogIcon.Information,
            Buttons = { skip, upload },
            DefaultButton = skip,
        };
        return TaskDialog.ShowDialog(this, page) == upload;
    }

    /// <summary>
    /// For each picked mod already on Steam, the Workshop items it needs that Steam updated after the mod's own last
    /// update (Steam's time, else this PC's last upload). Offline, it is simply empty.
    /// </summary>
    async Task<List<string>> DependencyUpdatesAsync(List<ModInfo> picked)
    {
        try
        {
            var cfg = _cfg;
            var onSteam = picked.Where(m => m.OnSteam).ToList();
            var deps = await Task.Run(() => onSteam.ToDictionary(m => m, m => Dependencies.Find(m, cfg)));
            if (deps.Values.All(d => d.Count == 0)) return new();
            var live = await WorkshopStatus.FetchAsync(onSteam.Select(m => m.WorkshopId!).Concat(deps.Values.SelectMany(d => d)));
            var latest = UploadHistory.Latest(UploadHistory.Load());
            var lines = new List<string>();
            foreach (var (m, ids) in deps)
            {
                var since = live.TryGetValue(m.WorkshopId!, out var own) && own.Updated != null ? own.Updated
                    : latest.TryGetValue(m.WorkshopId!, out var last) ? last.Time : (DateTimeOffset?)null;
                var updated = Dependencies.UpdatedSince(m.Name, ids, since, live, DateTimeOffset.Now);
                lines.AddRange(updated);
                // The Launch Pad's clean-colony test is the quick way to see whether the update broke anything.
                if (updated.Count > 0 && RimWorldLint.Applies(cfg))
                    lines.Add($"To see whether {m.Name} still works with that, right-click it > RimWorld > Test in a clean colony.");
            }
            return lines;
        }
        catch (Exception e) when (e is not OperationCanceledException) { return new(); }
    }

    /// <summary>
    /// Finds the picked mods whose description file differs from their Workshop page and, when the user agrees (or
    /// always, per Options), puts the file's text into their page changes so it goes out with this upload.
    /// </summary>
    async Task OfferDescriptionsAsync(List<ModInfo> picked)
    {
        List<(ModInfo Mod, string Text)> changed;
        UseWaitCursor = true;
        try { changed = await DescriptionSync.FindChangedAsync(picked, _cfg, DescriptionSync.FetchFromSteam, null, CancellationToken.None, footer: _s.FooterFor(_s.RepoPath)); }
        catch (Exception e) { Append("Could not compare the descriptions with Steam: " + e.Message, LogKind.Warn); return; }
        finally { UseWaitCursor = false; }
        if (changed.Count == 0) return;
        var names = string.Join(", ", changed.Select(c => c.Mod.Name));
        if (_s.DescriptionSync != DescriptionSync.Always)
        {
            var send = new TaskDialogButton("Send it");
            var page = new TaskDialogPage
            {
                Caption = Text,
                Heading = changed.Count == 1 ? $"{names}'s description changed" : $"{changed.Count} descriptions changed",
                Text = $"{_cfg.SteamDescriptionFile} of {names} differs from the Workshop page. Send the file's text as the new description with this upload?",
                Icon = TaskDialogIcon.Information,
                Buttons = { send, new TaskDialogButton("Not this time") },
                DefaultButton = send,
                Verification = new TaskDialogVerificationCheckBox("Always send changed descriptions without asking"),
            };
            if (TaskDialog.ShowDialog(this, page) != send) return;
            if (page.Verification.Checked) { _s.DescriptionSync = DescriptionSync.Always; _descSync.SelectedIndex = 1; _s.Save(); }
        }
        try { DescriptionSync.Stage(changed); }
        catch (Exception e) { Append("Could not save the new descriptions: " + e.Message, LogKind.Error); return; }
        Append($"The description of {names} goes out with this upload ({_cfg.SteamDescriptionFile} changed).", LogKind.Note);
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
        if (string.IsNullOrWhiteSpace(_s.Account)) { Nudge(_account, _accountNote, "Type your Steam account name first."); return false; }
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
    List<string> AfterUploadGit(List<BatchItem> results, string uploaded)
    {
        var tagged = new List<string>();
        if (!RepoIsGit) return tagged;
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

        if (!_s.TagUploads) return tagged;
        if (!_cfg.UseGitArchive) { Append("Not tagging: this layout copies the working folder, so no commit matches what was uploaded.", LogKind.Note); return tagged; }
        foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created))
        {
            var tag = GitOps.TagName(r.Mod.Folder, r.Version);
            var result = GitOps.Tag(_s.RepoPath, tag, $"{r.Mod.Name} {r.Version} uploaded to Steam Workshop item {r.WorkshopId}", uploaded.Length > 0 ? uploaded : "HEAD");
            if (result == null) { Append($"Tagged {tag}.", LogKind.Ok); tagged.Add(tag); }
            else if (result == "exists") { Append($"Tag {tag} already exists; left as it is.", LogKind.Note); tagged.Add(tag); }
            else Append($"Could not tag {tag}: {result}", LogKind.Warn);
        }
        return tagged;
    }

    // ---------------------------------------------------------------- profiles and announcements

    const string NoProfile = "(none)";

    void ShowProfiles()
    {
        _populating = true;
        _profile.BeginUpdate();
        _profile.Items.Clear();
        _profile.Items.Add(NoProfile);
        foreach (var p in _s.Profiles) _profile.Items.Add(p.Name);
        var current = _s.CurrentProfile();
        _profile.SelectedIndex = current == null ? 0 : _profile.Items.IndexOf(current.Name);
        _profile.EndUpdate();
        _profileDelete.Enabled = current != null && !_busy;
        _populating = false;
    }

    void SwitchProfile()
    {
        var p = _s.Profiles.FirstOrDefault(x => x.Name == _profile.SelectedItem as string);
        if (p == null || _busy) { ShowProfiles(); return; }
        _s.UseProfile(p);
        _s.Save();
        _account.Text = _s.Account;
        FillRecent();
        _failed.Clear(); _retrySet.Clear(); _progress.Visible = false; _filter.Text = "";
        Append($"Switched to the profile \"{p.Name}\": {(p.RepoPath.Length > 0 ? p.RepoPath : "no mods folder")}, account {(p.Account.Length > 0 ? p.Account : "(none)")}.", LogKind.Note);
        RefreshAll();
        UpdateLoginHint();
        if (SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd) && SteamLogin.LooksCached(_s.EffectiveSteamCmd, _s.Account)) _ = VerifyLoginAsync(false);
    }

    void SaveProfile()
    {
        SaveFields();
        var suggestion = _s.CurrentProfile()?.Name ?? (_s.RepoPath.Length > 0 ? Path.GetFileName(_s.RepoPath) : _s.Account);
        var name = Prompt("Save the current mods folder, Steam account and SteamCMD as a profile named:", suggestion)?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        _s.SaveProfile(name);
        _s.Save();
        ShowProfiles();
        Append($"Saved the profile \"{name}\".", LogKind.Ok);
    }

    void DeleteProfile()
    {
        var p = _s.CurrentProfile();
        if (p == null) return;
        if (MessageBox.Show(this, $"Delete the profile \"{p.Name}\"? The folder and account stay as they are.", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        _s.Profiles.Remove(p);
        _s.Save();
        ShowProfiles();
    }

    void ShowAnnounce()
    {
        _populating = true;
        var hasRepo = Directory.Exists(_s.RepoPath);
        _webhook.Text = hasRepo ? _s.WebhookFor(_s.RepoPath) : "";
        _noteTemplate.Text = hasRepo ? _s.TemplateFor(_s.RepoPath).Replace("\n", "\r\n") : "";
        _noteTemplate.Enabled = hasRepo && !_busy;
        _descFooter.Text = hasRepo ? _s.FooterFor(_s.RepoPath).Replace("\n", "\r\n") : "";
        _descFooter.Enabled = hasRepo && !_busy;
        _checklist.Text = hasRepo ? _s.ChecklistFor(_s.RepoPath).Replace("\n", "\r\n") : "";
        _checklist.Enabled = hasRepo && !_busy;
        _buildCmd.Text = hasRepo ? _s.BuildFor(_s.RepoPath) : "";
        _packageRef.Text = hasRepo ? _s.RefFor(_s.RepoPath) : "";
        _afterCmd.Text = hasRepo ? _s.AfterFor(_s.RepoPath) : "";
        _buildCmd.Enabled = _packageRef.Enabled = _afterCmd.Enabled = hasRepo && !_busy;
        _ghReleases.Checked = hasRepo && _s.GitHubReleasesFor(_s.RepoPath);
        _tsTeam.Text = hasRepo ? _s.ThunderstoreTeamFor(_s.RepoPath) : "";
        _tsCommunity.Text = hasRepo ? _s.ThunderstoreCommunityFor(_s.RepoPath) : "";
        _tsCategories.Text = hasRepo ? _s.ThunderstoreCategoriesFor(_s.RepoPath) : "";
        _tsAfter.Checked = hasRepo && _s.ThunderstoreAfterUpload(_s.RepoPath);
        _tsToken.Text = ThunderstoreToken.Unprotect(_s.ThunderstoreToken);
        _tsTeam.Enabled = _tsCommunity.Enabled = _tsCategories.Enabled = _tsAfter.Enabled = hasRepo && !_busy;
        _webhook.Enabled = _ghReleases.Enabled = _webhookTest.Enabled = hasRepo && !_busy;
        _populating = false;
    }

    void SaveAnnounce()
    {
        if (_populating || !Directory.Exists(_s.RepoPath)) return;
        var url = _webhook.Text.Trim();
        if (url.Length > 0 && !Announce.IsDiscordWebhook(url))
        {
            MessageBox.Show(this, "That is not a Discord webhook link. In Discord: channel settings > Integrations > Webhooks > Copy Webhook URL.", Text);
            _webhook.Focus();
            return;
        }
        _s.SetWebhook(_s.RepoPath, url);
        _s.SetGitHubReleases(_s.RepoPath, _ghReleases.Checked);
        _s.Save();
    }

    async Task TestWebhookAsync()
    {
        SaveAnnounce();
        var url = _s.WebhookFor(_s.RepoPath);
        if (url.Length == 0) { MessageBox.Show(this, "Paste the channel's webhook link first.", Text); _webhook.Focus(); return; }
        _webhookTest.Enabled = false;
        var error = await Announce.PostDiscordAsync(url, Announce.DiscordPayload("Modwright", "test", "0",
            "This is a test from Modwright. Uploads from this mods folder will be announced here.", _cfg.GameName));
        _webhookTest.Enabled = true;
        Append(error == null ? "Discord test message sent." : "Discord test failed: " + error, error == null ? LogKind.Ok : LogKind.Error);
    }

    /// <summary>Posts successful updates to Discord and makes GitHub releases for them, when those are turned on for this folder.</summary>
    async Task AnnounceAsync(List<BatchItem> results, Func<ModInfo, string> noteOf, string commit)
    {
        var done = results.Where(r => r.Status == BatchStatus.Uploaded && r.WorkshopId != null).ToList();
        if (done.Count == 0) return;
        var hook = _s.WebhookFor(_s.RepoPath);
        if (hook.Length > 0)
            foreach (var r in done)
            {
                var error = await Announce.PostDiscordAsync(hook, Announce.DiscordPayload(r.Mod.Name, r.Version, r.WorkshopId!, noteOf(r.Mod), _cfg.GameName));
                Append(error == null ? $"Announced {r.Mod.Name} {r.Version} on Discord." : $"Could not announce {r.Mod.Name} on Discord: {error}", error == null ? LogKind.Ok : LogKind.Warn);
            }
        if (!_s.GitHubReleasesFor(_s.RepoPath)) return;
        if (commit.Length == 0) { Append("No GitHub release: this layout does not upload a git commit.", LogKind.Note); return; }
        foreach (var r in done)
        {
            var tag = GitOps.TagName(r.Mod.Folder, r.Version);
            var title = $"{r.Mod.Name} {r.Version}";
            var note = noteOf(r.Mod) + $"\n\nSteam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id={r.WorkshopId}";
            try
            {
                var link = await Task.Run(() => Announce.CreateGitHubRelease(_s.RepoPath, tag, title, note, commit));
                Append($"GitHub release {tag}: {link}", LogKind.Ok);
            }
            catch (Exception e) { Append($"Could not make the GitHub release {tag}: {e.Message}", LogKind.Warn); }
        }
    }

    void SaveThunderstore()
    {
        if (_populating) return;
        if (Directory.Exists(_s.RepoPath))
            _s.SetThunderstore(_s.RepoPath, _tsTeam.Text, _tsCommunity.Text, _tsCategories.Text, _tsAfter.Checked);
        _s.ThunderstoreToken = ThunderstoreToken.Protect(_tsToken.Text.Trim());
        _s.Save();
    }

    /// <summary>"Later...": schedules the ticked mods to upload at a time picked in the window, or lists the scheduled uploads.</summary>
    void UploadLater()
    {
        var ticked = Selected().Select(m => m.Folder).ToList();
        var extra = new List<string>();
        if (_allowCreate.Checked) extra.Add("--allow-create");
        using var f = new UploadLaterForm(_s.RepoPath, ticked, ScheduledUploads.UploadArgs(_s.RepoPath, ticked, extra: extra));
        f.ShowDialog(this);
    }

    /// <summary>The ticked mods when more than one is ticked, else the selected one.</summary>
    List<ModInfo> TickedOrSelected()
    {
        var ticked = Selected().ToList();
        return ticked.Count > 1 ? ticked : SelectedMod() is { } m ? new() { m } : ticked;
    }

    /// <summary>
    /// Publishes mods to Thunderstore: each is packaged as for Steam (nothing goes to Steam), checked against what
    /// Thunderstore accepts, zipped with the version to upload in its manifest, and sent unless Thunderstore already has that version.
    /// </summary>
    async Task PublishThunderstoreAsync(List<ModInfo> mods)
    {
        if (mods.Count == 0) return;
        SaveThunderstore();
        var target = _s.ThunderstoreFor(_s.RepoPath);
        var token = ThunderstoreToken.Unprotect(_s.ThunderstoreToken);
        if (target == null || token.Length == 0)
        {
            Append("Thunderstore: set the team, community and token in Options > Uploading > Thunderstore first.", LogKind.Warn);
            ShowOptions(target == null ? _tsTeam : _tsToken);
            return;
        }
        var wasBusy = _busy;
        _cts ??= new CancellationTokenSource();
        var ct = _cts.Token;
        if (!wasBusy) SetBusy(true);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamCmdHelper");
        try
        {
            foreach (var m in mods)
            {
                var zip = Path.Combine(Path.GetTempPath(), $"steamcmd_helper_thunderstore_{Guid.NewGuid():N}.zip");
                string version = "", name = Thunderstore.NameOf(m.Dir) ?? m.Folder;
                List<string>? problems = null;
                var pub = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account) { GitRef = _s.RefFor(_s.RepoPath) is { Length: > 0 } r ? r : null };
                pub.StageReady += (_, stage, v) =>
                {
                    try
                    {
                        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(stage, Thunderstore.ManifestFile)))!;
                        version = Thunderstore.VersionFor(v, manifest);
                        problems = Thunderstore.Check(stage, version);
                        if (problems.Count == 0) Thunderstore.Zip(stage, zip, version);
                    }
                    catch (Exception e) { problems = new() { e.Message }; }
                };
                Append($"{m.Name}: packaging for Thunderstore...", LogKind.Note);
                var res = await Task.Run(() => pub.RunAsync(m, false, ct));
                try
                {
                    if (problems == null) { Append($"{m.Name}: not published to Thunderstore, the package could not be built: {res.Message}", LogKind.Error); continue; }
                    if (problems.Count > 0) { Append($"{m.Name}: not published to Thunderstore: the package {string.Join("; ", problems)}.", LogKind.Error); continue; }
                    if (await Thunderstore.LatestAsync(http, target.Team, name, ct) == version)
                    {
                        Append($"{m.Name}: Thunderstore already has {version}.", LogKind.Note);
                        continue;
                    }
                    var error = await Thunderstore.PublishAsync(http, token, target, zip, ct, line => Append($"{m.Name}: {line}", LogKind.Note));
                    Append(error == null
                        ? $"{m.Name}: published {version} to Thunderstore: {Thunderstore.PackageUrl(target.Community, target.Team, name)}"
                        : $"{m.Name}: not published to Thunderstore: {error}", error == null ? LogKind.Ok : LogKind.Error);
                }
                finally { try { File.Delete(zip); } catch { } }
            }
        }
        catch (OperationCanceledException) { Append("Cancelled.", LogKind.Warn); }
        catch (HttpRequestException e) { Append("Could not reach Thunderstore: " + e.Message, LogKind.Error); }
        finally
        {
            if (!wasBusy) { SetBusy(false); _cts?.Dispose(); _cts = null; }
        }
    }

    /// <summary>Packages one mod without uploading and shows what the package holds.</summary>
    async Task PreviewPackageAsync(ModInfo m)
    {
        if (_busy) return;
        PackageReport? report = null;
        var pub = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account) { ManifestDir = PackageDiffs.DefaultDir };
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
