using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SteamCmdHelper;

/// <summary>
/// Package > Make a preview image: a title card for the Workshop, drawn here so no image editor is needed. A picture
/// (a screenshot of the mod) or a colour behind, the mod's name and a line under it, with a shade so the text reads on
/// any picture. RimWorld shows previews at 16:9, other games mostly square.
/// </summary>
public sealed class PreviewMakerForm : Form
{
    readonly PictureBox _view = new() { SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Preview" };
    readonly TextBox _title = new() { Width = 300, AccessibleName = "Title" };
    readonly TextBox _subtitle = new() { Width = 300, AccessibleName = "Line under the title", PlaceholderText = "e.g. Shelves that sort themselves" };
    readonly ComboBox _size = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = "Size" };
    readonly ComboBox _place = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = "Where the text goes" };
    readonly Button _color = new() { Text = "Background &colour...", AutoSize = true };
    readonly Button _picture = new() { Text = "Background &picture...", AutoSize = true };
    readonly Button _accent = new() { Text = "&Text colour...", AutoSize = true };
    Image? _back;
    Color _bg = Color.FromArgb(0x2A, 0x34, 0x39);
    Color _fg = Color.White;
    Bitmap? _current;

    /// <summary>The finished image; the caller saves and disposes it.</summary>
    public Bitmap? Result { get; private set; }

    public PreviewMakerForm(string modName, Size suggested)
    {
        Text = "Make a preview image";
        Font = Theme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _title.Text = modName;
        _size.Items.AddRange(new object[] { "640 x 360 (16:9, RimWorld)", "1280 x 720 (16:9, sharper)", "512 x 512 (square)" });
        _size.SelectedIndex = suggested.Width == suggested.Height ? 2 : 0;
        _place.Items.AddRange(new object[] { "Bottom", "Middle", "Top" });
        _place.SelectedIndex = 0;
        _view.Size = new Size(480, 270);

        var form = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(12, 0, 0, 0) };
        void Row(string label, Control c)
        {
            form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
            form.Controls.Add(c);
        }
        Row("T&itle", _title);
        Row("&Line under it", _subtitle);
        Row("&Size", _size);
        Row("Te&xt goes", _place);
        var picks = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        picks.Controls.AddRange(new Control[] { _picture, _color, _accent });
        form.Controls.Add(new Label());
        form.Controls.Add(picks);
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(320, 0), Margin = new Padding(0, 8, 0, 0),
            Text = "A screenshot of the mod in the game makes the best background. The old preview is kept, and the new one is shrunk under Steam's 1 MB if needed.",
        }.Role(ThemeRole.Muted);
        form.Controls.Add(new Label());
        form.Controls.Add(hint);

        var ok = new Button { Text = "&Use this image", AutoSize = true, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.OK }.Role(ThemeRole.Primary);
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Location = new Point(12, 12), Padding = new Padding(0, 0, 12, 12) };
        root.Controls.Add(_view, 0, 0);
        root.Controls.Add(form, 1, 0);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
        CancelButton = cancel;

        foreach (var t in new[] { _title, _subtitle }) t.TextChanged += (_, _) => Draw();
        _size.SelectedIndexChanged += (_, _) => Draw();
        _place.SelectedIndexChanged += (_, _) => Draw();
        _color.Click += (_, _) => { if (PickColor(_bg) is { } c) { _bg = c; _back?.Dispose(); _back = null; Draw(); } };
        _accent.Click += (_, _) => { if (PickColor(_fg) is { } c) { _fg = c; Draw(); } };
        _picture.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Title = "Background picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using var ms = new MemoryStream(File.ReadAllBytes(d.FileName));
                using var tmp = Image.FromStream(ms);
                _back?.Dispose();
                _back = new Bitmap(tmp);
                Draw();
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { MessageBox.Show(this, "That picture could not be opened: " + e.Message, Text); }
        };
        ok.Click += (_, _) => { Result = Render(); };
        Theme.Apply(this);
        Draw();
    }

    Color? PickColor(Color now)
    {
        using var d = new ColorDialog { Color = now, FullOpen = true };
        return d.ShowDialog(this) == DialogResult.OK ? d.Color : null;
    }

    Size Target => _size.SelectedIndex switch { 1 => new Size(1280, 720), 2 => new Size(512, 512), _ => new Size(640, 360) };

    void Draw()
    {
        var img = Render();
        _view.Size = Target.Width == Target.Height ? new Size(300, 300) : new Size(480, 270);
        _view.Image = img;
        _current?.Dispose();
        _current = img;
    }

    /// <summary>Draws the card at the chosen size.</summary>
    Bitmap Render()
    {
        var size = Target;
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(_bg);
        if (_back != null)
        {
            // Cover: fill the card, cutting off what sticks out.
            var scale = Math.Max(size.Width / (float)_back.Width, size.Height / (float)_back.Height);
            var w = _back.Width * scale;
            var h = _back.Height * scale;
            g.DrawImage(_back, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
        }
        var title = _title.Text.Trim();
        var sub = _subtitle.Text.Trim();
        if (title.Length == 0 && sub.Length == 0) return bmp;
        var pad = size.Width / 24f;
        using var titleFont = new Font("Segoe UI Semibold", size.Height / 7.5f, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", size.Height / 16f, GraphicsUnit.Pixel);
        var box = new RectangleF(pad, 0, size.Width - 2 * pad, size.Height);
        var fmt = new StringFormat { Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };
        var th = title.Length > 0 ? g.MeasureString(title, titleFont, (int)box.Width, fmt).Height : 0;
        var sh = sub.Length > 0 ? g.MeasureString(sub, subFont, (int)box.Width, fmt).Height : 0;
        var block = th + sh + pad;
        var top = _place.SelectedIndex switch { 1 => (size.Height - block) / 2, 2 => pad / 2, _ => size.Height - block - pad / 2 };
        // A shade behind the text, fading out, so it reads on any picture.
        var band = new RectangleF(0, Math.Max(0, top - pad), size.Width, Math.Min(size.Height, block + 2 * pad));
        using (var shade = new LinearGradientBrush(band, Color.FromArgb(_place.SelectedIndex == 2 ? 170 : 0, 0, 0, 0), Color.FromArgb(_place.SelectedIndex == 2 ? 0 : 170, 0, 0, 0), LinearGradientMode.Vertical))
            if (_back != null) g.FillRectangle(shade, band);
        using var fg = new SolidBrush(_fg);
        using var soft = new SolidBrush(Color.FromArgb(220, _fg));
        if (th > 0) g.DrawString(title, titleFont, fg, new RectangleF(box.X, top + pad / 2, box.Width, th), fmt);
        if (sh > 0) g.DrawString(sub, subFont, soft, new RectangleF(box.X, top + pad / 2 + th, box.Width, sh), fmt);
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _back?.Dispose(); _current?.Dispose(); }
        base.Dispose(disposing);
    }
}
