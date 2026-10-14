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
                back = p.Back;
                f.BackColor = back; f.ForeColor = p.Text;
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
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = p.Input; cb.ForeColor = p.Text;
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

    static void DrawItemDefault(object? s, DrawListViewItemEventArgs e) => e.DrawDefault = true;
    static void DrawSubItemDefault(object? s, DrawListViewSubItemEventArgs e) => e.DrawDefault = true;

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
    }

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
