using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Drawing the mod list: rows, badges, colours, the details beside it.</summary>
public sealed partial class MainForm
{
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
        foreach (var c in new Control[] { _browse, _refresh, _all, _none, _checkLogin, _doLogin, _install, _repo, _account, _dry, _allowCreate, _theme, _textSize, _checkUpdateNow, _profile, _profileSave, _webhook, _webhookTest, _ghReleases, _uploadCredit, _noteTemplate, _descFooter, _checklist, _checklistMust, _testItems, _changed, _groups, _afterCmd, _pushTags, _keepPackages, _buildCmd, _packageRef, _autoRetry, _updateTags, _updateRequired, _updatePageExtras, _oneSession, _verifyUploads, _steamBack, _restartSteam, _watchBuilds, _strictAbout })
            c.Enabled = !busy;
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
}
