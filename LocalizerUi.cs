using System.Runtime.CompilerServices;

namespace SteamCmdHelper;

public static partial class Localizer
{
    static readonly ConditionalWeakTable<object, object> Hooked = new();

    /// <summary>
    /// Translates a window's text where the current language has it, and keeps doing so: text the program sets later,
    /// and controls added later, are translated as they appear. Text boxes and drop-downs are left alone (they hold what
    /// the user typed or picked). Safe to call again.
    /// </summary>
    public static void Apply(Control root)
    {
        if (Current.Length == 0) { Record(root); return; }
        Walk(root);
    }

    static void Record(Control c)
    {
        if (!Editable(c) && c.Text.Length > 0) lock (Seen) Seen.Add(c.Text);
        if (c.ContextMenuStrip != null) foreach (ToolStripItem i in c.ContextMenuStrip.Items) if (i.Text is { Length: > 0 } t) lock (Seen) Seen.Add(t);
        foreach (Control child in c.Controls) Record(child);
    }

    static bool Editable(Control c) => c is TextBoxBase or ComboBox or NumericUpDown or ListView or DataGridView or WebBrowser;

    static void Walk(Control c)
    {
        if (!Editable(c))
        {
            Fix(c);
            if (Hooked.TryAdd(c, c))
            {
                c.TextChanged += (_, _) => Fix(c);
                c.ControlAdded += (_, e) => { if (e.Control != null) Walk(e.Control); };
            }
        }
        else if (Hooked.TryAdd(c, c)) c.ControlAdded += (_, e) => { if (e.Control != null) Walk(e.Control); };
        if (c.ContextMenuStrip != null) Apply(c.ContextMenuStrip);
        if (c is ToolStrip strip) Apply(strip);
        foreach (Control child in c.Controls) Walk(child);
    }

    static void Fix(Control c)
    {
        var text = c.Text;
        if (text.Length == 0) return;
        if (Translates(text)) c.Text = T(text);
        else if (!_values.Contains(text)) lock (Seen) Seen.Add(text);
    }

    /// <summary>Translates a menu's items (and their sub-menus).</summary>
    public static void Apply(ToolStrip menu)
    {
        if (Current.Length == 0)
        {
            foreach (ToolStripItem i in menu.Items) if (i.Text is { Length: > 0 } t) lock (Seen) Seen.Add(t);
            return;
        }
        foreach (ToolStripItem i in menu.Items) Apply(i);
        if (Hooked.TryAdd(menu, menu)) menu.ItemAdded += (_, e) => { if (e.Item != null) Apply(e.Item); };
    }

    static void Apply(ToolStripItem i)
    {
        if (i.Text is { Length: > 0 } t && Translates(t)) i.Text = T(t);
        if (i.ToolTipText is { Length: > 0 } tip && Translates(tip)) i.ToolTipText = T(tip);
        if (i is ToolStripDropDownItem d && d.HasDropDownItems) Apply(d.DropDown);
    }
}
