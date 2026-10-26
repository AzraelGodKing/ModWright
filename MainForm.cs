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
            if (!NoStartupPrompts) _ = Task.Run(() => { try { SettingsBackups.MakeIfDue(AppSettings.Folder, SettingsBackups.DefaultDir); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { } });
            if (!HasUploader) { } // SteamCMD is only needed by the mod manager to download missing mods, and it asks then
            else if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { if (!NoStartupPrompts) await OfferInstallAsync(); }
            else _ = StartupSteamAsync(SteamLogin.LooksCached(_s.EffectiveSteamCmd, _s.Account));
            if (_s.CheckForUpdates && !NoStartupPrompts) await CheckForUpdateAsync(false);
        };
        Activated += (_, _) => { if (!_busy) Taskbar.Progress(this, 0, Taskbar.State.None); };
        Activated += async (_, _) => await RefreshIfChangedAsync();
        FormClosing += (_, e) =>
        {
            // Closing used to drop unsaved Drop Pod edits without a word.
            if (e.CloseReason == CloseReason.UserClosing && !OfferSaveDropPod("The Drop Pod's mod list has changes that are not saved to the game. Save them before closing?"))
            { e.Cancel = true; return; }
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
}
