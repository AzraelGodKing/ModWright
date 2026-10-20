namespace SteamCmdHelper;

/// <summary>
/// "Later...": schedules the ticked mods to upload at a time you pick (Windows Task Scheduler runs the command line, so
/// the window need not stay open), and lists this PC's scheduled uploads to cancel them.
/// </summary>
public sealed class UploadLaterForm : Form
{
    readonly DateTimePicker _when = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm", ShowUpDown = true, Width = 170, AccessibleName = "When to upload" };
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Width = 620, Height = 160, AccessibleName = "Scheduled uploads" };
    readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 6, 0, 0) }.Role(ThemeRole.Muted);
    readonly Button _cancelOne = new() { Text = "&Cancel the selected upload", AutoSize = true, Enabled = false };

    /// <param name="ticked">The ticked mods' folder names; empty only lists what is scheduled.</param>
    /// <param name="args">The upload command line for the ticked mods.</param>
    public UploadLaterForm(string repo, IReadOnlyList<string> ticked, List<string> args)
    {
        Text = "Upload later";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var what = ScheduledUploads.Describe(ticked);
        _when.Value = DateTime.Now.AddHours(1).Date.AddHours(DateTime.Now.AddHours(1).Hour);
        var schedule = new Button { Text = "&Schedule", AutoSize = true, Padding = new Padding(12, 2, 12, 2), Enabled = ticked.Count > 0 }.Role(ThemeRole.Primary);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 2, 12, 2) };
        var heading = new Label
        {
            AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 6), Font = new Font(Theme.BodyFont, FontStyle.Bold), UseMnemonic = false,
            Text = ticked.Count > 0 ? $"Upload {what} at:" : "Tick the mods to upload later first. Already scheduled:",
        };
        var pick = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Visible = ticked.Count > 0 };
        pick.Controls.Add(_when);
        pick.Controls.Add(schedule);
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 6, 0, 10), Visible = ticked.Count > 0,
            Text = "Windows Task Scheduler runs the upload with this PC's saved Steam login and the options of this mods folder, so the window " +
                   "need not stay open. The PC must be on and you signed in; if it is off then, it runs as soon as it can. Its output goes to the past logs.",
        }.Role(ThemeRole.Muted);
        _list.Columns.Add("When", 130);
        _list.Columns.Add("Mods", 200);
        _list.Columns.Add("Mods folder", 270);

        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(16, 12), Padding = new Padding(0, 0, 16, 12) };
        root.Controls.Add(heading);
        root.Controls.Add(pick);
        root.Controls.Add(hint);
        root.Controls.Add(new Label { Text = "Scheduled uploads on this PC", AutoSize = true, Font = new Font(Theme.BodyFont, FontStyle.Bold), Margin = new Padding(0, 4, 0, 4) });
        root.Controls.Add(_list);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_cancelOne);
        buttons.Controls.Add(close);
        root.Controls.Add(_status);
        root.Controls.Add(buttons);
        Controls.Add(root);
        CancelButton = close;

        _list.SelectedIndexChanged += (_, _) => _cancelOne.Enabled = _list.SelectedItems.Count == 1;
        schedule.Click += async (_, _) =>
        {
            schedule.Enabled = false;
            var (done, error) = await ScheduledUploads.AddAsync(_when.Value, DateTime.Now, repo, what, args);
            _status.Text = error ?? $"Scheduled: {what} uploads at {done!.At:yyyy-MM-dd HH:mm}.";
            schedule.Enabled = true;
            Fill();
        };
        _cancelOne.Click += async (_, _) =>
        {
            if (_list.SelectedItems.Count != 1 || _list.SelectedItems[0].Tag is not ScheduledUpload s) return;
            var error = await ScheduledUploads.CancelAsync(s.Name, DateTime.Now);
            _status.Text = error ?? $"Cancelled the upload of {s.What} at {s.At:yyyy-MM-dd HH:mm}.";
            Fill();
        };
        Fill();
        Theme.Apply(this);
    }

    void Fill()
    {
        _list.Items.Clear();
        var now = DateTime.Now;
        foreach (var s in ScheduledUploads.Load(now))
            _list.Items.Add(new ListViewItem(new[] { s.At.ToString("yyyy-MM-dd HH:mm") + (s.At < now ? " (passed)" : ""), s.What, s.Repo }) { Tag = s });
        if (_list.Items.Count == 0) _list.Items.Add(new ListViewItem(new[] { "", "None yet", "" }));
        _cancelOne.Enabled = false;
    }
}
