using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>The request form, the mods folder's game line, and the cards and rows the windows are built from.</summary>
public sealed partial class MainForm
{
    /// <summary>The "?" button: a bug report or feature request, sent to Linear.</summary>
    void OpenRequest()
    {
        using var f = new RequestForm();
        f.ShowDialog(this);
    }

    /// <summary>The line under the mods folder: the game and the layout, or why the layout file was not used.</summary>
    void ShowGame()
    {
        _game.Text = $"{_cfg.GameName} (app {_cfg.AppId})  -  {_cfg.Source}: mods with {_cfg.ModMarker}";
        _game.ForeColor = Theme.Of(Tone.Muted);
        Tip(_game, _game.Text); // full text when it is cut off with "..."
    }

    static Label Lbl(string t) => new() { Text = t, AutoSize = true };

    Label Hint(string t)
    {
        var h = new Label { Text = t, AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 2, 0, 2), UseMnemonic = false }.Role(ThemeRole.Muted);
        _hints.Add(h);
        return h;
    }

    /// <summary>A raised panel with an optional title (and controls to the right of it) over its rows.</summary>
    static TableLayoutPanel Card(string? title, Control[]? titleExtras, params Control[] rows)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 10) }.Role(ThemeRole.Surface);
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // as wide as its place, never wider
        if (title != null)
        {
            // The title takes the spare width, unless something next to it should (the Project card's summary).
            var extrasFill = titleExtras?.Any(c => (c.Tag as string) == "fill") == true;
            // In capitals with a fading accent line under it, like the section headings on Steam's own pages.
            var head = new Label { Text = title.ToUpperInvariant(), AccessibleName = Display.StripMnemonic(title), AutoSize = true, Font = Theme.SectionFont, Tag = extrasFill ? null : "fill", Margin = new Padding(0, 3, 16, 3) }.Role(ThemeRole.Title);
            var r = Row(new Control[] { head }.Concat(titleExtras ?? Array.Empty<Control>()).ToArray());
            r.Margin = new Padding(0, 0, 0, 2);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(r);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, Margin = new Padding(0, 0, 0, 8) }.Role(ThemeRole.Rule));
        }
        foreach (var c in rows)
        {
            var fill = c.Dock == DockStyle.Fill;
            card.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            card.Controls.Add(c);
        }
        return card;
    }

    static Button Btn(string text, EventHandler h)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };
        b.Click += h;
        return b;
    }

    /// <summary>A compact button for a card's title row.</summary>
    static Button SmallBtn(string text, EventHandler h)
    {
        var b = Btn(text, h);
        b.Padding = new Padding(4, 0, 4, 0);
        b.Font = Theme.SmallFont;
        return b;
    }

    static TableLayoutPanel Row(params Control[] cs)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = cs.Length, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
        string? label = null;
        foreach (var c in cs)
        {
            // Screen readers name an input by the label before it in its row ("Account name", "Theme", ...).
            if (c is Label { Text.Length: > 0 } l and not LinkLabel) label = Display.StripMnemonic(l.Text);
            else if (label != null && c is TextBox or ComboBox && string.IsNullOrEmpty(c.AccessibleName)) c.AccessibleName = label;
            var fill = (c.Tag as string) == "fill";
            t.ColumnStyles.Add(fill ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            c.Anchor = fill ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
            c.Margin = new Padding(0, 3, 8, 3);
            t.Controls.Add(c);
        }
        return t;
    }
}
