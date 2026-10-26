using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Signing in to SteamCMD and checking the saved login.</summary>
public sealed partial class MainForm
{
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

    string GameLabel() => _cfg.GameName;

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
        if (_busy || !SteamLogin.SteamCmdInstalled(_s.EffectiveSteamCmd))
        {
            if (list) Append(_busy ? "Busy; try again when the current job ends." : "Install SteamCMD first.", LogKind.Warn);
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
}
