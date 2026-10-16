namespace SteamCmdHelper;

/// <summary>Lists the saved logs of earlier upload runs (<see cref="RunLogs"/>) and shows the one picked.</summary>
public sealed class PastLogsForm : Form
{
    public PastLogsForm(string dir)
    {
        Text = "Past upload logs";
        Width = 960; Height = 600; MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;

        var logs = RunLogs.List(dir);
        var summary = new Label
        {
            AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 0, 0, 6),
            Text = logs.Count == 0
                ? "No upload logs yet. Each upload run's log is saved here when it finishes."
                : $"{logs.Count} saved upload log{(logs.Count == 1 ? "" : "s")}, newest first. The newest {RunLogs.Keep} are kept.",
        };

        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, AccessibleName = "Past logs" };
        list.Columns.Add("When", 130);
        list.Columns.Add("What", 200);
        foreach (var l in logs) list.Items.Add(new ListViewItem(new[] { l.Time.ToString("d MMM yyyy HH:mm"), l.Label }) { Tag = l });

        var text = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = new Font("Consolas", 9f), AccessibleName = "Log",
        }.Role(ThemeRole.Log);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItems.Count == 0) return;
            var l = (RunLog)list.SelectedItems[0].Tag!;
            try { text.Text = File.ReadAllText(l.Path).Replace("\r\n", "\n").Replace("\n", Environment.NewLine); }
            catch (Exception e) { text.Text = "Could not read this log: " + e.Message; }
            text.SelectionStart = text.TextLength;
            text.ScrollToCaret();
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(text);

        var open = new Button { Text = "Open logs folder", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch { }
        };
        var close = new Button { Text = "Close", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel }.Role(ThemeRole.Accent);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(close);
        buttons.Controls.Add(open);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        foreach (var size in new[] { 0, 100, 0 })
            root.RowStyles.Add(size == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, size));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        CancelButton = close;
        Theme.Apply(this);
        Shown += (_, _) =>
        {
            list.Columns[1].Width = Math.Max(150, list.ClientSize.Width - list.Columns[0].Width - 4);
            if (list.Items.Count > 0) list.Items[0].Selected = true;
        };
    }
}
