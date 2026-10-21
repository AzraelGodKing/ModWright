using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// Beside the Drop Pod's mod list: the selected mod's preview, who made it, its version and where it comes from, what
/// it needs and what needs it, which modlists and instances use it, your note on it, its description, and buttons for
/// its Workshop page and folder. The Drop Pod fills it; this only lays it out.
/// </summary>
public sealed class DropPodDetailsPanel : Panel
{
    readonly TableLayoutPanel _body = new() { Location = Point.Empty, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(12, 10, 12, 10) };
    readonly PictureBox _preview = new() { SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 10), AccessibleName = "Preview image" };
    readonly Label _name = new Label { AutoSize = true, Font = Theme.HeadingFont, UseMnemonic = false, Margin = new Padding(0, 0, 0, 2) }.Role(ThemeRole.Title);
    readonly Label _facts = new() { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) };
    readonly Label _note = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) }.Role(ThemeRole.Keep);
    readonly FlowLayoutPanel _actions = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 6) };
    readonly Label _desc = new Label { AutoSize = true, UseMnemonic = false }.Role(ThemeRole.Muted);
    readonly Dictionary<string, Image?> _images = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(Button Button, Func<ModManifest, bool> Applies)> _buttons = new();
    ModManifest? _mod;

    public DropPodDetailsPanel()
    {
        AutoScroll = true;
        AccessibleName = "Mod details";
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var c in new Control[] { _preview, _name, _facts, _note, _actions, _desc })
        {
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _body.Controls.Add(c);
        }
        Controls.Add(_body);
        Resize += (_, _) => FitWidth();
    }

    public ModManifest? Mod => _mod;

    public Button AddAction(string text, string tip, ToolTip tips, Action<ModManifest> run, Func<ModManifest, bool>? applies = null)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 0, 4, 0), Font = Theme.SmallFont, Margin = new Padding(0, 0, 6, 6) };
        b.Click += (_, _) => { if (_mod != null) run(_mod); };
        tips.SetToolTip(b, Localizer.T(tip));
        _buttons.Add((b, applies ?? (_ => true)));
        _actions.Controls.Add(b);
        return b;
    }

    /// <summary>Shows one mod; <paramref name="facts"/> are lines of "what: value".</summary>
    public void ShowMod(ModManifest m, IEnumerable<string> facts, string? note)
    {
        SuspendLayout();
        _mod = m;
        var img = m.PreviewPath != null ? Load(m.PreviewPath) : null;
        _preview.Image = img;
        _preview.Visible = img != null;
        _name.Text = m.DisplayName;
        _facts.Text = string.Join("\n", facts);
        _note.Text = note ?? "";
        _note.Visible = !string.IsNullOrEmpty(note);
        foreach (var (b, applies) in _buttons) b.Visible = applies(m);
        _actions.Visible = true;
        var d = m.Description.Trim();
        _desc.Text = d.Length > 1500 ? d[..1500] + "..." : d;
        _desc.Visible = d.Length > 0;
        FitWidth();
        ResumeLayout(true);
    }

    /// <summary>Shows a summary when no installed mod is selected.</summary>
    public void ShowSummary(string title, IEnumerable<string> lines)
    {
        SuspendLayout();
        _mod = null;
        _preview.Image = null;
        _preview.Visible = false;
        _name.Text = title;
        _facts.Text = string.Join("\n", lines);
        _note.Visible = _actions.Visible = _desc.Visible = false;
        FitWidth();
        ResumeLayout(true);
    }

    Image? Load(string path)
    {
        if (_images.TryGetValue(path, out var hit)) return hit;
        Image? img = null;
        try
        {
            // Read into memory so the file isn't kept locked while the player edits or deletes the mod.
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var tmp = Image.FromStream(ms);
            img = new Bitmap(tmp);
        }
        catch { }
        if (_images.Count > 200) { foreach (var i in _images.Values) i?.Dispose(); _images.Clear(); }
        _images[path] = img;
        return img;
    }

    void FitWidth()
    {
        var w = Math.Max(120, ClientSize.Width - _body.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        foreach (var l in new[] { _name, _facts, _note, _desc }) l.MaximumSize = new Size(w, 0);
        _actions.MaximumSize = new Size(w, 0);
        if (_preview.Image is { } img)
        {
            var pw = Math.Min(w, 360);
            _preview.Size = new Size(pw, Math.Max(40, pw * img.Height / Math.Max(1, img.Width)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var i in _images.Values) i?.Dispose();
        base.Dispose(disposing);
    }
}
