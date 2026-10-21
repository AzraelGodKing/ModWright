using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// Drop Pod > More > "Send the active mods to a Steam collection...": creates a Workshop collection of the active
/// Workshop mods, or makes an existing one match them (inactive mods are taken out), through the Steam client.
/// </summary>
public sealed class CollectionForm : Form
{
    readonly RadioButton _new = new() { Text = "Create a &new collection", AutoSize = true, Checked = true };
    readonly RadioButton _update = new() { Text = "&Update my collection:", AutoSize = true };
    readonly TextBox _link = new() { Width = 360, PlaceholderText = "Collection link or id", AccessibleName = "Collection link or id" };
    readonly TextBox _title = new() { Width = 520, AccessibleName = "Title" };
    readonly TextBox _description = new() { Width = 520, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Description" };
    readonly ComboBox _visibility = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Who can see it" };
    readonly CheckBox _sendPage = new() { Text = "Also change its &title, description and visibility", AutoSize = true };
    readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 8, 0, 0), UseMnemonic = false }.Role(ThemeRole.Muted);
    readonly Button _send = new Button { Text = "&Send to Steam", AutoSize = true, Padding = new Padding(12, 2, 12, 2) }.Role(ThemeRole.Primary);
    readonly string _appId;
    readonly IReadOnlyList<string> _items;

    /// <summary>The collection id once one was created or updated (the caller remembers it for the modlist).</summary>
    public string? CollectionId { get; private set; }

    /// <param name="items">The Workshop ids to put in it, in load order.</param>
    /// <param name="leftOut">Names of active mods that can't be in a collection.</param>
    /// <param name="knownId">The collection this modlist was sent to before, if any.</param>
    public CollectionForm(string appId, string name, IReadOnlyList<string> items, IReadOnlyList<string> leftOut, string? knownId)
    {
        _appId = appId;
        _items = items;
        Text = "Steam collection";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var heading = new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 4), Font = new Font(Theme.BodyFont, FontStyle.Bold), UseMnemonic = false,
            Text = items.Count == 1 ? "1 active Workshop mod goes in the collection." : $"{items.Count} active Workshop mods go in the collection.",
        };
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 8), UseMnemonic = false,
            Text = "Inactive mods are not included, and updating a collection takes out anything that is no longer active." +
                   (leftOut.Count == 0 ? "" : $" Left out because they are not on the Workshop: {string.Join(", ", leftOut.Take(8))}{(leftOut.Count > 8 ? ", ..." : "")}.") +
                   " Steam must be running and signed in with an account that owns the game.",
        }.Role(ThemeRole.Muted);
        _title.Text = name;
        _description.Text = $"{name}: {items.Count} mods, in load order.";
        _visibility.Items.AddRange(new object[] { "Public", "Friends only", "Hidden (private)", "Unlisted (link only)" });
        _visibility.SelectedIndex = 0;
        if (knownId != null) { _update.Checked = true; _link.Text = SteamCollections.Url(knownId); }

        var updateRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        updateRow.Controls.Add(_update);
        updateRow.Controls.Add(_link);
        var visRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        visRow.Controls.Add(new Label { Text = "Who can see it", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        visRow.Controls.Add(_visibility);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 2, 12, 2) };
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(_send);
        buttons.Controls.Add(close);

        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(16, 12), Padding = new Padding(0, 0, 16, 12) };
        root.Controls.Add(heading);
        root.Controls.Add(hint);
        root.Controls.Add(_new);
        root.Controls.Add(updateRow);
        root.Controls.Add(_sendPage);
        root.Controls.Add(new Label { Text = "Title", AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
        root.Controls.Add(_title);
        root.Controls.Add(new Label { Text = "Description", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        root.Controls.Add(_description);
        root.Controls.Add(visRow);
        root.Controls.Add(_status);
        root.Controls.Add(buttons);
        Controls.Add(root);
        CancelButton = close;

        _new.CheckedChanged += (_, _) => UpdateEnabled();
        _sendPage.CheckedChanged += (_, _) => UpdateEnabled();
        _link.TextChanged += (_, _) => UpdateEnabled();
        _title.TextChanged += (_, _) => UpdateEnabled();
        _send.Click += async (_, _) => await SendAsync();
        UpdateEnabled();
        Theme.Apply(this);
    }

    void UpdateEnabled()
    {
        var updating = _update.Checked;
        _link.Enabled = _sendPage.Enabled = updating;
        var page = !updating || _sendPage.Checked;
        _title.Enabled = _description.Enabled = _visibility.Enabled = page;
        _send.Enabled = _items.Count > 0 && (updating ? SteamCollections.ParseId(_link.Text) != null : true) && (!page || _title.Text.Trim().Length > 0);
    }

    async Task SendAsync()
    {
        var req = new CollectionRequest
        {
            AppId = _appId,
            CollectionId = _update.Checked ? SteamCollections.ParseId(_link.Text) : null,
            Title = _title.Text.Trim(),
            Description = _description.Text.Trim(),
            Visibility = (CollectionVisibility)_visibility.SelectedIndex,
            UpdatePage = _sendPage.Checked,
            Items = _items.ToArray(),
        };
        _send.Enabled = false;
        UseWaitCursor = true;
        _status.Text = req.CollectionId == null ? "Creating the collection through the Steam client..." : "Updating the collection through the Steam client...";
        try
        {
            var r = await SteamCollections.ApplyOnSteamAsync(req, CancellationToken.None);
            if (r.CollectionId != null)
            {
                CollectionId = r.CollectionId;
                // A new collection that failed half way exists now: the next send should update it, not make another.
                _update.Checked = true;
                _link.Text = SteamCollections.Url(r.CollectionId);
            }
            _status.Text = SteamCollections.Describe(r);
            if (r.Ok && r.CollectionId != null)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SteamCollections.Url(r.CollectionId)) { UseShellExecute = true }); } catch { }
        }
        finally
        {
            UseWaitCursor = false;
            UpdateEnabled();
        }
    }
}
