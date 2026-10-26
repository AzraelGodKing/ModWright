using System.Diagnostics;
using System.Reflection;

namespace SteamCmdHelper;

/// <summary>
/// The "?" form: report a bug or ask for a feature. Builds with a request key send
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
    readonly TextBox _details = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = new Font("Consolas", 9f) };
    readonly Label _intro = new() { AutoSize = true };
    readonly Label _note = new Label { AutoSize = true }.Role(ThemeRole.Muted);
    readonly TableLayoutPanel _root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
    readonly TableLayoutPanel _type = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 8, 0, 0) };
    readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    readonly bool _direct;
    readonly HashSet<string> _templates = new();

    public RequestForm()
    {
        _direct = RequestSender.Available; // this build can send requests itself (no browser, no login)
        Text = "Send a request";
        Width = 720; Height = 600; MinimumSize = new Size(560, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

        foreach (var (_, text) in Kinds) _kind.Items.Add(text);
        _area.Items.Add(AnyArea);
        foreach (var (_, text) in RequestSender.Areas) _area.Items.Add(text);
        _area.SelectedIndex = 0;

        var root = _root;
        // One column as wide as the window, so no row can push past its right edge and be cut off.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) root.RowStyles.Add(i == 3 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

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

        root.Controls.Add(_details, 0, 3);
        root.Controls.Add(_note, 0, 4);

        var open = new Button { Text = _direct ? "Send request" : "Open Linear issue", AutoSize = true, Padding = new Padding(8, 2, 8, 2) }.Role(ThemeRole.Accent);
        var copy = new Button { Text = "Copy text instead", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
        var buttons = _buttons;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(open);
        buttons.Controls.Add(copy);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
        root.SizeChanged += (_, _) => WrapText();
        CancelButton = cancel;
        AcceptButton = null;

        _kind.SelectedIndexChanged += (_, _) => ShowKind();
        _kind.SelectedIndex = 0;
        Theme.Apply(this);

        open.Click += async (_, _) => { if (_direct) await SendRequestAsync(open); else OpenIssue(); };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(BuildBody()); MessageBox.Show(this, "Copied. Paste it into a new issue in Linear.", Text); }
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
        foreach (var l in new[] { _intro, _note }) l.MaximumSize = new Size(width, 0);
    }

    RequestSender.RequestKind Kind => Kinds[Math.Max(0, _kind.SelectedIndex)].kind;

    /// <summary>Opens the form as a filled-in request, e.g. a bug report with a crash report as its details.</summary>
    public void Prefill(RequestSender.RequestKind kind, string summary, string details)
    {
        _kind.SelectedIndex = Array.FindIndex(Kinds, k => k.kind == kind);
        _summary.Text = summary;
        _details.Text = details.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    /// <summary>The selected area's label-map key, or null for "not sure".</summary>
    string? AreaKey => _area.SelectedIndex > 0 ? RequestSender.Areas[_area.SelectedIndex - 1].key : null;

    string AreaText => _area.SelectedIndex > 0 ? RequestSender.Areas[_area.SelectedIndex - 1].text : AnyArea;

    /// <summary>Swaps the explanation and the starter text for the selected kind, unless the person already wrote their own.</summary>
    void ShowKind()
    {
        var what = Kind == RequestSender.RequestKind.Bug
            ? "Found something that doesn't work? Describe it below."
            : "Have an idea that would make this tool better? Describe it below.";
        _intro.Text = what + (_direct
            ? " Press Send request and it goes straight to the developer over the internet; you do not need any account."
            : " It opens a new issue in Linear for you to review and create (you must be signed in to the AzraelGodKing Linear workspace). Nothing is sent from this window.");
        _note.Text = _direct
            ? "Pressing Send request creates an issue with exactly the details shown above, plus this program's version. Nothing else is sent. Edit or delete anything you don't want to share."
            : "Only the details above go into the issue, and only after you create it in Linear. Edit or delete anything you don't want to share.";

        var current = _details.Text.Trim();
        if (current.Length == 0 || _templates.Contains(current))
        {
            _details.Text = Template(Kind);
            _templates.Add(_details.Text.Trim());
        }
    }

    /// <summary>Starter text for the details box.</summary>
    public static string Template(RequestSender.RequestKind kind) => kind == RequestSender.RequestKind.Bug
        ? "What happened:\r\n\r\nWhat you expected:\r\n\r\nSteps to make it happen again:\r\n1. \r\n"
        : "What would you like the tool to do?\r\n\r\nWhy would it help?\r\n";

    static string Version =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?").Split('+')[0];

    string BuildBody() =>
        (Title() + "\n\n" + BuildDescription(Kind, AreaText, _details.Text.Trim())).Replace("\n", "\r\n");

    string Title() => BuildTitle(Kind, _summary.Text.Trim());

    /// <summary>The issue title for a request.</summary>
    public static string BuildTitle(RequestSender.RequestKind kind, string summary) =>
        (kind == RequestSender.RequestKind.Bug ? "Bug: " : "Feature request: ") + summary;

    /// <summary>Checks the fields; shows a message and returns false when something required is missing.</summary>
    bool CheckFields()
    {
        if (_summary.Text.Trim().Length == 0) { MessageBox.Show(this, "Enter a one-line summary.", Text); _summary.Focus(); return false; }
        return true;
    }

    void OpenIssue()
    {
        if (!CheckFields()) return;
        var (url, cut) = BuildIssueUrl(_details.Text.Trim(), Kind, _summary.Text.Trim(), AreaText);

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            if (cut) MessageBox.Show(this, "The text was long, so it was trimmed. Add the rest in the Linear issue if it matters.", Text);
            DialogResult = DialogResult.OK;
        }
        catch (Exception e)
        {
            try { Clipboard.SetText(BuildBody()); } catch { }
            MessageBox.Show(this, "Could not open the browser (" + e.Message + "). The text was copied; paste it into a new issue in Linear.", Text);
        }
    }

    /// <summary>The issue text (Markdown) shared by the pre-filled link and the direct send.</summary>
    public static string BuildDescription(RequestSender.RequestKind kind, string area, string details) =>
        $"**Type:** {(kind == RequestSender.RequestKind.Bug ? "Bug report" : "Feature request")}\n**About:** {area}\n**Modwright:** {Version}\n\n### Details\n\n{details}"
        .Replace("\r\n", "\n");

    static DateTime _lastSend = DateTime.MinValue;

    async Task SendRequestAsync(Button button)
    {
        if (!CheckFields()) return;
        if (DateTime.UtcNow - _lastSend < TimeSpan.FromSeconds(60)) { MessageBox.Show(this, "A request was just sent. Please wait a minute before sending another.", Text); return; }

        button.Enabled = false;
        var oldText = button.Text; button.Text = "Sending...";
        try
        {
            var description = BuildDescription(Kind, AreaText, _details.Text.Trim());
            var (ok, message) = await RequestSender.SendAsync(Title(), description, Kind, AreaKey);
            if (ok)
            {
                _lastSend = DateTime.UtcNow;
                MessageBox.Show(this, $"Thanks! Your request was sent (reference {message}).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                return;
            }
            try { Clipboard.SetText(BuildBody()); } catch { }
            MessageBox.Show(this, message + "\n\nThe request text was copied to your clipboard so you can send it another way.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed) { button.Text = oldText; button.Enabled = true; } }
    }

    /// <summary>The pre-filled "new issue" address. The text is trimmed until the whole address fits (cut = true when that happened).</summary>
    public static (string url, bool cut) BuildIssueUrl(string details, RequestSender.RequestKind kind, string summary, string area = AnyArea)
    {
        string Url(string text)
        {
            var description = BuildDescription(kind, area, text);
            return NewIssueUrl + "?team=" + TeamKey
                + "&title=" + Uri.EscapeDataString(BuildTitle(kind, summary))
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
}
