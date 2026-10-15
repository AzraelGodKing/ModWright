using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SteamCmdHelper;

public sealed class MainForm : Form
{
    readonly AppSettings _s = AppSettings.Load();
    ProjectConfig _cfg = new();

    readonly TextBox _repo = new() { Tag = "fill", ReadOnly = true, TabStop = false, Cursor = Cursors.Hand };
    readonly TextBox _account = new() { Width = 220 };
    readonly Label _branch = new Label { AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(240, 0) }.Role(ThemeRole.Muted);
    readonly TextBox _appId = new() { Width = 90 };
    readonly ComboBox _preset = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _game = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill" }.Role(ThemeRole.Keep);
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    readonly ToolTip _tip = new();
    readonly Label _login = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill" }.Role(ThemeRole.Keep);
    readonly LinkLabel _loginLink = new LinkLabel { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill", LinkBehavior = LinkBehavior.HoverUnderline }.Role(ThemeRole.Keep);
    readonly Label _steamCmdPath = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "fill" }.Role(ThemeRole.Muted);
    readonly RadioButton _themeDark = new() { AutoSize = true, Text = "Dark" };
    readonly RadioButton _themeLight = new() { AutoSize = true, Text = "Light" };
    readonly CheckBox _checkUpdates = new() { AutoSize = true, Text = "Check for a newer version at start-up" };
    readonly ListView _list = new();
    readonly FlatProgressBar _modBar = new() { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 6) };
    readonly FlatProgressBar _overall = new() { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 2) };
    readonly Label _current = new() { AutoSize = true, Text = "Idle", Tag = "fill" };
    readonly Label _stage = new Label { AutoSize = true }.Role(ThemeRole.Muted);
    readonly CheckBox _dry = new() { AutoSize = true, Text = "Dry run (check everything, upload nothing)" };
    readonly CheckBox _allowCreate = new() { AutoSize = true, Text = "Allow creating new items (private) for mods not on Steam yet" };
    readonly RichTextBox _log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None }.Role(ThemeRole.Log);

    Button _help = null!, _checkUpdateNow = null!, _editPage = null!, _browse = null!, _refresh = null!, _all = null!, _none = null!, _go = null!, _cancel = null!, _checkLogin = null!, _doLogin = null!, _install = null!, _editLayout = null!;
    NavButton _navUpload = null!, _navOptions = null!;
    Control _uploadPage = null!, _optionsPage = null!;
    Tone _loginTone = Tone.Muted;

    List<ModInfo> _mods = new();
    CancellationTokenSource? _cts;
    bool _busy, _populating;
    int _idx, _count;

    /// <summary>Skips the start-up "install SteamCMD?" question (for --shot).</summary>
    public bool NoStartupPrompts { get; init; }

    public const string Disclaimer = "Not affiliated with or endorsed by Valve Corporation or Steam.";

    /// <param name="theme">Overrides the saved theme without saving it (used by --shot).</param>
    public MainForm(string? theme = null)
    {
        Theme.Set(theme ?? _s.Theme);
        BuildUi();
        _repo.Text = _s.RepoPath;
        _account.Text = _s.Account;
        _allowCreate.Checked = _s.AllowCreate;
        Shown += async (_, _) =>
        {
            RefreshAll();
            if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { if (!NoStartupPrompts) await OfferInstallAsync(); }
            else if (SteamLogin.LooksCached(_s.EffectiveSteamCmd, _s.Account)) _ = VerifyLoginAsync(false);
            if (_s.CheckForUpdates && !NoStartupPrompts) await CheckForUpdateAsync(false);
        };
        FormClosing += (_, e) =>
        {
            if (!_busy) return;
            if (MessageBox.Show(this, "An upload is running. Stop it and close?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            Cancel();
        };
    }

    // ---------------------------------------------------------------- UI

    void BuildUi()
    {
        Text = "SteamCMD Helper - Workshop uploads";
        var screen = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1000, 800);
        Width = Math.Min(1000, screen.Width); Height = Math.Min(800, screen.Height); MinimumSize = new Size(840, 640);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
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
        _cancel.Enabled = false;

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

        _repo.DoubleClick += (_, _) => { if (!_busy) BrowseRepo(); };
        _account.Leave += (_, _) => { SaveFields(); UpdateLoginHint(); };
        _allowCreate.CheckedChanged += (_, _) => { if (!_populating) { SaveFields(); RefreshAll(); } };
        _appId.Leave += async (_, _) => await ApplyAppIdAsync();
        _appId.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyAppIdAsync(); } };
        _preset.SelectionChangeCommitted += (_, _) => ApplyPresetChoice();
        _themeDark.CheckedChanged += (_, _) => { if (_themeDark.Checked) SetTheme(Theme.DarkName); };
        _themeLight.CheckedChanged += (_, _) => { if (_themeLight.Checked) SetTheme(Theme.LightName); };
        _loginLink.LinkClicked += (_, _) => ShowPage(_optionsPage);
        _checkUpdates.CheckedChanged += (_, _) => { if (!_populating) { _s.CheckForUpdates = _checkUpdates.Checked; _s.Save(); } };
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
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14, 12, 14, 12) };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); // the list and the log share what is left, so neither vanishes on small screens

        _preset.Items.AddRange(ProjectConfig.PresetNames.Cast<object>().Append("Custom").ToArray());
        page.Controls.Add(Card("Project", null,
            Row(Lbl("Mods repo"), _repo, _browse, _branch),
            Row(Lbl("Steam app ID"), _appId, Lbl("Layout"), _preset, _editLayout, _game)), 0, 0);

        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Dock = DockStyle.Fill;
        _list.ShowItemToolTips = true;
        _list.Columns.Add("Mod", 200);
        _list.Columns.Add("Uploaded version", 135);
        _list.Columns.Add("Version to upload", 135);
        _list.Columns.Add("Workshop ID", 105);
        _list.Columns.Add("Preview", 65, HorizontalAlignment.Center);
        _list.Columns.Add("Status", 190);
        _list.ItemCheck += OnItemCheck;
        _list.ItemChecked += (_, _) => UpdateGoButton();
        _list.MouseDoubleClick += OnListDoubleClick;
        _list.Resize += (_, _) => StretchLastColumn();
        _list.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex < _list.Columns.Count - 1) StretchLastColumn(); };
        _tip.SetToolTip(_editPage, "Change the selected mod's title, description or visibility on Steam with its next upload");
        var menu = new ContextMenuStrip();
        menu.Items.Add("Edit Workshop page...", null, (_, _) => EditWorkshopPage(SelectedMod()));
        menu.Opening += (_, e) => e.Cancel = _busy || SelectedMod() == null;
        _list.ContextMenuStrip = menu;
        var mods = Card("Mods", new Control[] { _editPage, _all, _none, _refresh }, _list);
        mods.AutoSize = false; mods.Dock = DockStyle.Fill;
        page.Controls.Add(mods, 0, 1);

        var progress = Card(null, null,
            Row(_current, _stage),
            _modBar,
            Lbl("Overall").Role(ThemeRole.Muted),
            _overall,
            Row(_allowCreate, _dry));
        page.Controls.Add(progress, 0, 2);

        _tip.SetToolTip(_loginLink, "Steam login settings are on the Options tab");
        _tip.SetToolTip(_all, "Tick every mod that is ready to upload, then upload them in one go");
        var actions = Row(_loginLink, _cancel, _go);
        actions.Margin = new Padding(0, 2, 0, 8);
        page.Controls.Add(actions, 0, 3);

        var log = Card(null, null, _log);
        log.AutoSize = false; log.Dock = DockStyle.Fill;
        log.Padding = new Padding(1); // a thin frame; the log has its own console background
        log.Margin = Padding.Empty;
        page.Controls.Add(log, 0, 4);
        return page;
    }

    Control BuildOptionsPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14, 12, 14, 12), AutoScroll = true };
        for (var i = 0; i < 3; i++) page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _themeDark.Checked = Theme.Current.Dark;
        _themeLight.Checked = !Theme.Current.Dark;
        page.Controls.Add(Card("Appearance", null,
            Row(Lbl("Theme"), _themeDark, _themeLight),
            Hint("Saved for your Windows user and applied straight away.")), 0, 0);

        page.Controls.Add(Card("Steam account", null,
            Row(Lbl("Account name"), _account, _checkLogin, _doLogin),
            Row(Lbl("Status"), _login),
            Row(Lbl("SteamCMD"), _steamCmdPath, _install),
            Hint("You log in once in SteamCMD's own window and Steam keeps the login. This program never sees your password.")), 0, 1);

        var version = (typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];
        _checkUpdates.Checked = _s.CheckForUpdates;
        _checkUpdateNow = Btn("Check now", async (_, _) => await CheckForUpdateAsync(true));
        page.Controls.Add(Card("About", null,
            Hint($"SteamCMD Helper {version}. Settings are stored in %LocalAppData%\\SteamCmdHelper\\settings.json."),
            Row(_checkUpdates, _checkUpdateNow),
            Hint(Disclaimer + " Steam and SteamCMD are trademarks of Valve Corporation.")), 0, 2);
        return page;
    }

    public void ShowOptions() => ShowPage(_optionsPage);

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
        _navUpload.Selected = _uploadPage.Visible;
        _navOptions.Selected = _optionsPage.Visible;
        foreach (ListViewItem it in _list.Items) if (it.Tag is ModInfo m) ColorRow(it, m);
        SetLogin(_login.Text, _loginTone);
        ShowGame();
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
    // ---------------------------------------------------------------- game / layout

    /// <summary>The "?" button: a layout request, bug report or feature request, sent to Linear.</summary>
    void OpenRequest()
    {
        // Show one mod's file names (never contents) so the request says what a mod looks like.
        var mod = (_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag : _list.Items.Count > 0 ? _list.Items[0].Tag : null) as ModInfo;
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
        if (id.Length > 0 && !id.All(char.IsDigit)) { Append("The app ID is a number, e.g. 294100 for RimWorld (it is in the game's Steam store URL).", Color.Orange); return; }
        if (!Directory.Exists(_s.RepoPath)) { MessageBox.Show(this, "Pick the mods repo folder first.", Text); _appId.Text = _cfg.AppId; return; }

        _cfg.AppId = id;
        _cfg.GameName = "";
        if (id.Length > 0) _cfg.GameName = await LookupGameNameAsync(id) ?? "";
        SaveLayout(id.Length > 0 ? $"App ID set to {id}{(_cfg.GameName.Length > 0 ? " (" + _cfg.GameName + ")" : "")}." : "App ID cleared.");
        ShowGame();
        UpdateGoButton();
    }

    void ApplyPresetChoice()
    {
        var name = _preset.SelectedItem as string ?? "";
        if (!Directory.Exists(_s.RepoPath)) { MessageBox.Show(this, "Pick the mods repo folder first.", Text); ShowGame(); return; }
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
        try { _cfg.Save(_s.RepoPath); Append(what + " Saved to " + ProjectConfig.FileName + " in the repo.", Color.DeepSkyBlue); }
        catch (Exception e) { Append("Could not save " + ProjectConfig.FileName + ": " + e.Message, Color.Tomato); }
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

    // ---------------------------------------------------------------- state

    void SaveFields()
    {
        _s.RepoPath = _repo.Text.Trim();
        _s.Account = _account.Text.Trim();
        _s.AllowCreate = _allowCreate.Checked;
        _s.Save();
    }

    void BrowseRepo()
    {
        using var d = new FolderBrowserDialog { Description = "Pick the folder that holds your mods (the repo)", SelectedPath = _repo.Text };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _repo.Text = d.SelectedPath;
        SaveFields();
        RefreshAll();
    }

    void EditLayout()
    {
        SaveFields();
        if (!Directory.Exists(_s.RepoPath)) { MessageBox.Show(this, "Pick the mods repo folder first.", Text); return; }
        if (_cfg.IsBuiltIn) { MessageBox.Show(this, "Built-in layouts are locked. Choose \"Custom\" in the Layout box to make your own.", Text); return; }
        var path = _cfg.EnsureFile(_s.RepoPath);
        Append($"Opened {path}. Save it, then press Refresh.", Color.DeepSkyBlue);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Append("Could not open the file: " + e.Message, Color.Tomato); }
    }

    void RefreshAll()
    {
        SaveFields();
        _cfg = ProjectConfig.Load(_s.RepoPath);
        var branch = ModScanner.GitBranch(_s.RepoPath);
        _branch.Text = branch == null ? "(not a git repo)" : "branch: " + branch;
        ShowGame();
        if (_cfg.Error != null) Append(_cfg.Error, Color.Orange);

        _populating = true;
        _list.Items.Clear();
        _mods = ModScanner.Scan(_s.RepoPath, _cfg);
        foreach (var m in _mods)
            if (WorkshopPage.Get(m.Dir) is { } page) m.Warnings.Add("Workshop page changes go out with the next upload: " + page.Summary());
        var uploaded = UploadedVersions.LoadRecord();
        foreach (var m in _mods)
        {
            var it = new ListViewItem(m.Name) { UseItemStyleForSubItems = false };
            it.SubItems.Add(!m.OnSteam ? "-" : uploaded.TryGetValue(m.WorkshopId!, out var up) ? up : "...");
            it.SubItems.Add(m.Version.Length > 0 ? m.Version : "?");
            it.SubItems.Add(m.WorkshopId ?? "0"); // 0 = not on Steam yet, so uploading creates it
            it.SubItems.Add(m.PreviewOk == null ? "-" : m.PreviewOk == true ? "✔" : "✖");
            it.SubItems.Add(m.StatusLabel);
            it.ToolTipText = RowTip(m);
            it.Tag = m;
            var usable = m.Ready && (m.OnSteam || _s.AllowCreate);
            it.Checked = usable && m.OnSteam;
            ColorRow(it, m);
            _list.Items.Add(it);
            if (!m.Ready) Append($"{m.Name}: fix {string.Join("; ", m.Problems)}", Color.Orange);
        }
        _populating = false;
        _ = LoadUploadedVersionsAsync(_mods.Where(m => m.OnSteam).ToList(), uploaded);

        if (_mods.Count == 0)
            Append(string.IsNullOrWhiteSpace(_cfg.ModMarker)
                ? "No mod folders found. Pick the folder that contains one sub-folder per mod, or press Edit layout file to point at them."
                : $"No mods found. Pick the folder that contains the mod folders (each needs {_cfg.ModMarker}), choose a different Layout, or press Edit layout file.", Color.Orange);

        UpdateLoginHint();
        UpdateGoButton();
        StretchLastColumn();
    }

    const int UploadedCol = 1, VersionCol = 2, PreviewCol = 4, StatusCol = 5;

    /// <summary>Row text colour: red when it needs fixing, dimmed when it can't be picked, the preview mark green or red.</summary>
    void ColorRow(ListViewItem it, ModInfo m)
    {
        var tone = !m.Ready ? Tone.Error : (!m.OnSteam && !_s.AllowCreate) ? Tone.Muted : Tone.Normal;
        for (var i = 0; i < it.SubItems.Count; i++)
        {
            var sub = it.SubItems[i];
            sub.BackColor = _list.BackColor;
            sub.ForeColor = Theme.Of(i == PreviewCol && m.PreviewOk != null ? (m.PreviewOk == true ? Tone.Ok : Tone.Error) : tone);
        }
    }

    string RowTip(ModInfo m)
    {
        var lines = new List<string>();
        lines.AddRange(m.Problems.Select(p => "Fix: " + p));
        lines.AddRange(m.Warnings.Select(w => "Note: " + w));
        if (m.PreviewNote.Length > 0) lines.Add("Preview: " + m.PreviewNote);
        lines.Add("Double-click the version to upload to change it, or the name to edit the Workshop page.");
        if (ModScanner.NeedsChangelogEntry(m, _cfg)) lines.Add("Double-click the status to write the changelog entry for " + m.Version + ".");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Fills in "Uploaded version" from each item's public Steam changelog (the newest change note starts with the
    /// version when it was sent by this app or the Add Changenote mod). Falls back to what this PC last uploaded, else "?".
    /// </summary>
    async Task LoadUploadedVersionsAsync(List<ModInfo> mods, Dictionary<string, string> uploaded)
    {
        // One at a time: Steam rate-limits its community pages.
        foreach (var m in mods)
        {
            var v = await UploadedVersions.FetchAsync(m.WorkshopId!);
            if (IsDisposed) return;
            var it = _list.Items.Cast<ListViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, m));
            if (it == null) return; // the list was refreshed meanwhile; that refresh fills it in
            it.SubItems[UploadedCol].Text = v ?? (uploaded.TryGetValue(m.WorkshopId!, out var up) ? up : "?");
        }
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
        Append($"{m.Name}: version set to {version}.", Color.DeepSkyBlue);
        // RimWorld refuses the upload until the new version has its own changelog entry, so offer to write it now.
        m.Version = version;
        if (ModScanner.NeedsChangelogEntry(m, _cfg) && !AskChangelogEntry(m, version))
            Append($"Add a {version} entry to {_cfg.Changelog} before uploading (double-click the Status to write it here).", Color.Orange);
        RefreshAll();
    }

    ModInfo? SelectedMod() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModInfo : null;

    /// <summary>Opens the Workshop page editor; its changes are kept for the mod's next upload.</summary>
    void EditWorkshopPage(ModInfo? m)
    {
        if (_busy) return;
        if (m == null) { MessageBox.Show(this, "Select a mod in the list first.", Text); return; }
        var file = Publisher.DescriptionFor(m, _cfg);
        using var f = new WorkshopPageForm(m, _cfg.SteamDescriptionFile, file.Length > 0 ? file : null);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try { WorkshopPage.Set(m.Dir, f.Result); }
        catch (Exception e) { Append("Could not save the Workshop page changes: " + e.Message, Color.Tomato); return; }
        Append(f.Result.IsEmpty
            ? $"{m.Name}: Workshop page changes cleared."
            : $"{m.Name}: Workshop page changes ({f.Result.Summary()}) will be sent with the next upload.", Color.DeepSkyBlue);
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
        Append($"{m.Name}: added the {version} entry to {_cfg.Changelog} (commit it).", Color.DeepSkyBlue);
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
        if (_list.Items[e.Index].Tag is not ModInfo m) return;
        if (!m.Ready) { e.NewValue = CheckState.Unchecked; return; }
        if (!m.OnSteam && !_allowCreate.Checked) e.NewValue = CheckState.Unchecked;
    }

    /// <summary>Ticks every mod that can be picked (the same rules as ticking by hand), or clears them all.</summary>
    void TickAll(bool on)
    {
        if (_busy) return;
        _list.BeginUpdate();
        foreach (ListViewItem it in _list.Items) it.Checked = on; // OnItemCheck turns down the ones that can't be picked
        _list.EndUpdate();
        UpdateGoButton();
    }

    IEnumerable<ModInfo> Selected() => _list.CheckedItems.Cast<ListViewItem>().Select(i => (ModInfo)i.Tag!);

    void UpdateGoButton()
    {
        var n = _list.CheckedItems.Count;
        _go.Text = n == 0 ? "Upload selected" : $"Upload {n} mod{(n == 1 ? "" : "s")}";
        _go.Enabled = !_busy && n > 0 && _cfg.AppIdValid;
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var c in new Control[] { _browse, _editLayout, _refresh, _all, _none, _checkLogin, _doLogin, _install, _repo, _account, _list, _dry, _allowCreate, _appId, _preset, _themeDark, _themeLight, _checkUpdateNow, _editPage })
            c.Enabled = !busy;
        ApplyLocks();
        _cancel.Enabled = busy;
        UpdateGoButton();
    }

    /// <summary>Shows the login state on the Options tab and, as a link to it, next to the Upload button.</summary>
    void SetLogin(string text, Tone tone)
    {
        _loginTone = tone;
        _login.Text = text;
        _login.ForeColor = Theme.Of(tone);
        _loginLink.Text = text;
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

    void Append(string line, Color? color = null)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(line, color)); return; }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = color ?? Theme.LogText;
        _log.AppendText(line + "\n");
        _log.SelectionColor = _log.ForeColor;
        _log.ScrollToCaret();
    }

    static Color LineColor(string l)
    {
        if (l.StartsWith("SKIPPED") || l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || l.Contains("FAILED", StringComparison.OrdinalIgnoreCase)) return Color.Tomato;
        if (l.StartsWith("Uploaded ") || l.StartsWith("Created ") || l.Contains("Success", StringComparison.OrdinalIgnoreCase)) return Color.LightGreen;
        if (l.StartsWith("===") || l.StartsWith("---")) return Color.DeepSkyBlue;
        return Color.Gainsboro;
    }

    // ---------------------------------------------------------------- login

    async Task<bool> VerifyLoginAsync(bool announce)
    {
        var steam = _s.EffectiveSteamCmd;
        if (!SteamLogin.SteamCmdInstalled(steam)) { UpdateLoginHint(); return false; }
        if (string.IsNullOrWhiteSpace(_s.Account)) { UpdateLoginHint(); if (announce) { ShowPage(_optionsPage); MessageBox.Show(this, "Type your Steam account name first.", Text); } return false; }

        SetLogin("Checking login...", Tone.Muted);
        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        try
        {
            var (ok, _) = await SteamLogin.VerifyAsync(steam, _s.Account, CancellationToken.None);
            if (ok) SetLogin($"Logged in as {_s.Account}", Tone.Ok);
            else SetLogin($"Not logged in as {_s.Account}", Tone.Error);
            if (announce || !ok) Append(ok ? $"Steam login OK for {_s.Account}." : $"Steam login check failed for {_s.Account} (no saved login, wrong name, or no connection).", ok ? Color.LightGreen : Color.Orange);
            return ok;
        }
        catch (Exception e) { SetLogin("Login check failed", Tone.Error); Append("Login check error: " + e.Message, Color.Tomato); return false; }
        finally { if (!wasBusy) SetBusy(false); }
    }

    /// <summary>Checks the saved login; if there isn't one, opens SteamCMD so you can log in, waits, then checks again.</summary>
    async Task<bool> LoginFlowAsync()
    {
        if (string.IsNullOrWhiteSpace(_s.Account)) { ShowPage(_optionsPage); MessageBox.Show(this, "Type your Steam account name on the Options tab first.", Text); _account.Focus(); return false; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { ShowPage(_optionsPage); MessageBox.Show(this, "SteamCMD is not installed yet. Click Install SteamCMD on the Options tab.", Text); return false; }

        if (await VerifyLoginAsync(false)) return true;

        var r = MessageBox.Show(this,
            "Steam needs you to log in once.\n\nA SteamCMD window will open. Type your password, then the Steam Guard code, and wait until the window closes by itself.\n\nYour password goes to Steam only; this program never sees it.",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (r != DialogResult.OK) return false;

        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        SetLogin("Waiting for you to finish logging in...", Tone.Muted);
        try { await SteamLogin.InteractiveLoginAsync(_s.EffectiveSteamCmd, _s.Account, CancellationToken.None); }
        catch (Exception e) { Append("Could not open SteamCMD: " + e.Message, Color.Tomato); }
        finally { if (!wasBusy) SetBusy(false); }

        return await VerifyLoginAsync(true);
    }

    /// <summary>At start-up without SteamCMD: offer the one-click install, and say plainly that uploads need it.</summary>
    async Task OfferInstallAsync()
    {
        var r = MessageBox.Show(this,
            "SteamCMD is not installed.\n\nThis program uploads through SteamCMD (Valve's command-line Steam client) and won't work without it.\n\n" +
            $"Install it now? It is downloaded from Valve to {Path.GetDirectoryName(_s.EffectiveSteamCmd)} and checked before it is used.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r == DialogResult.Yes) { await InstallAsync(); return; }
        Append("SteamCMD is not installed, so nothing can be uploaded. Install it from the Options tab (Install SteamCMD).", Color.Orange);
    }

    async Task InstallAsync()
    {
        SaveFields();
        SetBusy(true);
        try { await SteamLogin.InstallAsync(_s.EffectiveSteamCmd, l => Append(l), CancellationToken.None); }
        catch (Exception e) { Append("Install failed: " + e.Message, Color.Tomato); }
        finally { SetBusy(false); UpdateLoginHint(); }
    }

    // ---------------------------------------------------------------- updates

    /// <summary>
    /// Looks for a newer release on GitHub and offers to download it. At start-up (<paramref name="manual"/> false) it
    /// stays quiet when up to date, offline, or the release was skipped; "Check now" always says what it found.
    /// </summary>
    async Task CheckForUpdateAsync(bool manual)
    {
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
        Append($"SteamCMD Helper {r.Version} is available (you have {UpdateCheck.CurrentVersion}): {r.PageUrl}", Color.DeepSkyBlue);

        var download = new TaskDialogButton("Download");
        var notes = new TaskDialogButton("Release notes");
        var later = new TaskDialogButton("Later");
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"SteamCMD Helper {r.Version} is available",
            Text = $"You have {UpdateCheck.CurrentVersion}. " + (r.DownloadUrl != null
                ? "Download saves the new exe through your browser; close this one and run the new one."
                : "Open the release page to download it."),
            Icon = TaskDialogIcon.Information,
            Buttons = { download, notes, later },
            Verification = new TaskDialogVerificationCheckBox($"Don't remind me about {r.Version} again"),
        };
        if (r.DownloadUrl == null) page.Buttons.Remove(download);
        var picked = TaskDialog.ShowDialog(this, page);
        _s.SkippedUpdate = page.Verification.Checked ? r.Version : "";
        _s.Save();
        var url = picked == download ? r.DownloadUrl : picked == notes ? r.PageUrl : null;
        if (url == null) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Append("Could not open the browser: " + e.Message + " Download it from " + r.PageUrl, Color.Tomato); }
    }

    // ---------------------------------------------------------------- upload

    async Task GoAsync()
    {
        SaveFields();
        var picked = Selected().ToList();
        if (picked.Count == 0) return;
        if (!_cfg.AppIdValid) { MessageBox.Show(this, "Enter the game's Steam app ID first (the number in its store page URL).", Text); _appId.Focus(); return; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { ShowPage(_optionsPage); MessageBox.Show(this, "SteamCMD is not installed yet. Click Install SteamCMD on the Options tab.", Text); return; }

        var dry = _dry.Checked;
        var sb = new StringBuilder();
        sb.AppendLine(dry ? "Dry run for:" : $"Publish to the Steam Workshop ({(_cfg.GameName.Length > 0 ? _cfg.GameName : "app " + _cfg.AppId)}):");
        sb.AppendLine();
        foreach (var m in picked)
        {
            var page = WorkshopPage.Get(m.Dir);
            var create = "NEW " + WorkshopPage.VisibilityName(page?.Visibility ?? WorkshopPage.Private).ToLowerInvariant() + " item";
            sb.AppendLine($"  {m.Name}  v{m.Version}   {(m.OnSteam ? "update " + m.WorkshopId : create)}{(m.Warnings.Count > 0 ? "   (" + m.Warnings[0] + ")" : "")}");
            if (page != null && m.OnSteam) sb.AppendLine($"      also changes the Workshop page: {page.Summary()}");
        }
        if (!dry) { sb.AppendLine(); sb.AppendLine("Updates go live for subscribers right away."); }
        if (MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.OKCancel, dry ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.OK) return;

        if (!dry && _cfg.Experimental)
        {
            var warn = MessageBox.Show(this,
                $"The \"{_cfg.PresetName()}\" layout is experimental: it has not been proven against the real game, and the game may expect its own in-game uploader's packaging.\n\n" +
                "Updating an EXISTING item replaces its content on Steam. Try a brand-new private item first (tick Allow creating new items), and check it in the game before updating anything real.\n\nContinue?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (warn != DialogResult.Yes) return;
        }

        if (!dry && !await LoginFlowAsync()) { Append("Stopped: not logged in to Steam.", Color.Orange); return; }

        var pub = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account) { PageEdits = m => WorkshopPage.Get(m.Dir) };
        pub.Log += l => BeginInvoke(() => Append(l, LineColor(l)));
        pub.Stage += (t, p) => BeginInvoke(() => SetStage(t, p));

        // One login check above covers the batch; each mod then reuses Steam's saved login.
        var batch = new BatchUpload(pub);
        batch.Starting += (m, i, n) =>
        {
            _idx = i;
            _current.Text = $"{m.Name} v{m.Version}  ({i + 1} of {n})";
            _modBar.Value = 0;
            SetStage("Starting...", 3);
            Append($"--- {m.Name} ---", Color.DeepSkyBlue);
        };
        batch.Finished += (r, i, n) =>
        {
            if (r.Status == BatchStatus.NotRun) Append("Cancelled.", Color.Orange);
            else if (!r.Ok) Append($"SKIPPED {r.Mod.Name}: {r.Message}", Color.Tomato);
            SetStage(r.Ok ? r.Message : "Failed - see the log", r.Ok ? 100 : 0);
            _overall.Value = Math.Min(100, (int)((i + 1) * 100.0 / n));
        };

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _overall.Value = 0;
        _idx = 0;
        _count = picked.Count;
        List<BatchItem> results;
        try { results = await batch.RunAsync(picked, !dry, _cts.Token); }
        finally
        {
            _cts?.Dispose(); _cts = null;
            SetBusy(false);
        }
        // Page changes went out with each successful upload.
        if (!dry) foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created)) WorkshopPage.Clear(r.Mod.Dir);
        var counts = BatchUpload.Counts(results);
        var allOk = results.All(r => r.Ok);
        _current.Text = "Finished: " + counts;
        Append(BatchUpload.Summary(results), allOk ? Color.LightGreen : Color.Orange);
        RefreshAll();
        if (picked.Count > 1 || !allOk)
            MessageBox.Show(this, BatchUpload.Summary(results), Text, MessageBoxButtons.OK, allOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    void SetStage(string text, int pct)
    {
        _stage.Text = text;
        _modBar.Value = Math.Clamp(Math.Max(_modBar.Value, pct), 0, 100);
        if (_count > 0) _overall.Value = Math.Clamp((int)((_idx * 100.0 + _modBar.Value) / _count), 0, 100);
    }

    void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }
}
