using System.Diagnostics;
using System.Text;

namespace SteamCmdHelper;

public sealed class MainForm : Form
{
    readonly AppSettings _s = AppSettings.Load();
    ProjectConfig _cfg = new();

    readonly TextBox _repo = new() { Tag = "fill", ReadOnly = true, TabStop = false, Cursor = Cursors.Hand };
    readonly TextBox _account = new() { Width = 200 };
    readonly Label _branch = new() { AutoSize = true };
    readonly TextBox _appId = new() { Width = 90 };
    readonly ComboBox _preset = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _game = new() { AutoSize = true, Tag = "fill", ForeColor = Color.DimGray };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    readonly Label _login = new() { AutoSize = true, Tag = "fill" };
    readonly ListView _list = new();
    readonly ProgressBar _modBar = new() { Minimum = 0, Maximum = 100, Height = 16, Dock = DockStyle.Top };
    readonly ProgressBar _overall = new() { Minimum = 0, Maximum = 100, Height = 16, Dock = DockStyle.Top };
    readonly Label _current = new() { AutoSize = true, Text = "Idle" };
    readonly Label _stage = new() { AutoSize = true, ForeColor = Color.DimGray };
    readonly CheckBox _dry = new() { AutoSize = true, Text = "Dry run (check everything, upload nothing)" };
    readonly CheckBox _allowCreate = new() { AutoSize = true, Text = "Allow creating new items (private) for mods not on Steam yet" };
    readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24, 24, 28), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None };

    Button _browse = null!, _refresh = null!, _go = null!, _cancel = null!, _checkLogin = null!, _doLogin = null!, _install = null!, _editLayout = null!;

    List<ModInfo> _mods = new();
    CancellationTokenSource? _cts;
    bool _busy, _populating;
    int _idx, _count;

    public MainForm()
    {
        BuildUi();
        _repo.Text = _s.RepoPath;
        _account.Text = _s.Account;
        _allowCreate.Checked = _s.AllowCreate;
        Shown += (_, _) => { RefreshAll(); if (SteamLogin.LooksCached(_s.EffectiveSteamCmd, _s.Account)) _ = VerifyLoginAsync(false); };
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
        Width = 1000; Height = 800; MinimumSize = new Size(840, 660);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;

        _browse = Btn("Browse...", (_, _) => BrowseRepo());
        _editLayout = Btn("Edit layout file...", (_, _) => EditLayout());
        _checkLogin = Btn("Check login", async (_, _) => { SaveFields(); await VerifyLoginAsync(true); });
        _doLogin = Btn("Log in...", async (_, _) => { SaveFields(); await LoginFlowAsync(); });
        _install = Btn("Install SteamCMD", async (_, _) => await InstallAsync());
        _refresh = Btn("Refresh", (_, _) => RefreshAll());
        _go = Btn("Upload selected", async (_, _) => await GoAsync());
        _go.Font = new Font(Font, FontStyle.Bold);
        _cancel = Btn("Cancel", (_, _) => Cancel());
        _cancel.Enabled = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));

        root.Controls.Add(Row(Lbl("Mods repo"), _repo, _browse, _branch), 0, 0);
        _preset.Items.AddRange(ProjectConfig.PresetNames.Cast<object>().Append("Custom").ToArray());
        _preset.Width = 150;
        root.Controls.Add(Row(Lbl("Steam app ID"), _appId, Lbl("Layout"), _preset, _editLayout, _game), 0, 1);
        root.Controls.Add(Row(Lbl("Steam account"), _account, _login, _checkLogin, _doLogin, _install), 0, 2);

        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Dock = DockStyle.Fill;
        _list.Columns.Add("Mod", 220);
        _list.Columns.Add("Version", 80);
        _list.Columns.Add("Workshop ID", 120);
        _list.Columns.Add("Status", 520);
        _list.ItemCheck += OnItemCheck;
        _list.ItemChecked += (_, _) => UpdateGoButton();
        root.Controls.Add(_list, 0, 3);

        var prog = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 8, 0, 4) };
        prog.Controls.Add(_current);
        prog.Controls.Add(_modBar);
        prog.Controls.Add(_stage);
        prog.Controls.Add(new Label { Text = "Overall", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        prog.Controls.Add(_overall);
        root.Controls.Add(prog, 0, 4);

        root.Controls.Add(Row(_allowCreate, _dry), 0, 5);
        root.Controls.Add(Row(new Label { Tag = "fill", Text = "" }, _refresh, _cancel, _go), 0, 6);

        _log.Margin = new Padding(0, 6, 0, 0);
        root.Controls.Add(_log, 0, 7);

        Controls.Add(root);

        _repo.DoubleClick += (_, _) => { if (!_busy) BrowseRepo(); };
        _account.Leave += (_, _) => { SaveFields(); UpdateLoginHint(); };
        _allowCreate.CheckedChanged += (_, _) => { if (!_populating) { SaveFields(); RefreshAll(); } };
        _appId.Leave += async (_, _) => await ApplyAppIdAsync();
        _appId.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyAppIdAsync(); } };
        _preset.SelectionChangeCommitted += (_, _) => ApplyPresetChoice();
    }

    // ---------------------------------------------------------------- game / layout

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
            _game.ForeColor = Color.DarkOrange;
        }
        else
        {
            _game.Text = $"{(_cfg.GameName.Length > 0 ? _cfg.GameName : "App " + _cfg.AppId)}  -  layout: {_cfg.Source}"
                         + (_cfg.IsBuiltIn ? " (locked; choose Custom to make your own)" : "");
            _game.ForeColor = Color.DimGray;
        }
        ApplyLocks();
    }

    /// <summary>Built-in layouts are read-only: no editing the layout file, and no changing the app id when the layout fixes it.</summary>
    void ApplyLocks()
    {
        _editLayout.Enabled = !_busy && !_cfg.IsBuiltIn;
        _appId.Enabled = !_busy && !_cfg.AppIdLocked;
    }

    static Label Lbl(string t) => new() { Text = t, AutoSize = true };

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
        {
            var it = new ListViewItem(m.Name);
            it.SubItems.Add(m.Version);
            it.SubItems.Add(m.WorkshopId ?? "(not on Steam)");
            it.SubItems.Add(m.StatusText);
            it.Tag = m;
            var usable = m.Ready && (m.OnSteam || _s.AllowCreate);
            it.Checked = usable && m.OnSteam;
            it.ForeColor = !m.Ready ? Color.Firebrick : (!m.OnSteam && !_s.AllowCreate) ? Color.Gray : SystemColors.WindowText;
            _list.Items.Add(it);
        }
        _populating = false;

        if (_mods.Count == 0)
            Append(string.IsNullOrWhiteSpace(_cfg.ModMarker)
                ? "No mod folders found. Pick the folder that contains one sub-folder per mod, or press Edit layout file to point at them."
                : $"No mods found. Pick the folder that contains the mod folders (each needs {_cfg.ModMarker}), choose a different Layout, or press Edit layout file.", Color.Orange);

        UpdateLoginHint();
        UpdateGoButton();
    }

    void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_populating || e.NewValue != CheckState.Checked) return;
        if (_list.Items[e.Index].Tag is not ModInfo m) return;
        if (!m.Ready) { e.NewValue = CheckState.Unchecked; return; }
        if (!m.OnSteam && !_allowCreate.Checked) e.NewValue = CheckState.Unchecked;
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
        foreach (var c in new Control[] { _browse, _editLayout, _refresh, _checkLogin, _doLogin, _install, _repo, _account, _list, _dry, _allowCreate, _appId, _preset })
            c.Enabled = !busy;
        ApplyLocks();
        _cancel.Enabled = busy;
        UpdateGoButton();
    }

    void SetLogin(string text, Color c) { _login.Text = text; _login.ForeColor = c; }

    void UpdateLoginHint()
    {
        var steam = _s.EffectiveSteamCmd;
        _install.Visible = !SteamLogin.SteamCmdInstalled(steam);
        if (!SteamLogin.SteamCmdInstalled(steam)) SetLogin("SteamCMD is not installed - click Install SteamCMD", Color.Firebrick);
        else if (string.IsNullOrWhiteSpace(_s.Account)) SetLogin("Type your Steam account name", Color.DarkOrange);
        else if (SteamLogin.LooksCached(steam, _s.Account)) SetLogin($"Saved login found for {_s.Account} (click Check login to confirm)", Color.DarkGoldenrod);
        else SetLogin($"No saved login for {_s.Account} - you'll be asked to log in once", Color.DarkOrange);
    }

    // ---------------------------------------------------------------- log

    void Append(string line, Color? color = null)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(line, color)); return; }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = color ?? Color.Gainsboro;
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
        if (string.IsNullOrWhiteSpace(_s.Account)) { UpdateLoginHint(); if (announce) MessageBox.Show(this, "Type your Steam account name first.", Text); return false; }

        SetLogin("Checking login...", Color.DimGray);
        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        try
        {
            var (ok, _) = await SteamLogin.VerifyAsync(steam, _s.Account, CancellationToken.None);
            if (ok) SetLogin($"Logged in as {_s.Account}", Color.SeaGreen);
            else SetLogin($"Not logged in as {_s.Account}", Color.Firebrick);
            if (announce || !ok) Append(ok ? $"Steam login OK for {_s.Account}." : $"Steam login check failed for {_s.Account} (no saved login, wrong name, or no connection).", ok ? Color.LightGreen : Color.Orange);
            return ok;
        }
        catch (Exception e) { SetLogin("Login check failed", Color.Firebrick); Append("Login check error: " + e.Message, Color.Tomato); return false; }
        finally { if (!wasBusy) SetBusy(false); }
    }

    /// <summary>Checks the saved login; if there isn't one, opens SteamCMD so you can log in, waits, then checks again.</summary>
    async Task<bool> LoginFlowAsync()
    {
        if (string.IsNullOrWhiteSpace(_s.Account)) { MessageBox.Show(this, "Type your Steam account name first.", Text); return false; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { MessageBox.Show(this, "SteamCMD is not installed yet. Click Install SteamCMD.", Text); return false; }

        if (await VerifyLoginAsync(false)) return true;

        var r = MessageBox.Show(this,
            "Steam needs you to log in once.\n\nA SteamCMD window will open. Type your password, then the Steam Guard code, and wait until the window closes by itself.\n\nYour password goes to Steam only; this program never sees it.",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (r != DialogResult.OK) return false;

        var wasBusy = _busy;
        if (!wasBusy) SetBusy(true);
        SetLogin("Waiting for you to finish logging in...", Color.DimGray);
        try { await SteamLogin.InteractiveLoginAsync(_s.EffectiveSteamCmd, _s.Account, CancellationToken.None); }
        catch (Exception e) { Append("Could not open SteamCMD: " + e.Message, Color.Tomato); }
        finally { if (!wasBusy) SetBusy(false); }

        return await VerifyLoginAsync(true);
    }

    async Task InstallAsync()
    {
        SaveFields();
        SetBusy(true);
        try { await SteamLogin.InstallAsync(_s.EffectiveSteamCmd, l => Append(l), CancellationToken.None); }
        catch (Exception e) { Append("Install failed: " + e.Message, Color.Tomato); }
        finally { SetBusy(false); UpdateLoginHint(); }
    }

    // ---------------------------------------------------------------- upload

    async Task GoAsync()
    {
        SaveFields();
        var picked = Selected().ToList();
        if (picked.Count == 0) return;
        if (!_cfg.AppIdValid) { MessageBox.Show(this, "Enter the game's Steam app ID first (the number in its store page URL).", Text); _appId.Focus(); return; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { MessageBox.Show(this, "SteamCMD is not installed yet. Click Install SteamCMD.", Text); return; }

        var dry = _dry.Checked;
        var sb = new StringBuilder();
        sb.AppendLine(dry ? "Dry run for:" : $"Publish to the Steam Workshop ({(_cfg.GameName.Length > 0 ? _cfg.GameName : "app " + _cfg.AppId)}):");
        sb.AppendLine();
        foreach (var m in picked)
            sb.AppendLine($"  {m.Name}  v{m.Version}   {(m.OnSteam ? "update " + m.WorkshopId : "NEW private item")}{(m.Warnings.Count > 0 ? "   (" + m.Warnings[0] + ")" : "")}");
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

        var pub = new Publisher(_s.RepoPath, _cfg, _s.EffectiveSteamCmd, _s.Account);
        pub.Log += l => BeginInvoke(() => Append(l, LineColor(l)));
        pub.Stage += (t, p) => BeginInvoke(() => SetStage(t, p));

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _overall.Value = 0;
        _count = picked.Count;
        int ok = 0, failed = 0;
        try
        {
            for (_idx = 0; _idx < picked.Count; _idx++)
            {
                var m = picked[_idx];
                _current.Text = $"{m.Name} v{m.Version}  ({_idx + 1} of {_count})";
                _modBar.Value = 0;
                SetStage("Starting...", 3);
                Append($"--- {m.Name} ---", Color.DeepSkyBlue);
                PublishResult res;
                try { res = await pub.RunAsync(m, !dry, _cts.Token); }
                catch (OperationCanceledException) { Append("Cancelled.", Color.Orange); break; }
                catch (Exception e) { res = new PublishResult(false, e.Message); }
                if (!res.Ok) Append($"SKIPPED {m.Name}: {res.Message}", Color.Tomato);
                if (res.Ok) ok++; else failed++;
                SetStage(res.Ok ? res.Message : "Failed - see the log", res.Ok ? 100 : 0);
                _overall.Value = Math.Min(100, (int)((_idx + 1) * 100.0 / _count));
            }
        }
        finally
        {
            _cts?.Dispose(); _cts = null;
            SetBusy(false);
        }
        var verb = dry ? "checked" : "uploaded";
        _current.Text = $"Finished: {ok} {verb}, {failed} failed";
        Append($"Finished: {ok} {verb}, {failed} failed.", failed == 0 ? Color.LightGreen : Color.Orange);
        RefreshAll();
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
