using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamCmdHelper;

/// <summary>The request form, and a mods folder's game (app ID) and packaging layout.</summary>
public sealed partial class MainForm
{
    /// <summary>The "?" button: a layout request, bug report or feature request, sent to Linear.</summary>
    void OpenRequest()
    {
        // Show one mod's file names (never contents) so the request says what a mod looks like.
        var mod = (_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag : _rows.Count > 0 ? _rows[0].Tag : null) as ModInfo;
        var names = mod != null ? RequestForm.ListFileNames(mod.Dir) : "";
        using var f = new RequestForm(_cfg.GameName, _cfg.AppId, _cfg.PresetName(), names);
        f.ShowDialog(this);
    }

    /// <summary>The user typed an app id: validate it, look the game name up on Steam, and save it to the layout file.</summary>
    async Task ApplyAppIdAsync()
    {
        if (_populating || _busy) return;
        if (_cfg.AppIdLocked) { _appId.Text = _cfg.AppId; return; }
        var id = _appId.Text.Trim();
        if (id == _cfg.AppId) return;
        if (id.Length > 0 && !id.All(char.IsDigit)) { Nudge(_appId, _projectNote, "The app ID is a number, e.g. 294100 for RimWorld (it is in the game's Steam store URL)."); return; }
        if (!Directory.Exists(_s.RepoPath)) { _appId.Text = _cfg.AppId; Nudge(_repo, _projectNote, "Pick the mods folder first; the app ID is saved in it."); return; }

        ShowNote(_projectNote, null);
        _cfg.AppId = id;
        _cfg.GameName = "";
        if (id.Length > 0) _cfg.GameName = await LookupGameNameAsync(id) ?? "";
        SaveLayout(id.Length > 0 ? $"App ID set to {id}{(_cfg.GameName.Length > 0 ? " (" + _cfg.GameName + ")" : "")}." : "App ID cleared.");
        ShowGame();
        UpdateSetup();
        UpdateGoButton();
    }

    void ApplyPresetChoice()
    {
        var name = _preset.SelectedItem as string ?? "";
        if (!Directory.Exists(_s.RepoPath)) { ShowGame(); Nudge(_repo, _projectNote, "Pick the mods folder first; the layout is saved in it."); return; }
        ShowNote(_projectNote, null);
        if (name == ProjectConfig.CustomName)
        {
            if (_cfg.PresetName() == ProjectConfig.CustomName) { ShowGame(); return; }
            _cfg.MakeCustom(); // keeps the current built-in layout's fields as the starting point
            SaveLayout("Layout set to Custom (editable).");
            ShowGame();
            return;
        }
        _cfg.ApplyPreset(name);
        SaveLayout($"Layout set to {name} (built in, locked).");
        RefreshAll();
    }

    void SaveLayout(string what)
    {
        try { _cfg.Save(_s.RepoPath); Append(what + " Saved to " + ProjectConfig.FileName + " in the repo.", LogKind.Note); }
        catch (Exception e) { Append("Could not save " + ProjectConfig.FileName + ": " + e.Message, LogKind.Error); }
    }

    static async Task<string?> LookupGameNameAsync(string appId)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId, out var e)) return null;
            if (!e.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
            return e.GetProperty("data").GetProperty("name").GetString();
        }
        catch { return null; }
    }

    void ShowGame()
    {
        _populating = true;
        _appId.Text = _cfg.AppId;
        var preset = _cfg.PresetName();
        _preset.SelectedItem = preset;
        _populating = false;
        if (!_cfg.AppIdValid)
        {
            _game.Text = preset == ProjectConfig.ParadoxName
                ? "Enter the game's Steam app ID: " + string.Join(", ", ProjectConfig.ParadoxGames.Select(g => $"{g.Game} {g.AppId}")) + "."
                : "Enter the game's Steam app ID (the number in its store URL) before uploading.";
            _game.ForeColor = Theme.Of(Tone.Warn);
        }
        else
        {
            _game.Text = $"{(_cfg.GameName.Length > 0 ? _cfg.GameName : "App " + _cfg.AppId)}  -  layout: {_cfg.Source}"
                         + (_cfg.IsBuiltIn ? " (locked; choose Custom to make your own)" : "");
            _game.ForeColor = Theme.Of(Tone.Muted);
        }
        Tip(_game, _game.Text); // full text when it is cut off with "..."
        ApplyLocks();
    }

    /// <summary>Built-in layouts are read-only: no editing the layout file, and no changing the app id when the layout fixes it.</summary>
    void ApplyLocks()
    {
        _editLayout.Enabled = !_busy && !_cfg.IsBuiltIn;
        _appId.Enabled = !_busy && !_cfg.AppIdLocked;
        Tip(_editLayout, _cfg.IsBuiltIn ? "Built-in layouts are locked; choose Custom in the Layout box to edit one" : "Open the layout file (" + ProjectConfig.FileName + ") in your editor");
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
