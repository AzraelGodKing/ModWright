using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Keyboard shortcuts, the shortcuts list, status lines and dropping folders and files on the window.</summary>
public sealed partial class MainForm
{
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
            case Keys.Control | Keys.S when _dropPodPage.Visible && _dpSave.Enabled:
                SaveDropPodToGame(); return true;
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
            case Keys.Escape when _optionsPage.Visible && !Focused<ComboBox>(c => c.DroppedDown):
                ShowPage(_onDropPod ? _dropPodPage : _uploadPage); // back to the side Options was opened from
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Whether the focused control is a <typeparamref name="T"/> that <paramref name="test"/> holds for.</summary>
    bool Focused<T>(Func<T, bool> test) where T : Control
    {
        Control? c = ActiveControl;
        while (c is ContainerControl { ActiveControl: { } inner }) c = inner;
        return c is T t && test(t);
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
}
