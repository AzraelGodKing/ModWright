namespace SteamCmdHelper;

/// <summary>Shows what a mod's package holds (each file and its size) and anything in it that looks out of place.</summary>
public sealed class PackagePreviewForm : Form
{
    /// <param name="leaveOut">Adds patterns to the mods folder's leave-out list and says what it did; null hides the button.</param>
    public PackagePreviewForm(ModInfo mod, PackageReport report, Func<IReadOnlyList<string>, string>? leaveOut = null)
    {
        Text = "Package - " + mod.Name;
        Width = 720; Height = 560; MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

        var summary = new Label
        {
            AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 0, 0, 6),
            Text = $"{report.Files.Count} files, {PackageCheck.Size(report.TotalBytes)} in total. This is exactly what an upload of {mod.Name} {mod.Version} would send to Steam.",
        };
        var warnings = new Label
        {
            AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 0, 0, 6), Visible = report.Warnings.Count > 0,
            Text = string.Join("\n", report.Warnings.Select(w => "• The package " + w + ".")),
            ForeColor = Theme.Current.Warn,
        }.Role(ThemeRole.Keep);

        var diff = report.Diff;
        if (diff != null)
        {
            summary.Text += $" Compared with {diff.LastUpload}: {diff.Summary}.";
            var w = diff.Warnings();
            if (w.Count > 0)
            {
                warnings.Visible = true;
                warnings.Text = string.Join("\n", report.Warnings.Concat(w).Select(x => "• The package " + x + "."));
            }
        }

        if (report.LeftOut.Count > 0)
        {
            var code = report.LeftOut.Count(PackageCheck.IsCode);
            warnings.Visible = true;
            warnings.Text += (warnings.Text.Length > 0 ? "\n" : "") + $"• {report.LeftOut.Count} file{(report.LeftOut.Count == 1 ? " is" : "s are")} in the mod folder but not in git, so " +
                (report.LeftOut.Count == 1 ? "it is" : "they are") + " left out (listed at the end)" + (code > 0 ? ". A .dll among them means players would get the mod without its code, so the upload stops until it is fixed." : ".");
        }

        // Where the size is, so a big package says why at a glance.
        var top = PackageCheck.ByTopFolder(report.Files).Take(3).ToList();
        if (report.TotalBytes > 0 && top.Count > 1)
            summary.Text += " Most of it: " + string.Join(", ", top.Select(t => $"{t.Folder} {PackageCheck.Size(t.Bytes)} ({t.Bytes * 100 / report.TotalBytes}%)")) + ".";

        var rare = PackageCheck.RarelyNeeded(report.Files);
        var rareText = new Label
        {
            AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 0, 0, 6), Visible = rare.Count > 0,
            Text = "Players rarely need: " + string.Join(", ", rare.Select(r => $"{r.Pattern} ({r.Files} file{(r.Files == 1 ? "" : "s")}, {PackageCheck.Size(r.Bytes)})")) + ".",
        };
        var leave = new Button { Text = "Leave these out of every package", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Visible = rare.Count > 0 && leaveOut != null };
        leave.Click += (_, _) =>
        {
            rareText.Text = leaveOut!(rare.Select(r => r.Pattern).ToList());
            leave.Enabled = false;
        };

        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Clickable };
        list.Columns.Add("File", 440);
        list.Columns.Add("Size", 90, HorizontalAlignment.Right);
        var third = diff != null || report.LeftOut.Count > 0;
        list.Columns.Add(diff != null ? "Since last upload" : third ? "Note" : "", third ? 110 : 0);
        string Change(string path) => diff == null ? "" : diff.Added.Contains(path) ? "added" : diff.Changed.Contains(path) ? "changed" : "";
        list.BeginUpdate();
        foreach (var f in report.Files) list.Items.Add(new ListViewItem(new[] { f.Path, PackageCheck.Size(f.Bytes), Change(f.Path) }));
        // Files the last upload had and this one does not, at the end so they read as gone.
        foreach (var gone in diff?.Removed ?? Array.Empty<string>())
            list.Items.Add(new ListViewItem(new[] { gone, "", "removed" }) { ForeColor = Theme.Current.Warn });
        foreach (var left in report.LeftOut)
            list.Items.Add(new ListViewItem(new[] { left, "", "left out" }) { ForeColor = Theme.Current.Warn });
        list.EndUpdate();
        // Click Size to see the biggest files first (and again for the smallest); click File to go back to A to Z.
        var bySize = 0;
        var bytes = report.Files.ToDictionary(f => f.Path, f => f.Bytes, StringComparer.OrdinalIgnoreCase);
        list.ColumnClick += (_, e) =>
        {
            bySize = e.Column == 1 ? (bySize == 1 ? -1 : 1) : 0;
            list.ListViewItemSorter = new RowOrder(bySize, bytes);
            list.Sort();
        };

        var open = new Button { Text = "Open mod folder", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mod.Dir) { UseShellExecute = true }); } catch { } };
        var close = new Button { Text = "Close", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel }.Role(ThemeRole.Accent);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(close);
        buttons.Controls.Add(open);
        buttons.Controls.Add(leave);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        foreach (var size in new[] { 0, 0, 0, 100, 0 })
            root.RowStyles.Add(size == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, size));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(warnings, 0, 1);
        root.Controls.Add(rareText, 0, 2);
        root.Controls.Add(list, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);
        CancelButton = close;
        Theme.Apply(this);
        Shown += (_, _) => list.Columns[0].Width = Math.Max(200, list.ClientSize.Width - list.Columns[1].Width - list.Columns[2].Width - 4);
    }

    /// <summary>A to Z by path, or by size (1 biggest first, -1 smallest first). Rows without a size (removed, left out) stay at the end.</summary>
    sealed class RowOrder(int bySize, IReadOnlyDictionary<string, long> bytes) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = ((ListViewItem)x!).Text;
            var b = ((ListViewItem)y!).Text;
            var (ha, hb) = (bytes.TryGetValue(a, out var sa), bytes.TryGetValue(b, out var sb));
            if (ha != hb) return ha ? -1 : 1;
            if (bySize != 0 && ha && sa != sb) return bySize * sb.CompareTo(sa);
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }
    }
}
