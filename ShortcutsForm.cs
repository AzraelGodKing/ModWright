namespace SteamCmdHelper;

/// <summary>Every keyboard shortcut in one place (F1, or ? > Keyboard shortcuts).</summary>
public sealed class ShortcutsForm : Form
{
    public static readonly (string Keys, string What)[] All =
    {
        ("F1", "Show this list"),
        ("Alt + underlined letter", "Press that button, or go to the box whose label has the letter underlined"),
        ("Alt+U, Alt+O", "Go to the Upload or Options tab"),
        ("Ctrl+Tab", "Switch between the Upload and Options tabs"),
        ("Ctrl+PageDown, Ctrl+PageUp", "Next or previous Options section (Up and Down in the section list too)"),
        ("Ctrl+O", "Pick the mods folder"),
        ("Ctrl+N", "Make a new mod folder"),
        ("F5", "Read the mods folder again"),
        ("Ctrl+F", "Filter the mod list (Esc clears the filter)"),
        ("Space", "Tick or untick the selected mod"),
        ("Enter", "Edit the selected mod's Workshop page"),
        ("F2", "Change the selected mod's version to upload"),
        ("Shift+F10", "More for the selected mod: changelog, history, package preview, Steam page"),
        ("Ctrl+Enter", "Upload the ticked mods"),
        ("Esc", "Stop a running upload"),
    };

    public ShortcutsForm()
    {
        Text = "Keyboard shortcuts";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 12, 16, 12);

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Padding = new Padding(12, 10, 12, 10) }.Role(ThemeRole.Surface);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.AccessibleName = "Keyboard shortcuts";
        foreach (var (keys, what) in All)
        {
            table.Controls.Add(new Label { Text = keys, AutoSize = true, Font = new Font(Theme.BodyFont, FontStyle.Bold), Margin = new Padding(0, 4, 24, 4), UseMnemonic = false });
            table.Controls.Add(new Label { Text = what, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 4, 0, 4), UseMnemonic = false });
        }

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 2, 14, 2), Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) }.Role(ThemeRole.Accent);
        var intro = new Label { Text = "Everything in SteamCMD Helper works from the keyboard.", AutoSize = true, Margin = new Padding(0, 0, 0, 8) }.Role(ThemeRole.Muted);
        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(16, 12) };
        table.Dock = DockStyle.None;
        table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        root.Controls.Add(intro);
        root.Controls.Add(table);
        root.Controls.Add(close);
        Controls.Add(root);
        AcceptButton = CancelButton = close;
        Theme.Apply(this);
    }
}
