using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SteamCmdHelper;

/// <summary>What a control is for, which decides how the theme colours it. Unmarked controls get the default look.</summary>
public enum ThemeRole { None, Surface, Header, Divider, Muted, Title, Accent, Log, Keep }

/// <summary>Meaning of a status text; the theme picks a colour that reads well on its background.</summary>
public enum Tone { Normal, Muted, Ok, Warn, Error, Info }

public sealed record Palette(
    string Name, bool Dark,
    Color Back, Color Surface, Color Header, Color Input, Color Border,
    Color Text, Color Muted,
    Color Button, Color ButtonHover, Color ButtonDown,
    Color Accent, Color AccentHover, Color AccentText,
    Color Track, Color ListHeader,
    Color Ok, Color Warn, Color Error, Color Info);

/// <summary>
/// Dark (default) and light themes for the WinForms UI. <see cref="Apply"/> walks a control tree and colours it;
/// call it again after <see cref="Set"/> to switch live. The log keeps its console look in both themes.
/// </summary>
public static class Theme
{
    public const string DarkName = "Dark", LightName = "Light";

    public static readonly Palette DarkPalette = new(
        DarkName, true,
        Back: Color.FromArgb(30, 31, 36), Surface: Color.FromArgb(40, 42, 48), Header: Color.FromArgb(24, 25, 29),
        Input: Color.FromArgb(50, 52, 59), Border: Color.FromArgb(66, 69, 78),
        Text: Color.FromArgb(232, 233, 237), Muted: Color.FromArgb(152, 156, 168),
        Button: Color.FromArgb(55, 58, 66), ButtonHover: Color.FromArgb(68, 72, 82), ButtonDown: Color.FromArgb(80, 84, 96),
        Accent: Color.FromArgb(48, 132, 230), AccentHover: Color.FromArgb(70, 150, 240), AccentText: Color.White,
        Track: Color.FromArgb(56, 59, 67), ListHeader: Color.FromArgb(46, 48, 55),
        Ok: Color.FromArgb(98, 204, 132), Warn: Color.FromArgb(240, 178, 82), Error: Color.FromArgb(244, 112, 104), Info: Color.FromArgb(110, 180, 250));

    public static readonly Palette LightPalette = new(
        LightName, false,
        Back: Color.FromArgb(242, 243, 246), Surface: Color.White, Header: Color.White,
        Input: Color.White, Border: Color.FromArgb(206, 209, 216),
        Text: Color.FromArgb(28, 30, 34), Muted: Color.FromArgb(102, 107, 118),
        Button: Color.FromArgb(248, 249, 251), ButtonHover: Color.FromArgb(232, 235, 240), ButtonDown: Color.FromArgb(218, 222, 230),
        Accent: Color.FromArgb(0, 103, 192), AccentHover: Color.FromArgb(18, 120, 210), AccentText: Color.White,
        Track: Color.FromArgb(222, 225, 231), ListHeader: Color.FromArgb(246, 247, 249),
        Ok: Color.FromArgb(22, 128, 60), Warn: Color.FromArgb(178, 98, 0), Error: Color.FromArgb(196, 43, 28), Info: Color.FromArgb(0, 103, 192));

    /// <summary>Console colours for the log, the same in both themes.</summary>
    public static readonly Color LogBack = Color.FromArgb(20, 21, 25), LogText = Color.Gainsboro;

    public static Palette Current { get; private set; } = DarkPalette;

    /// <summary>Picks a theme by name; anything but "Light" is dark, so dark is the default.</summary>
    public static void Set(string? name) =>
        Current = string.Equals(name, LightName, StringComparison.OrdinalIgnoreCase) ? LightPalette : DarkPalette;

    /// <summary>Mixes <paramref name="amount"/> (0..1) of <paramref name="over"/> into <paramref name="under"/>.</summary>
    public static Color Blend(Color under, Color over, double amount) => Color.FromArgb(
        (int)(under.R + (over.R - under.R) * amount),
        (int)(under.G + (over.G - under.G) * amount),
        (int)(under.B + (over.B - under.B) * amount));

    /// <summary>The app icon (Assets/app.ico, embedded), shown on every window and on the taskbar.</summary>
    public static Icon? AppIcon { get; } = LoadIcon();

    static Icon? LoadIcon()
    {
        try
        {
            using var s = typeof(Theme).Assembly.GetManifestResourceStream("SteamCmdHelper.app.ico");
            return s == null ? null : new Icon(s);
        }
        catch { return null; }
    }

    public static Color Of(Tone t) => t switch
    {
        Tone.Muted => Current.Muted,
        Tone.Ok => Current.Ok,
        Tone.Warn => Current.Warn,
        Tone.Error => Current.Error,
        Tone.Info => Current.Info,
        _ => Current.Text,
    };

    static readonly ConditionalWeakTable<Control, object> Roles = new();

    /// <summary>Marks a control's role for <see cref="Apply"/>; returns it so it can be used inline.</summary>
    public static T Role<T>(this T c, ThemeRole role) where T : Control
    {
        Roles.AddOrUpdate(c, role);
        return c;
    }

    static ThemeRole RoleOf(Control c) => Roles.TryGetValue(c, out var r) ? (ThemeRole)r : ThemeRole.None;

    public static void Apply(Control root) => Apply(root, root.Parent?.BackColor ?? Current.Back);

    /// <summary>
    /// Every window is laid out in 96-DPI pixels. The first time it is themed, it is scaled to the screen's DPI (150%,
    /// 200%, ...), so fixed widths and heights grow with the text instead of clipping it. Only once: a theme switch
    /// themes the window again.
    /// </summary>
    static void ScaleForDpi(Form f)
    {
        if (f.AutoScaleMode == AutoScaleMode.Dpi) return;
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.PerformAutoScale();
    }

    static void Apply(Control c, Color parentBack)
    {
        var p = Current;
        var role = RoleOf(c);
        var back = role switch
        {
            ThemeRole.Surface => p.Surface,
            ThemeRole.Header => p.Header,
            ThemeRole.Divider => p.Border,
            _ => parentBack,
        };

        switch (c)
        {
            case Form f:
                ScaleForDpi(f);
                back = p.Back;
                f.BackColor = back; f.ForeColor = p.Text;
                if (AppIcon != null) f.Icon = AppIcon;
                Native(f, OnFormHandle);
                break;
            case NavButton nb:
                nb.FlatStyle = FlatStyle.Flat;
                nb.FlatAppearance.BorderSize = 0;
                nb.FlatAppearance.MouseOverBackColor = p.ButtonHover;
                nb.FlatAppearance.MouseDownBackColor = p.ButtonDown;
                nb.BackColor = back;
                nb.ForeColor = nb.Selected ? p.Text : p.Muted;
                nb.UseVisualStyleBackColor = false;
                nb.Invalidate();
                break;
            case Button b:
                StyleButton(b, role == ThemeRole.Accent);
                b.EnabledChanged -= OnButtonEnabled;
                b.EnabledChanged += OnButtonEnabled;
                b.Paint -= PaintDisabled;
                b.Paint += PaintDisabled;
                break;
            case RichTextBox r:
                r.BackColor = role == ThemeRole.Log ? LogBack : p.Input;
                r.ForeColor = role == ThemeRole.Log ? LogText : p.Text;
                Native(r, role == ThemeRole.Log ? OnDarkScrollHandle : OnScrollHandle);
                break;
            case TextBox t:
                t.BackColor = p.Input; t.ForeColor = p.Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                if (t.Multiline) Native(t, OnScrollHandle);
                break;
            case ComboBox cb:
                // Flat style alone leaves a drop-down list's box white; draw the items ourselves.
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = p.Input; cb.ForeColor = p.Text;
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = cb.Font.Height + 4;
                cb.DrawItem -= DrawComboItem; cb.DrawItem += DrawComboItem;
                cb.Invalidate();
                break;
            case ListView lv:
                lv.BackColor = p.Input; lv.ForeColor = p.Text;
                lv.BorderStyle = BorderStyle.None;
                lv.OwnerDraw = true;
                lv.DrawColumnHeader -= DrawHeader; lv.DrawColumnHeader += DrawHeader;
                lv.DrawItem -= DrawItemDefault; lv.DrawItem += DrawItemDefault;
                lv.DrawSubItem -= DrawSubItemDefault; lv.DrawSubItem += DrawSubItemDefault;
                Native(lv, OnScrollHandle);
                lv.Invalidate();
                break;
            case LinkLabel ll:
                ll.BackColor = back;
                if (role != ThemeRole.Keep) ll.LinkColor = p.Accent;
                ll.ActiveLinkColor = p.AccentHover;
                ll.VisitedLinkColor = ll.LinkColor;
                break;
            case Label l:
                l.BackColor = back;
                if (role != ThemeRole.Keep) l.ForeColor = role == ThemeRole.Muted ? p.Muted : p.Text;
                break;
            case CheckBox or RadioButton:
                c.BackColor = back; c.ForeColor = p.Text;
                break;
            case FlatProgressBar pb:
                pb.BackColor = p.Track; pb.ForeColor = p.Accent;
                break;
            default:
                c.BackColor = back; c.ForeColor = p.Text;
                break;
        }

        foreach (Control child in c.Controls) Apply(child, back);
    }

    static void StyleButton(Button b, bool accent)
    {
        var p = Current;
        accent &= b.Enabled; // a disabled main button should not look pressable
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = accent ? p.Accent : p.Border;
        b.FlatAppearance.MouseOverBackColor = accent ? p.AccentHover : p.ButtonHover;
        b.FlatAppearance.MouseDownBackColor = accent ? p.Accent : p.ButtonDown;
        b.BackColor = accent ? p.Accent : p.Button;
        b.ForeColor = accent ? p.AccentText : p.Text;
    }

    static void OnButtonEnabled(object? s, EventArgs e)
    {
        if (s is Button b) StyleButton(b, RoleOf(b) == ThemeRole.Accent);
    }

    /// <summary>
    /// Windows draws a disabled flat button's text in a fixed grey that almost vanishes on the dark theme. Paint it
    /// again in the theme's muted colour on a slightly sunken face, so it reads as "not now" but stays legible.
    /// </summary>
    static void PaintDisabled(object? s, PaintEventArgs e)
    {
        if (s is not Button b || b.Enabled) return;
        var p = Current;
        var r = b.ClientRectangle;
        using (var bg = new SolidBrush(Blend(p.Button, p.Surface, 0.5))) e.Graphics.FillRectangle(bg, r);
        using (var pen = new Pen(p.Border)) e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
        TextRenderer.DrawText(e.Graphics, b.Text, b.Font, r, p.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.HidePrefix);
    }

    /// <summary>Right-click menus in the theme's colours.</summary>
    public static void StyleMenu(ToolStrip menu)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(Current)) { RoundedEdges = false };
        menu.BackColor = Current.Surface;
        menu.ForeColor = Current.Text;
        foreach (ToolStripItem i in menu.Items) i.ForeColor = Current.Text;
    }

    sealed class MenuColors(Palette p) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => p.Surface;
        public override Color ImageMarginGradientBegin => p.Surface;
        public override Color ImageMarginGradientMiddle => p.Surface;
        public override Color ImageMarginGradientEnd => p.Surface;
        public override Color MenuBorder => p.Border;
        public override Color MenuItemBorder => p.ButtonHover;
        public override Color MenuItemSelected => p.ButtonHover;
        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Surface;
    }

    /// <summary>Tooltips are drawn in the theme's colours.</summary>
    public static void Style(ToolTip tip)
    {
        tip.OwnerDraw = true;
        tip.Draw -= DrawTip;
        tip.Draw += DrawTip;
    }

    static void DrawTip(object? s, DrawToolTipEventArgs e)
    {
        var p = Current;
        using (var bg = new SolidBrush(p.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
        using (var pen = new Pen(p.Border)) e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, p.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    static void DrawComboItem(object? s, DrawItemEventArgs e)
    {
        if (s is not ComboBox cb) return;
        var p = Current;
        var highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var bg = new SolidBrush(highlighted ? p.Accent : p.Input)) e.Graphics.FillRectangle(bg, e.Bounds);
        var text = e.Index >= 0 ? cb.GetItemText(cb.Items[e.Index]) : "";
        var color = highlighted ? p.AccentText : cb.Enabled ? p.Text : p.Muted;
        TextRenderer.DrawText(e.Graphics, text, cb.Font, Rectangle.Inflate(e.Bounds, -3, 0), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ------------------------------------------------------------ list view

    static void DrawHeader(object? s, DrawListViewColumnHeaderEventArgs e)
    {
        var p = Current;
        var font = (s as Control)?.Font ?? SystemFonts.DefaultFont;
        using (var bg = new SolidBrush(p.ListHeader)) e.Graphics.FillRectangle(bg, e.Bounds);
        using (var pen = new Pen(p.Border))
        {
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 5, e.Bounds.Right - 1, e.Bounds.Bottom - 6);
        }
        var align = e.Header?.TextAlign switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, font, Rectangle.Inflate(e.Bounds, -6, 0), p.Muted,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    static readonly ConditionalWeakTable<ListView, Func<DrawListViewSubItemEventArgs, bool>> Painters = new();

    /// <summary>Lets a list draw its own cells; <paramref name="paint"/> returns false to leave a cell to Windows.</summary>
    public static void PaintCells(this ListView lv, Func<DrawListViewSubItemEventArgs, bool> paint) => Painters.AddOrUpdate(lv, paint);

    static void DrawItemDefault(object? s, DrawListViewItemEventArgs e) =>
        e.DrawDefault = !(s is ListView lv && Painters.TryGetValue(lv, out _));

    static void DrawSubItemDefault(object? s, DrawListViewSubItemEventArgs e) =>
        e.DrawDefault = !(s is ListView lv && Painters.TryGetValue(lv, out var paint) && paint(e));

    // ------------------------------------------------------------ native: dark title bar and scroll bars

    /// <summary>Runs a native styling step now and every time the control's window is (re)created.</summary>
    static void Native(Control c, EventHandler h)
    {
        c.HandleCreated -= h;
        c.HandleCreated += h;
        if (c.IsHandleCreated) h(c, EventArgs.Empty);
    }

    static void OnFormHandle(object? s, EventArgs e)
    {
        if (s is not Form f || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int on = Current.Dark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; Windows 10 before 20H1 used 19.
        if (DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(f.Handle, 19, ref on, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Windows 11 can colour the caption itself: match the app's header bar so the window reads as one piece.
            int caption = ColorRef(Current.Header), text = ColorRef(Current.Text), border = ColorRef(Current.Border);
            DwmSetWindowAttribute(f.Handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(f.Handle, 36, ref text, sizeof(int));    // DWMWA_TEXT_COLOR
            DwmSetWindowAttribute(f.Handle, 34, ref border, sizeof(int));  // DWMWA_BORDER_COLOR
        }
        // A window that is already on screen keeps its old caption until the frame is redrawn (a live theme switch).
        const uint NoSize = 0x1, NoMove = 0x2, NoZOrder = 0x4, NoActivate = 0x10, FrameChanged = 0x20;
        SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, NoSize | NoMove | NoZOrder | NoActivate | FrameChanged);
    }

    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static void OnScrollHandle(object? s, EventArgs e) => SetScrollTheme(s as Control, Current.Dark);
    static void OnDarkScrollHandle(object? s, EventArgs e) => SetScrollTheme(s as Control, true);

    static void SetScrollTheme(Control? c, bool dark)
    {
        if (c == null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
        catch { /* cosmetic only */ }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}

/// <summary>A tab-like button for the top navigation; the selected one gets an accent underline.</summary>
public sealed class NavButton : Button
{
    bool _selected;

    public NavButton()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 6, 14, 8);
        Margin = new Padding(0);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            ForeColor = value ? Theme.Current.Text : Theme.Current.Muted;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_selected) return;
        using var b = new SolidBrush(Theme.Current.Accent);
        e.Graphics.FillRectangle(b, 8, Height - 3, Width - 16, 3);
    }
}

/// <summary>A flat progress bar that follows the theme (the native one ignores colours).</summary>
public sealed class FlatProgressBar : Control
{
    int _value;

    public FlatProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Height = 8;
        AccessibleRole = AccessibleRole.ProgressBar;
    }

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            AccessibleDescription = $"{v * 100 / Math.Max(1, Maximum - Minimum)}%";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var span = Math.Max(1, Maximum - Minimum);
        var w = (int)((long)Width * (_value - Minimum) / span);
        if (w <= 0) return;
        using var b = new SolidBrush(ForeColor);
        e.Graphics.FillRectangle(b, 0, 0, w, Height);
    }
}
