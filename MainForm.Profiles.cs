using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Upload profiles and announcements after an upload.</summary>
public sealed partial class MainForm
{
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
        using var f = new PackagePreviewForm(m, report, patterns =>
        {
            var have = _cfg.LeaveOut ?? Array.Empty<string>();
            _cfg.LeaveOut = have.Concat(patterns).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            try
            {
                _cfg.Save(_s.RepoPath);
                return $"Every package from this mods folder now leaves out {string.Join(", ", patterns)} (saved in {ProjectConfig.FileName}" +
                       (RepoIsGit ? "; commit it so a fresh clone and CI do the same)." : ").");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _cfg.LeaveOut = have; return "Could not save the list: " + e.Message; }
        });
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
