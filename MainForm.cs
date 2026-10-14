using System.Diagnostics;
using System.Text;

namespace SteamCmdHelper;

public sealed class MainForm : Form
{
    readonly AppSettings _s = AppSettings.Load();

    readonly TextBox _repo = new() { Tag = "fill" };
    readonly TextBox _account = new() { Width = 200 };
    readonly Label _branch = new() { AutoSize = true };
    readonly Label _login = new() { AutoSize = true, Tag = "fill" };
    readonly ListView _list = new();
    readonly ProgressBar _modBar = new() { Minimum = 0, Maximum = 100, Height = 16, Dock = DockStyle.Top };
    readonly ProgressBar _overall = new() { Minimum = 0, Maximum = 100, Height = 16, Dock = DockStyle.Top };
    readonly Label _current = new() { AutoSize = true, Text = "Idle" };
    readonly Label _stage = new() { AutoSize = true, ForeColor = Color.DimGray };
    readonly CheckBox _dry = new() { AutoSize = true, Text = "Dry run (check everything, upload nothing)" };
    readonly CheckBox _allowCreate = new() { AutoSize = true, Text = "Allow creating new items (private) for mods not on Steam yet" };
    readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24, 24, 28), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None };

    Button _browse = null!, _refresh = null!, _go = null!, _cancel = null!, _checkLogin = null!, _doLogin = null!, _install = null!;

    List<ModInfo> _mods = new();
    CancellationTokenSource? _cts;
    Process? _proc;
    bool _busy, _populating;
    bool _modOk, _modFailed;
    int _idx, _count;

    string ScriptPath => Path.Combine(_s.RepoPath, "scripts", "steam_publish.ps1");

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
        Width = 1000; Height = 780; MinimumSize = new Size(840, 640);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;

        _browse = Btn("Browse...", (_, _) => BrowseRepo());
        _checkLogin = Btn("Check login", async (_, _) => { SaveFields(); await VerifyLoginAsync(true); });
        _doLogin = Btn("Log in...", async (_, _) => { SaveFields(); await LoginFlowAsync(); });
        _install = Btn("Install SteamCMD", async (_, _) => await InstallAsync());
        _refresh = Btn("Refresh", (_, _) => RefreshAll());
        _go = Btn("Upload selected", async (_, _) => await GoAsync());
        _go.Font = new Font(Font, FontStyle.Bold);
        _cancel = Btn("Cancel", (_, _) => Cancel());
        _cancel.Enabled = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));

        root.Controls.Add(Row(Lbl("Mods repo"), _repo, _browse, _branch), 0, 0);
        root.Controls.Add(Row(Lbl("Steam account"), _account, _login, _checkLogin, _doLogin, _install), 0, 1);

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
        root.Controls.Add(_list, 0, 2);

        var prog = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 8, 0, 4) };
        prog.Controls.Add(_current);
        prog.Controls.Add(_modBar);
        prog.Controls.Add(_stage);
        prog.Controls.Add(new Label { Text = "Overall", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        prog.Controls.Add(_overall);
        root.Controls.Add(prog, 0, 3);

        root.Controls.Add(Row(_allowCreate, _dry), 0, 4);

        var buttons = Row(new Label { Tag = "fill", Text = "" }, _refresh, _cancel, _go);
        root.Controls.Add(buttons, 0, 5);

        _log.Margin = new Padding(0, 6, 0, 0);
        root.Controls.Add(_log, 0, 6);

        Controls.Add(root);

        _repo.Leave += (_, _) => { if (_repo.Text.Trim() != _s.RepoPath) { SaveFields(); RefreshAll(); } };
        _account.Leave += (_, _) => { SaveFields(); UpdateLoginHint(); };
        _allowCreate.CheckedChanged += (_, _) => { if (!_populating) { SaveFields(); RefreshAll(); } };
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
        using var d = new FolderBrowserDialog { Description = "Pick the folder that holds your RimWorld mods (the git repo)", SelectedPath = _repo.Text };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _repo.Text = d.SelectedPath;
        SaveFields();
        RefreshAll();
    }

    void RefreshAll()
    {
        SaveFields();
        var branch = ModScanner.GitBranch(_s.RepoPath);
        _branch.Text = branch == null ? "(not a git repo)" : "branch: " + branch;

        _populating = true;
        _list.Items.Clear();
        _mods = ModScanner.Scan(_s.RepoPath);
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

        if (!File.Exists(ScriptPath))
            Append($"Cannot find scripts\\steam_publish.ps1 in {_s.RepoPath}. Check out the branch that has it (feat/steam-workshop-upload) or merge it.", Color.Orange);
        if (_mods.Count == 0)
            Append("No mods found. Pick the folder that contains the mod folders (each has About\\About.xml).", Color.Orange);

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
        _go.Enabled = !_busy && n > 0;
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var c in new Control[] { _browse, _refresh, _checkLogin, _doLogin, _install, _repo, _account, _list, _dry, _allowCreate })
            c.Enabled = !busy;
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
        if (l.StartsWith("===")) return Color.DeepSkyBlue;
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
            var (ok, output) = await SteamLogin.VerifyAsync(steam, _s.Account, CancellationToken.None);
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
        if (!File.Exists(ScriptPath)) { MessageBox.Show(this, "scripts\\steam_publish.ps1 is missing from the repo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        if (!SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd)) { MessageBox.Show(this, "SteamCMD is not installed yet. Click Install SteamCMD.", Text); return; }

        var dry = _dry.Checked;
        var sb = new StringBuilder();
        sb.AppendLine(dry ? "Dry run for:" : "Publish to the Steam Workshop:");
        sb.AppendLine();
        foreach (var m in picked)
            sb.AppendLine($"  {m.Name}  v{m.Version}   {(m.OnSteam ? "update " + m.WorkshopId : "NEW private item")}{(m.Warnings.Count > 0 ? "   (" + m.Warnings[0] + ")" : "")}");
        if (!dry) { sb.AppendLine(); sb.AppendLine("Updates go live for subscribers right away."); }
        if (MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.OKCancel, dry ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.OK) return;

        if (!dry && !await LoginFlowAsync()) { Append("Stopped: not logged in to Steam.", Color.Orange); return; }

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
                bool good;
                try { good = await RunScriptAsync(m, !dry, _cts.Token); }
                catch (OperationCanceledException) { Append("Cancelled.", Color.Orange); break; }
                if (good) ok++; else failed++;
                SetStage(good ? "Done" : "Failed - see the log", good ? 100 : 0);
                _overall.Value = Math.Min(100, (int)((_idx + 1) * 100.0 / _count));
            }
        }
        finally
        {
            _cts?.Dispose(); _cts = null; _proc = null;
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

    void OnScriptLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Append(line, LineColor(line));
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;
        if (line.Contains("Workshop id", ic)) SetStage("Packaged, preparing upload...", 20);
        else if (line.Contains("Logging in", ic)) SetStage("Logging in to Steam...", 30);
        else if (line.Contains("Uploading content", ic)) SetStage("Uploading files...", 50);
        else if (line.Contains("Uploading preview", ic)) SetStage("Uploading preview image...", 70);
        else if (line.Contains("Committing", ic)) SetStage("Committing update...", 88);
        else if (line.StartsWith("Uploaded ") || line.StartsWith("Created ")) { _modOk = true; SetStage("Uploaded", 100); }
        else if (line.Contains("Dry run: nothing uploaded", ic)) { _modOk = true; SetStage("Dry run OK", 100); }
        else if (line.StartsWith("SKIPPED")) _modFailed = true;
    }

    async Task<bool> RunScriptAsync(ModInfo m, bool upload, CancellationToken ct)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var cmd = new StringBuilder();
        cmd.Append("[Console]::OutputEncoding=[Text.Encoding]::UTF8; & ").Append(Q(ScriptPath));
        cmd.Append(" -Mod ").Append(Q(m.Folder)).Append(" -SteamCmd ").Append(Q(_s.EffectiveSteamCmd));
        if (!m.OnSteam) cmd.Append(" -Create");
        if (upload) cmd.Append(" -Upload -Account ").Append(Q(_s.Account));
        cmd.Append("; exit $LASTEXITCODE");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd.ToString()));

        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded)
        {
            WorkingDirectory = _s.RepoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _modOk = false; _modFailed = false;
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) BeginInvoke(() => OnScriptLine(e.Data)); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) BeginInvoke(() => { Append(e.Data!, Color.Tomato); _modFailed = true; }); };
        p.Start();
        _proc = p;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        await Task.Run(() => p.WaitForExit());
        ct.ThrowIfCancellationRequested();
        await Task.Delay(150); // let the last BeginInvoke'd lines land
        return _modOk && !_modFailed;
    }

    void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
        try { _proc?.Kill(true); } catch { }
    }
}
