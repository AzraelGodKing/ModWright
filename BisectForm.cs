using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// Drop Pod > Tools > Find the broken mod: each round puts half the suspects in the game's mod list and starts the game;
/// the player says whether the problem is still there. Stays open beside the game (it isn't modal), and puts the
/// player's own list back at the end.
/// </summary>
public sealed class BisectForm : Form
{
    readonly Bisect _b;
    readonly Func<string, string> _name;
    readonly Func<IReadOnlyList<string>, bool> _start;
    readonly Action<string?, bool> _finish;
    readonly Label _round = new Label { AutoSize = true, Font = Theme.HeadingFont, UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) }.Role(ThemeRole.Title);
    readonly Label _text = new() { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 10) };
    readonly Button _play = new() { Text = "&Start the game with this half", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    readonly Button _broken = new() { Text = "Still &broken", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Enabled = false };
    readonly Button _works = new() { Text = "It &works now", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Enabled = false };
    readonly Button _untick = new() { Text = "&Untick it and put my list back", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Visible = false };
    readonly Button _stop = new() { Text = "Stop and put my &list back", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    bool _finished;

    /// <param name="start">Writes the round's mods to the game and starts it; false when it couldn't.</param>
    /// <param name="finish">Puts the player's list back; with true, without the culprit.</param>
    public BisectForm(Bisect b, Func<string, string> name, Func<IReadOnlyList<string>, bool> start, Action<string?, bool> finish)
    {
        _b = b;
        _name = name;
        _start = start;
        _finish = finish;
        Text = "Find the broken mod";
        CancelButton = _stop; // Esc puts the list back, like the button
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = true;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, 10),
            Text = "Each round puts half of the suspects (with what they need, and always the game and DLC) in RimWorld's mod list. " +
                   "Start the game, check for the problem (red errors, a crash, something broken), close the game, then say how it went.",
        }.Role(ThemeRole.Muted);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(580, 0) };
        buttons.Controls.AddRange(new Control[] { _play, _broken, _works, _untick, _stop });
        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Location = new Point(16, 12), Padding = new Padding(0, 0, 16, 12) };
        foreach (var c in new Control[] { intro, _round, _text, buttons }) root.Controls.Add(c);
        Controls.Add(root);
        _play.Role(ThemeRole.Primary);

        _play.Click += (_, _) =>
        {
            if (!_start(_b.Trial())) return;
            _broken.Enabled = _works.Enabled = true;
            _text.Text += "\n\nThe game is starting. When you've looked, close it and answer.";
        };
        _broken.Click += (_, _) => Answer(BisectAnswer.Broken);
        _works.Click += (_, _) => Answer(BisectAnswer.Works);
        _untick.Click += (_, _) => { _finished = true; _finish(_b.Culprit, true); Close(); };
        _stop.Click += (_, _) => Close();
        FormClosing += (_, _) => { if (!_finished) { _finished = true; _finish(null, false); } };
        ShowRound();
        Theme.Apply(this);
    }

    void Answer(BisectAnswer a)
    {
        _b.Answer(a);
        _broken.Enabled = _works.Enabled = false;
        ShowRound();
    }

    void ShowRound()
    {
        if (_b.Done)
        {
            var c = _b.Culprit;
            _round.Text = c == null ? "Nothing left to try" : "Found it: " + _name(c);
            _text.Text = c == null ? "No suspect is left. The problem may come from the game itself, or from more than one mod together." :
                _b.AlsoNeeded.Count > 0
                    ? $"{_name(c)} only breaks together with something in this group, which had to stay on: {string.Join(", ", _b.AlsoNeeded.Take(10).Select(_name))}{(_b.AlsoNeeded.Count > 10 ? ", ..." : "")}."
                    : $"With {_name(c)} on the problem shows; without it, it doesn't. Report it to the mod's author (Read the log > Report to the author), or leave it off.";
            _play.Visible = _broken.Visible = _works.Visible = false;
            _untick.Visible = c != null;
            _stop.Text = "Put my &list back as it was";
            return;
        }
        var (trying, left) = _b.Size;
        _round.Text = $"Round {_b.Round}: trying {trying} of {left} suspects";
        _text.Text = $"About {_b.RoundsLeft} round{(_b.RoundsLeft == 1 ? "" : "s")} to go." + (_b.AlsoNeeded.Count > 0 ? $" {_b.AlsoNeeded.Count} mods stay on because the problem needs one of them." : "");
    }
}
