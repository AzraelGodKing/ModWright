using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>Inline notes under a card instead of message boxes.</summary>
public sealed partial class MainForm
{
    /// <summary>Shows <paramref name="text"/> in a note line under a card (warning colour), or hides the line when null.</summary>
    void ShowNote(Label note, string? text)
    {
        note.Text = text ?? "";
        note.Visible = !string.IsNullOrEmpty(text);
        note.ForeColor = Theme.Of(Tone.Warn);
        Tip(note, note.Text);
    }

    /// <summary>
    /// Says what is missing next to where it is fixed, instead of a pop-up: switches to the right tab, shows the note,
    /// puts the cursor on <paramref name="target"/> and briefly lights it up.
    /// </summary>
    void Nudge(Control target, Label note, string text)
    {
        if (note == _accountNote) ShowOptions(target); else ShowPage(_uploadPage);
        if (note == _projectNote && !_projectOpen) { _projectOpen = true; UpdateProjectCard(); }
        ShowNote(note, text);
        Say(text);
        if (target.CanFocus) target.Focus();
        Flash(target);
    }

    readonly HashSet<Control> _flashing = new();

    async void Flash(Control c)
    {
        if (!_flashing.Add(c)) return;
        var old = c.BackColor;
        try
        {
            for (var i = 0; i < 2; i++)
            {
                c.BackColor = Theme.Blend(old, Theme.Of(Tone.Warn), 0.45);
                await Task.Delay(220);
                if (c.IsDisposed) return;
                c.BackColor = old;
                await Task.Delay(160);
                if (c.IsDisposed) return;
            }
        }
        finally
        {
            _flashing.Remove(c);
            if (!c.IsDisposed) c.BackColor = old;
        }
    }
}
