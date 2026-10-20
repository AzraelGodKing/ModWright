using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SteamCmdHelper;

/// <summary>
/// The "?" form: ask for a layout for another game, report a bug or ask for a feature. Builds with a request key send
/// it straight to Linear (labelled by kind and area, see <see cref="RequestSender"/>); other builds open a pre-filled
/// Linear issue in the browser, which only people signed in to the Linear workspace can create.
/// </summary>
public sealed class RequestForm : Form
{
    /// <summary>Linear's "new issue" link; query parameters pre-fill the issue (title, description, team).</summary>
    public const string NewIssueUrl = "https://linear.new";
    /// <summary>Linear team key the requests go to.</summary>
    public const string TeamKey = "AZR";
    const int MaxUrl = 7000; // browsers reject much longer URLs

    static readonly (RequestSender.RequestKind kind, string text)[] Kinds =
    {
        (RequestSender.RequestKind.Layout, "Support another game (layout request)"),
        (RequestSender.RequestKind.Bug, "Report a bug"),
        (RequestSender.RequestKind.Feature, "Suggest a feature"),
    };
    const string AnyArea = "Not sure / other";

    // The drop-downs share the row's width, down to these sizes; the window is widened to fit them (see OnLoad).
    readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Width = 230, MinimumSize = new Size(230, 0) };
    readonly ComboBox _area = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Width = 160, MinimumSize = new Size(160, 0) };
    readonly Label _areaLabel = new() { Text = "About", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 8, 0) };
    readonly TextBox _summary = new() { Dock = DockStyle.Fill };
    readonly TableLayoutPanel _summaryRow = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 4, 0, 0) };
    readonly TextBox _game = new() { Dock = DockStyle.Fill };
    readonly TextBox _appId = new() { Width = 120 };
    readonly Label _appIdHint = new Label
    {
        AutoSize = true, Margin = new Padding(0, 0, 0, 4),
        Text = "The app ID is the number in the game's store URL, e.g. store.steampowered.com/app/294100/ is 294100."
    }.Role(ThemeRole.Muted);
    readonly TextBox _details = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = new Font("Consolas", 9f) };
    readonly Label _intro = new() { AutoSize = true };
    readonly Label _note = new Label { AutoSize = true }.Role(ThemeRole.Muted);
    readonly TableLayoutPanel _root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
    readonly TableLayoutPanel _type = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 8, 0, 0) };
    readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    readonly string _modFileNames;
    readonly bool _direct;
    readonly HashSet<string> _templates = new();

    public RequestForm(string gameName, string appId, string layoutName, string modFileNames)
    {
        _direct = RequestSender.Available; // this build can send requests itself (no browser, no login)
        _modFileNames = modFileNames;
        Text = "Send a request";
        Width = 720; Height = 600; MinimumSize = new Size(560, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

        _game.Text = gameName;
        _appId.Text = appId;
        foreach (var (_, text) in Kinds) _kind.Items.Add(text);
        _area.Items.Add(AnyArea);
        foreach (var (_, text) in RequestSender.Areas) _area.Items.Add(text);
        _area.SelectedIndex = 0;

        var root = _root;
        // One column as wide as the window, so no row can push past its right edge and be cut off.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++) root.RowStyles.Add(i == 5 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

        root.Controls.Add(_intro, 0, 0);

        var type = _type;
        type.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        type.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        type.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        type.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        type.Controls.Add(new Label { Text = "Request", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
        type.Controls.Add(_kind);
        type.Controls.Add(_areaLabel);
        type.Controls.Add(_area);
        root.Controls.Add(type, 0, 1);

        _summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _summaryRow.Controls.Add(new Label { Text = "Summary", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
        _summaryRow.Controls.Add(_summary);
        root.Controls.Add(_summaryRow, 0, 2);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 4, 0, 4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Game", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
        top.Controls.Add(_game);
        top.Controls.Add(new Label { Text = "Steam app ID", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 8, 0) });
        top.Controls.Add(_appId);
        root.Controls.Add(top, 0, 3);
        root.Controls.Add(_appIdHint, 0, 4);
        root.Controls.Add(_details, 0, 5);
        root.Controls.Add(_note, 0, 6);

        var open = new Button { Text = _direct ? "Send request" : "Open Linear issue", AutoSize = true, Padding = new Padding(8, 2, 8, 2) }.Role(ThemeRole.Accent);
        var copy = new Button { Text = "Copy text instead", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = _buttons;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(open);
        buttons.Controls.Add(copy);
        root.Controls.Add(buttons, 0, 7);
        Controls.Add(root);
        root.SizeChanged += (_, _) => WrapText();
        CancelButton = cancel;
        AcceptButton = null;

        _kind.SelectedIndexChanged += (_, _) => ShowKind();
        _kind.SelectedIndex = 0;
        Theme.Apply(this);

        open.Click += async (_, _) => { if (_direct) await SendRequestAsync(layoutName, open); else OpenIssue(layoutName); };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(BuildBody(layoutName)); MessageBox.Show(this, "Copied. Paste it into a new issue in Linear.", Text); }
            catch (Exception e) { MessageBox.Show(this, "Could not copy: " + e.Message, Text); }
        };
    }

    /// <summary>
    /// After scaling for the screen and text size: widens the window until the request row and the buttons fit,
    /// so nothing is cut off at the right, and makes that the smallest it can be dragged to.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        var chrome = Width - ClientSize.Width + _root.Padding.Horizontal;
        var need = Math.Max(_type.GetPreferredSize(Size.Empty).Width + _type.Margin.Horizontal, _buttons.GetPreferredSize(Size.Empty).Width + _buttons.Margin.Horizontal) + chrome;
        var screen = Screen.FromControl(this).WorkingArea.Width;
        need = Math.Min(need, screen);
        if (MinimumSize.Width < need) MinimumSize = new Size(need, MinimumSize.Height);
        if (Width < need) Width = need;
        WrapText();
        base.OnLoad(e); // after the resize, so the window is centred at its final size
    }

    /// <summary>Wraps the explanations at the window's width instead of a fixed one.</summary>
    void WrapText()
    {
        var width = Math.Max(100, _root.ClientSize.Width - _root.Padding.Horizontal - 4);
        foreach (var l in new[] { _intro, _note, _appIdHint }) l.MaximumSize = new Size(width, 0);
    }

    RequestSender.RequestKind Kind => Kinds[Math.Max(0, _kind.SelectedIndex)].kind;

    /// <summary>Opens the form as a filled-in request, e.g. a bug report with a crash report as its details.</summary>
    public void Prefill(RequestSender.RequestKind kind, string summary, string details)
    {
        _kind.SelectedIndex = Array.FindIndex(Kinds, k => k.kind == kind);
        _summary.Text = summary;
        _details.Text = details.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    /// <summary>The selected area's label-map key, or null for "not sure" (and for layout requests).</summary>
    string? AreaKey => Kind != RequestSender.RequestKind.Layout && _area.SelectedIndex > 0 ? RequestSender.Areas[_area.SelectedIndex - 1].key : null;

    string AreaText => _area.SelectedIndex > 0 ? RequestSender.Areas[_area.SelectedIndex - 1].text : AnyArea;

    /// <summary>Shows the fields for the selected kind and swaps the starter text, unless the person already wrote their own.</summary>
    void ShowKind()
    {
        var layout = Kind == RequestSender.RequestKind.Layout;
        _summaryRow.Visible = !layout;
        _areaLabel.Visible = _area.Visible = !layout;
        _appIdHint.Visible = layout;

        var what = Kind switch
        {
            RequestSender.RequestKind.Bug => "Found something that doesn't work? Describe it below.",
            RequestSender.RequestKind.Feature => "Have an idea that would make this tool better? Describe it below.",
            _ => "Want this tool to support another game? Fill this in.",
        };
        _intro.Text = what + (_direct
            ? " Press Send request and it goes straight to the developer over the internet; you do not need any account."
            : " It opens a new issue in Linear for you to review and create (you must be signed in to the AzraelGodKing Linear workspace). Nothing is sent from this window.");
        _note.Text = _direct
            ? "Pressing Send request creates an issue with exactly the details shown above, plus this program's version and layout. Nothing else is sent. Edit or delete anything you don't want to share."
            : "Only the details above go into the issue, and only after you create it in Linear. Edit or delete anything you don't want to share.";

        var current = _details.Text.Trim();
        if (current.Length == 0 || _templates.Contains(current))
        {
            _details.Text = Template(Kind, _modFileNames);
            _templates.Add(_details.Text.Trim());
        }
    }

    /// <summary>Starter text for the details box.</summary>
    public static string Template(RequestSender.RequestKind kind, string modFileNames) => kind switch
    {
        RequestSender.RequestKind.Bug => "What happened:\r\n\r\nWhat you expected:\r\n\r\nSteps to make it happen again:\r\n1. \r\n",
        RequestSender.RequestKind.Feature => "What would you like the tool to do?\r\n\r\nWhy would it help?\r\n",
        _ => modFileNames.Length > 0
            ? "Here is what one mod folder looks like (file names only):\r\n\r\n" + modFileNames + "\r\n\r\nExtra notes (where the title, version and changelog live, how a mod is published in the game, ...):\r\n"
            : "Describe what one mod folder looks like: the files in it, where the title, version and changelog live, and how a mod is published in the game.\r\n",
    };

    static string Version =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?").Split('+')[0];

    string BuildBody(string layoutName) =>
        (Title() + "\n\n" + BuildDescription(Kind, _game.Text.Trim(), _appId.Text.Trim(), layoutName, AreaText, _details.Text.Trim())).Replace("\n", "\r\n");

    string Title() => BuildTitle(Kind, _game.Text.Trim(), _summary.Text.Trim());

    /// <summary>The issue title for a request.</summary>
    public static string BuildTitle(RequestSender.RequestKind kind, string game, string summary) => kind switch
    {
        RequestSender.RequestKind.Bug => "Bug: " + summary,
        RequestSender.RequestKind.Feature => "Feature request: " + summary,
        _ => "Game layout request: " + game,
    };

    /// <summary>Checks the fields; shows a message and returns false when something required is missing.</summary>
    bool CheckFields(out string game, out string app)
    {
        game = _game.Text.Trim();
        app = _appId.Text.Trim();
        if (Kind == RequestSender.RequestKind.Layout && game.Length == 0) { MessageBox.Show(this, "Enter the game's name.", Text); _game.Focus(); return false; }
        if (Kind != RequestSender.RequestKind.Layout && _summary.Text.Trim().Length == 0) { MessageBox.Show(this, "Enter a one-line summary.", Text); _summary.Focus(); return false; }
        if (app.Length > 0 && !app.All(char.IsDigit)) { MessageBox.Show(this, "The app ID is a number.", Text); _appId.Focus(); return false; }
        return true;
    }

    void OpenIssue(string layoutName)
    {
        if (!CheckFields(out var game, out var app)) return;
        var (url, cut) = BuildIssueUrl(game, app, layoutName, _details.Text.Trim(), Kind, _summary.Text.Trim(), AreaText);

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            if (cut) MessageBox.Show(this, "The text was long, so it was trimmed. Add the rest in the Linear issue if it matters.", Text);
            DialogResult = DialogResult.OK;
        }
        catch (Exception e)
        {
            try { Clipboard.SetText(BuildBody(layoutName)); } catch { }
            MessageBox.Show(this, "Could not open the browser (" + e.Message + "). The text was copied; paste it into a new issue in Linear.", Text);
        }
    }

    /// <summary>The layout-request issue text (Markdown).</summary>
    public static string BuildDescription(string game, string app, string layoutName, string layout) =>
        BuildDescription(RequestSender.RequestKind.Layout, game, app, layoutName, AnyArea, layout);

    /// <summary>The issue text (Markdown) shared by the pre-filled link and the direct send.</summary>
    public static string BuildDescription(RequestSender.RequestKind kind, string game, string app, string layoutName, string area, string details) =>
        (kind == RequestSender.RequestKind.Layout
            ? $"**Game:** {game}\n**Steam app ID:** {app}\n**Helper:** {Version} (layout: {layoutName})\n\n### What does one mod look like?\n\n{details}"
            : $"**Type:** {(kind == RequestSender.RequestKind.Bug ? "Bug report" : "Feature request")}\n**About:** {area}\n**Game:** {(game.Length > 0 ? game : "-")}"
              + $"{(app.Length > 0 ? " (app " + app + ")" : "")}\n**Helper:** {Version} (layout: {layoutName})\n\n### Details\n\n{details}")
        .Replace("\r\n", "\n");

    static DateTime _lastSend = DateTime.MinValue;

    async Task SendRequestAsync(string layoutName, Button button)
    {
        if (!CheckFields(out var game, out var app)) return;
        if (DateTime.UtcNow - _lastSend < TimeSpan.FromSeconds(60)) { MessageBox.Show(this, "A request was just sent. Please wait a minute before sending another.", Text); return; }

        button.Enabled = false;
        var oldText = button.Text; button.Text = "Sending...";
        try
        {
            var description = BuildDescription(Kind, game, app, layoutName, AreaText, _details.Text.Trim());
            var (ok, message) = await RequestSender.SendAsync(Title(), description, Kind, AreaKey);
            if (ok)
            {
                _lastSend = DateTime.UtcNow;
                MessageBox.Show(this, $"Thanks! Your request was sent (reference {message}).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                return;
            }
            try { Clipboard.SetText(BuildBody(layoutName)); } catch { }
            MessageBox.Show(this, message + "\n\nThe request text was copied to your clipboard so you can send it another way.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) { button.Text = oldText; button.Enabled = true; } }
    }

    /// <summary>The pre-filled "new issue" address. The text is trimmed until the whole address fits (cut = true when that happened).</summary>
    public static (string url, bool cut) BuildIssueUrl(string game, string app, string layoutName, string details,
        RequestSender.RequestKind kind = RequestSender.RequestKind.Layout, string summary = "", string area = AnyArea)
    {
        string Url(string text)
        {
            var description = BuildDescription(kind, game, app, layoutName, area, text);
            return NewIssueUrl + "?team=" + TeamKey
                + "&title=" + Uri.EscapeDataString(BuildTitle(kind, game, summary))
                + "&description=" + Uri.EscapeDataString(description.Replace("\r\n", "\n"));
        }

        var url = Url(details);
        var cut = false;
        while (url.Length > MaxUrl && details.Length > 200)
        {
            details = details.Substring(0, (int)(details.Length * 0.8));
            cut = true;
            url = Url(details + "\n\n[trimmed: paste the rest here]");
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
