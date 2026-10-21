namespace SteamCmdHelper;

/// <summary>The changelog sections since the version that last ran (or all of them, from Options > About).</summary>
public sealed class WhatsNewForm : Form
{
    readonly RichTextBox _body;
    readonly List<NoteBlock> _blocks;

    public WhatsNewForm(string heading, IEnumerable<(string Version, string Text)> sections)
    {
        Text = "What's new in Modwright";
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(680, 520);
        MinimumSize = new Size(420, 300);
        Padding = new Padding(16, 12, 16, 12);
        _blocks = WhatsNew.Blocks(sections);

        var intro = new Label { Text = heading, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8), UseMnemonic = false }.Role(ThemeRole.Muted);
        _body = new RichTextBox
        {
            ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill, DetectUrls = false,
            AccessibleName = "What's new", BorderStyle = BorderStyle.None, Font = Theme.BodyFont, TabStop = true,
        };
        // The text box has no padding of its own; a frame around it keeps the text off the edges.
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 6, 8), BorderStyle = BorderStyle.FixedSingle };
        frame.Controls.Add(_body);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 2, 14, 2), Dock = DockStyle.Right }.Role(ThemeRole.Accent);
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 10, 0, 0) };
        bar.Controls.Add(close);
        Controls.Add(frame);
        Controls.Add(bar);
        Controls.Add(intro);
        AcceptButton = CancelButton = close;
        Shown += (_, _) => { _body.Select(0, 0); _body.ScrollToCaret(); close.Focus(); };
        Theme.Apply(this);
        frame.BackColor = _body.BackColor;
        Fill();
    }

    /// <summary>
    /// Writes the blocks into the text box: a larger heading per version, bullets with the wrapped lines indented
    /// under the text, the lead phrase in bold and commands and file names in a fixed-width font. Done after theming,
    /// so the fonts follow Options > Appearance > Text size.
    /// </summary>
    void Fill()
    {
        var body = _body.Font;
        var bold = new Font(body, FontStyle.Bold);
        var version = new Font("Segoe UI Semibold", body.SizeInPoints * 1.25f, FontStyle.Regular, GraphicsUnit.Point);
        var subhead = new Font("Segoe UI Semibold", body.SizeInPoints * 1.05f, FontStyle.Regular, GraphicsUnit.Point);
        var code = new Font("Consolas", body.SizeInPoints * 0.95f, FontStyle.Regular, GraphicsUnit.Point);
        var gap = new Font(body.FontFamily, body.SizeInPoints * 0.45f, FontStyle.Regular, GraphicsUnit.Point);
        var indent = TextRenderer.MeasureText("•", body).Width + LogicalToDeviceUnits(4);

        _body.Clear();
        // Lines of a bullet that wrap line up with its text, not under the dot.
        _body.BulletIndent = indent;
        var left = LogicalToDeviceUnits(4);
        void Write(string text, Font font, bool bullet = false)
        {
            _body.SelectionStart = _body.TextLength;
            _body.SelectionLength = 0;
            _body.SelectionFont = font;
            _body.SelectionBullet = bullet;
            _body.SelectionIndent = bullet ? left : 0;
            _body.SelectedText = text;
        }
        for (var i = 0; i < _blocks.Count; i++)
        {
            var b = _blocks[i];
            if (i > 0) Write("\n", b.Kind == NoteKind.Version ? body : gap);
            switch (b.Kind)
            {
                case NoteKind.Version:
                    Write(b.Text + "\n", version);
                    continue;
                case NoteKind.Heading:
                    Write(b.Text + "\n", subhead);
                    continue;
            }
            var bullet = b.Kind == NoteKind.Bullet;
            foreach (var s in b.Spans)
                Write(s.Text, s.Bold ? bold : s.Code ? code : body, bullet);
            Write("\n", body, bullet);
        }
        // Each paragraph ends in a line break, which leaves an empty one at the end; it must not show a dot.
        Write("", body);
        _body.Select(0, 0);
    }
}
