namespace SteamCmdHelper;

/// <summary>
/// Asked on the first start: use Modwright as a mod manager (The Drop Pod), a mod uploader, or both. Only the sides
/// picked are shown; Options > Appearance changes it later.
/// </summary>
public sealed class SidesForm : Form
{
    readonly List<(RadioButton Radio, string Name)> _choices = new();

    /// <summary>The picked choice, one of <see cref="Display.SidesChoices"/>.</summary>
    public string Choice => _choices.FirstOrDefault(c => c.Radio.Checked).Name ?? Display.Both;

    public SidesForm(string current)
    {
        Text = "Welcome to Modwright";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        if (Theme.AppIcon != null) Icon = Theme.AppIcon;

        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(20, 16), Padding = new Padding(0, 0, 20, 16) };
        root.Controls.Add(new Label { Text = "What do you want to use Modwright for?", AutoSize = true, Font = Theme.HeadingFont, Margin = new Padding(0, 0, 0, 4), UseMnemonic = false }.Role(ThemeRole.Title));
        root.Controls.Add(new Label { Text = "Only what you pick is shown. You can change it any time under Options > Appearance.", AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 0, 0, 12), UseMnemonic = false }.Role(ThemeRole.Muted));

        void Choice(string name, string text, string blurb)
        {
            var radio = new RadioButton { Text = text, AutoSize = true, Checked = name == Display.NormalizeSides(current), Margin = new Padding(0, 6, 0, 0), Font = new Font(Theme.BodyFont, FontStyle.Bold) };
            radio.DoubleClick += (_, _) => { radio.Checked = true; DialogResult = DialogResult.OK; };
            _choices.Add((radio, name));
            root.Controls.Add(radio);
            root.Controls.Add(new Label { Text = blurb, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(20, 0, 0, 6), UseMnemonic = false }.Role(ThemeRole.Muted));
        }
        Choice(Display.Manager, "Mod &manager (The Drop Pod)",
            "Pick, sort and check the RimWorld mods you play with, keep modlists and instances, and read the game's log.");
        Choice(Display.Uploader, "Mod &uploader",
            "Upload and update your own mods on the Steam Workshop, with change notes, tags and checks before each upload.");
        Choice(Display.Both, "&Both",
            "The mod manager and the mod uploader, each on its own tab.");

        var ok = new Button { Text = "Continue", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 2, 14, 2), Margin = new Padding(0, 14, 0, 0), Anchor = AnchorStyles.Right }.Role(ThemeRole.Primary);
        root.Controls.Add(ok);
        Controls.Add(root);
        AcceptButton = ok;
        Theme.Apply(this);
    }
}
