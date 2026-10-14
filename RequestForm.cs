using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SteamCmdHelper;

/// <summary>
/// "Request a game layout": collects a few details and opens a pre-filled Linear issue in the browser.
/// Nothing is sent from here; the person reviews the issue in Linear and creates it themselves.
/// Only people signed in to the Linear workspace can create it.
/// </summary>
public sealed class RequestForm : Form
{
    /// <summary>Linear's "new issue" link; query parameters pre-fill the issue (title, description, team).</summary>
    public const string NewIssueUrl = "https://linear.new";
    /// <summary>Linear team key the requests go to.</summary>
    public const string TeamKey = "AZR";
    const int MaxUrl = 7000; // browsers reject much longer URLs

    readonly TextBox _game = new() { Dock = DockStyle.Fill };
    readonly TextBox _appId = new() { Width = 120 };
    readonly TextBox _layout = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = new Font("Consolas", 9f) };
    readonly Label _note = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(560, 0) };

    public RequestForm(string gameName, string appId, string layoutName, string modFileNames)
    {
        Text = "Request a layout for another game";
        Width = 620; Height = 560; MinimumSize = new Size(520, 480);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

        _game.Text = gameName;
        _appId.Text = appId;
        _layout.Text = modFileNames.Length > 0
            ? "Here is what one mod folder looks like (file names only):\r\n\r\n" + modFileNames + "\r\n\r\nExtra notes (where the title, version and changelog live, how a mod is published in the game, ...):\r\n"
            : "Describe what one mod folder looks like: the files in it, where the title, version and changelog live, and how a mod is published in the game.\r\n";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0),
            Text = "Want this tool to support another game? Fill this in and it opens a new issue in Linear for you to review and create (you must be signed in to the AzraelGodKing Linear workspace). Nothing is sent from this window."
        }, 0, 0);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 8, 0, 4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Game", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
        top.Controls.Add(_game);
        top.Controls.Add(new Label { Text = "Steam app ID", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 8, 0) });
        top.Controls.Add(_appId);
        root.Controls.Add(top, 0, 1);

        root.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 4),
            Text = "The app ID is the number in the game's store URL, e.g. store.steampowered.com/app/294100/ is 294100."
        }, 0, 2);

        root.Controls.Add(_layout, 0, 3);

        _note.Text = "Only the text in the box above goes into the issue, and only after you create it in Linear. Edit or delete anything you don't want to share.";
        root.Controls.Add(_note, 0, 4);

        var open = new Button { Text = "Open Linear issue", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        var copy = new Button { Text = "Copy text instead", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(open);
        buttons.Controls.Add(copy);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
        CancelButton = cancel;
        AcceptButton = null;

        open.Click += (_, _) => OpenIssue(layoutName);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(BuildBody(layoutName)); MessageBox.Show(this, "Copied. Paste it into a new issue in Linear.", Text); }
            catch (Exception e) { MessageBox.Show(this, "Could not copy: " + e.Message, Text); }
        };
    }

    static string Version =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?").Split('+')[0];

    string BuildBody(string layoutName) =>
        $"Game: {_game.Text.Trim()}\r\nSteam app ID: {_appId.Text.Trim()}\r\nHelper version: {Version}\r\nLayout in use: {layoutName}\r\n\r\n{_layout.Text.Trim()}";

    void OpenIssue(string layoutName)
    {
        var game = _game.Text.Trim();
        var app = _appId.Text.Trim();
        if (game.Length == 0) { MessageBox.Show(this, "Enter the game's name.", Text); _game.Focus(); return; }
        if (app.Length > 0 && !app.All(char.IsDigit)) { MessageBox.Show(this, "The app ID is a number.", Text); _appId.Focus(); return; }

        var (url, cut) = BuildIssueUrl(game, app, layoutName, _layout.Text.Trim());

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            if (cut) MessageBox.Show(this, "The file list was long, so it was trimmed. Add the rest in the Linear issue if it matters.", Text);
            DialogResult = DialogResult.OK;
        }
        catch (Exception e)
        {
            try { Clipboard.SetText(BuildBody(layoutName)); } catch { }
            MessageBox.Show(this, "Could not open the browser (" + e.Message + "). The text was copied; paste it into a new issue in Linear.", Text);
        }
    }

    /// <summary>The pre-filled "new issue" address. The text is trimmed until the whole address fits (cut = true when that happened).</summary>
    public static (string url, bool cut) BuildIssueUrl(string game, string app, string layoutName, string layout)
    {
        string Url(string layoutText)
        {
            var description = $"**Game:** {game}\n**Steam app ID:** {app}\n**Helper:** {Version} (layout: {layoutName})\n\n### What does one mod look like?\n\n{layoutText}";
            return NewIssueUrl + "?team=" + TeamKey
                + "&title=" + Uri.EscapeDataString("Game layout request: " + game)
                + "&description=" + Uri.EscapeDataString(description.Replace("\r\n", "\n"));
        }

        var url = Url(layout);
        var cut = false;
        while (url.Length > MaxUrl && layout.Length > 200)
        {
            layout = layout.Substring(0, (int)(layout.Length * 0.8));
            cut = true;
            url = Url(layout + "\n\n[trimmed: paste the rest of the file list here]");
        }
        return (url, cut);
    }
    /// <summary>Relative file names (no contents) of one mod folder, to show in the form. Capped so the form stays readable.</summary>
    public static string ListFileNames(string modDir, int max = 40)
    {
        try
        {
            var files = Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(modDir, f).Replace('\\', '/'))
                .Where(f => !f.StartsWith(".git/") && !f.StartsWith("bin/") && !f.StartsWith("obj/"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var sb = new StringBuilder();
            foreach (var f in files.Take(max)) sb.AppendLine(f);
            if (files.Count > max) sb.AppendLine($"... and {files.Count - max} more files");
            return sb.ToString().TrimEnd();
        }
        catch { return ""; }
    }
}
