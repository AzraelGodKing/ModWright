namespace SteamCmdHelper;

/// <summary>Shows what a mod's package holds (each file and its size) and anything in it that looks out of place.</summary>
public sealed class PackagePreviewForm : Form
{
    public PackagePreviewForm(ModInfo mod, PackageReport report)
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

        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        list.Columns.Add("File", 520);
        list.Columns.Add("Size", 100, HorizontalAlignment.Right);
        list.BeginUpdate();
        foreach (var f in report.Files) list.Items.Add(new ListViewItem(new[] { f.Path, PackageCheck.Size(f.Bytes) }));
        list.EndUpdate();

        var open = new Button { Text = "Open mod folder", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mod.Dir) { UseShellExecute = true }); } catch { } };
        var close = new Button { Text = "Close", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel }.Role(ThemeRole.Accent);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(close);
        buttons.Controls.Add(open);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        foreach (var size in new[] { 0, 0, 100, 0 })
            root.RowStyles.Add(size == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, size));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(warnings, 0, 1);
        root.Controls.Add(list, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        CancelButton = close;
        Theme.Apply(this);
        Shown += (_, _) => list.Columns[0].Width = Math.Max(200, list.ClientSize.Width - list.Columns[1].Width - 4);
    }
}
