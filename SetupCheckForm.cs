namespace SteamCmdHelper;

/// <summary>The setup check's results (Options > About > Check setup): one line per check, what to fix first.</summary>
public sealed class SetupCheckForm : Form
{
    public SetupCheckForm(IReadOnlyList<SetupCheck> checks)
    {
        Text = "Setup check";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Padding = new Padding(12, 10, 12, 10), AccessibleName = "Setup check" }.Role(ThemeRole.Surface);
        foreach (var c in checks.OrderByDescending(c => c.Level))
        {
            var (mark, color) = c.Level switch
            {
                CheckLevel.Ok => ("OK", Theme.Current.Ok),
                CheckLevel.Warn => ("Check", Theme.Current.Warn),
                _ => ("Fix", Theme.Current.Error),
            };
            table.Controls.Add(new Label { Text = mark, AutoSize = true, ForeColor = color, Font = new Font(Theme.BodyFont, FontStyle.Bold), Margin = new Padding(0, 4, 12, 4) }.Role(ThemeRole.Keep));
            table.Controls.Add(new Label { Text = c.Name, AutoSize = true, Font = new Font(Theme.BodyFont, FontStyle.Bold), Margin = new Padding(0, 4, 16, 4), UseMnemonic = false });
            table.Controls.Add(new Label { Text = c.Detail, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 4, 0, 4), UseMnemonic = false });
        }

        var intro = new Label { Text = Doctor.Summary(checks.ToList()), AutoSize = true, Font = new Font(Theme.BodyFont, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
        var note = new Label { Text = "Nothing was changed. The same check runs from the command line: SteamCmdHelper doctor (add --login to really log in).", AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 8, 0, 0) }.Role(ThemeRole.Muted);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 2, 14, 2), Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) }.Role(ThemeRole.Accent);
        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(16, 12), Padding = new Padding(0, 0, 16, 12) };
        root.Controls.Add(intro);
        root.Controls.Add(table);
        root.Controls.Add(note);
        root.Controls.Add(close);
        Controls.Add(root);
        AcceptButton = CancelButton = close;
        Theme.Apply(this);
    }
}
