using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Uploading: building the package, running SteamCMD and what happens after.</summary>
public sealed partial class MainForm
{
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
}
