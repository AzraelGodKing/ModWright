using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Remembering the window between starts, what's new, and exporting or importing settings.</summary>
public sealed partial class MainForm
{
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
            var n = SettingsTransfer.Export(dlg.FileName, dropPod: true);
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
        ApplySettingsFile(dlg.FileName, "Replace this PC's settings and upload history with the ones in this file? The current ones are kept in a backup folder first.", "Imported");
    }

    /// <summary>Options > About > Your data > Restore a backup: picks one of the daily backups and puts it back.</summary>
    void RestoreSettingsBackup()
    {
        if (_busy) return;
        var list = SettingsBackups.List(SettingsBackups.DefaultDir);
        if (list.Count == 0) { MessageBox.Show(this, "There are no backups yet. One is made each day Modwright starts.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var w = new ListWindow("Restore a backup", "Pick the day to go back to. Your current settings are kept in a backup folder first, so this can be undone too.",
            new[] { ("Made", 220), ("Size", 100) });
        foreach (var b in list) w.AddRow(b, Tone.Normal, b.Made.ToString("dddd d MMMM yyyy, HH:mm"), PackageCheck.Size(b.Bytes));
        SettingsBackup? picked = null;
        w.DoubleClicked = tag => { picked = tag as SettingsBackup; w.Close(); };
        w.AddButton("&Restore", () => { picked = w.Selected as SettingsBackup; w.Close(); }, needsRow: true, primary: true);
        w.AddButton("&Open the folder", () => OpenFolder(SettingsBackups.DefaultDir));
        w.AddClose();
        w.ShowDialog(this);
        if (picked != null)
            ApplySettingsFile(picked.Path, $"Go back to the settings, upload history and Drop Pod modlists from {picked.Made:d MMMM, HH:mm}? The current ones are kept in a backup folder first.", "Restored");
    }

    /// <summary>Reads a settings zip (an export or a daily backup), asks, asks where moved mods folders are, then takes its values.</summary>
    void ApplySettingsFile(string file, string question, string verb)
    {
        var (imported, error) = SettingsTransfer.Read(file);
        if (imported == null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (MessageBox.Show(this, question, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

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
            var backup = SettingsTransfer.Import(file, imported);
            _s.CopyFrom(imported);
            Append($"{verb} settings and upload history from {file}. The previous ones are in {backup}.", LogKind.Ok);
        }
        catch (Exception e) { MessageBox.Show(this, "Could not import the settings: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        FillRecent();
        ShowProfiles();
        ShowAnnounce();
        _ = RefreshAllAsync();
        MessageBox.Show(this, $"Settings {verb.ToLowerInvariant()}. Restart Modwright so every option shows its new value.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        _emptyClear.Visible = _emptyNew.Visible = false;
        _emptyBrowse.Visible = true;
        if (!Directory.Exists(_s.RepoPath))
        {
            _emptyTitle.Text = "Pick your mods folder";
            _emptyText.Text = "Choose the folder that holds your mods, one sub-folder per mod.\nYou can also drop the folder onto this window.";
        }
        else if (_rows.Count == 0)
        {
            _emptyTitle.Text = "No mods found in this folder";
            _emptyText.Text = $"Each mod folder needs {_cfg.ModMarker}. Pick the folder that contains the mod folders, or make a new mod.";
            _emptyNew.Visible = true;
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
}
