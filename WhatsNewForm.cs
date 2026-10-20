namespace SteamCmdHelper;

/// <summary>The changelog sections since the version that last ran (or all of them, from Options > About).</summary>
public sealed class WhatsNewForm : Form
{
    public WhatsNewForm(string heading, string text)
    {
        Text = "What's new in SteamCMD Helper";
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 480);
        MinimumSize = new Size(420, 300);
        Padding = new Padding(16, 12, 16, 12);

        var intro = new Label { Text = heading, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8), UseMnemonic = false }.Role(ThemeRole.Muted);
        var body = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill,
            Text = text, AccessibleName = "What's new", BorderStyle = BorderStyle.FixedSingle,
        };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 2, 14, 2), Dock = DockStyle.Right }.Role(ThemeRole.Accent);
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 10, 0, 0) };
        bar.Controls.Add(close);
        Controls.Add(body);
        Controls.Add(bar);
        Controls.Add(intro);
        AcceptButton = CancelButton = close;
        Shown += (_, _) => { body.SelectionLength = 0; close.Focus(); };
        Theme.Apply(this);
    }
}
