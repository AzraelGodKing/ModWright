namespace SteamCmdHelper;

static class Program
{
    /// <summary>Headless check of the locked built-in layouts and the editable Custom layout.</summary>
    static string LayoutTest()
    {
        var sb = new System.Text.StringBuilder();
        var dir = Path.Combine(Path.GetTempPath(), "layouttest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = ProjectConfig.PathFor(dir);
        void Show(string label, ProjectConfig c) =>
            sb.AppendLine($"{label}: preset={c.PresetName()} builtIn={c.IsBuiltIn} appIdLocked={c.AppIdLocked} app={c.AppId} marker='{c.ModMarker}' changelog='{c.Changelog}'");

        // 1. choosing a built-in layout writes only its name
        var c1 = new ProjectConfig(); c1.ApplyPreset("RimWorld"); c1.Save(dir);
        sb.AppendLine("1) file after choosing RimWorld: " + File.ReadAllText(file).Replace("\r", "").Replace("\n", " "));

        // 2. editing the file does not change a built-in layout
        File.WriteAllText(file, "{ \"Preset\": \"RimWorld\", \"AppId\": \"1\", \"ModMarker\": \"hacked.txt\", \"Changelog\": \"nope.md\" }");
        Show("2) tampered RimWorld file", ProjectConfig.Load(dir));

        // 3. Generic keeps the app id the user typed
        var c3 = new ProjectConfig(); c3.ApplyPreset("Generic"); c3.AppId = "123456"; c3.Save(dir);
        sb.AppendLine("3) file for Generic + app id: " + File.ReadAllText(file).Replace("\r", "").Replace("\n", " "));
        Show("3) reloaded", ProjectConfig.Load(dir));

        // 4. Custom is stored in full and editable
        var c4 = ProjectConfig.Load(dir); c4.MakeCustom(); c4.ModMarker = "mod.json"; c4.Save(dir);
        Show("4) custom after save", ProjectConfig.Load(dir));
        var len = File.ReadAllText(file).Length;
        sb.AppendLine("4) custom file length=" + len + " (full layout stored)");
        File.WriteAllText(file, File.ReadAllText(file).Replace("mod.json", "other.json"));
        Show("4) custom after hand edit", ProjectConfig.Load(dir));

        // 5. an old file without a Preset field that equals RimWorld becomes the locked RimWorld layout
        File.WriteAllText(file, "{ \"AppId\": \"294100\", \"ModMarker\": \"About/About.xml\", \"NameXPath\": \"ModMetaData/name\", \"VersionXPath\": \"ModMetaData/modVersion\", \"Changelog\": \"About/changelog.txt\", \"ChangelogFirstLineIsVersion\": true, \"WorkshopIdFile\": \"About/PublishedFileId.txt\" }");
        Show("5) old 1.1.0 file", ProjectConfig.Load(dir));
        sb.AppendLine("5) EnsureFile on built-in throws: " + Throws(() => ProjectConfig.Load(dir).EnsureFile(dir)));

        try { Directory.Delete(dir, true); } catch { }
        return sb.ToString();
    }

    /// <summary>A throwaway RimWorld mods folder for the --shot screenshots: one mod per kind of row.</summary>
    static string DemoRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_shot_repo");
        try { Directory.Delete(root, true); } catch { }
        void Mod(string folder, string name, string version, string? id, string? changelogVersion, Color? preview, bool built = true)
        {
            var about = Path.Combine(root, folder, "About");
            Directory.CreateDirectory(about);
            File.WriteAllText(Path.Combine(about, "About.xml"), $"<?xml version=\"1.0\"?><ModMetaData><name>{name}</name><modVersion>{version}</modVersion><supportedVersions><li>1.6</li></supportedVersions><description>Demo.</description></ModMetaData>");
            if (changelogVersion != null) File.WriteAllText(Path.Combine(about, "changelog.txt"), $"{changelogVersion}\n- Demo change.");
            if (id != null) File.WriteAllText(Path.Combine(about, "PublishedFileId.txt"), id);
            if (preview is { } c)
            {
                using var bmp = new Bitmap(640, 360);
                using (var g = Graphics.FromImage(bmp))
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 640, 360), c, Color.FromArgb(20, 22, 30), 35f))
                {
                    g.FillRectangle(b, 0, 0, 640, 360);
                    using var f = new Font("Segoe UI Semibold", 54f);
                    TextRenderer.DrawText(g, name, f, new Rectangle(0, 0, 640, 360), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                bmp.Save(Path.Combine(about, "Preview.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            if (built)
            {
                Directory.CreateDirectory(Path.Combine(root, folder, "Assemblies"));
                File.WriteAllText(Path.Combine(root, folder, "Assemblies", folder + ".dll"), "demo");
            }
        }
        Mod("BetterStorage", "Better Storage", "1.4.0", "2000000001", "1.4.0", Color.FromArgb(48, 132, 230));
        Mod("FasterHauling", "Faster Hauling", "2.1.3", "2000000002", "2.1.3", Color.FromArgb(98, 170, 110), built: false);
        Mod("QuietNights", "Quiet Nights", "0.9.0", null, "0.9.0", Color.FromArgb(170, 110, 200));
        Mod("BrokenThing", "Broken Thing", "1.1.0", "2000000004", "1.0.0", null);
        var cfg = new ProjectConfig();
        cfg.ApplyPreset("RimWorld");
        cfg.Save(root);
        return root;
    }

    static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int which);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetFileType(IntPtr handle);

    /// <summary>True when standard output already goes to a file or pipe (a CI step, "> out.txt", "| more").</summary>
    static bool OutputToFileOrPipe()
    {
        const uint Disk = 1, Pipe = 3;
        var h = GetStdHandle(-11); // STD_OUTPUT_HANDLE
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return false;
        var type = GetFileType(h);
        return type == Disk || type == Pipe;
    }

    /// <summary>
    /// The command line (<see cref="Cli"/>). The app is a Windows (not console) program, so it attaches to the console it
    /// was started from to print there; in a CI step or with redirected output the text goes to the pipe or file as usual.
    /// </summary>
    static int RunCommandLine(string[] args)
    {
        // -1: the parent's console. The prompt there has already come back, so start on a fresh line.
        var attached = !OutputToFileOrPipe() && AttachConsole(-1);
        var o = Cli.Parse(args);
        var settings = AppSettings.Load(); // overrides below are for this run only, never saved
        if (o.Repo != null) settings.RepoPath = Path.GetFullPath(o.Repo);
        if (o.Account != null) settings.Account = o.Account;
        if (o.SteamCmd != null) settings.SteamCmdPath = Path.GetFullPath(o.SteamCmd);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var console = Console.Out;
        if (attached) console.WriteLine();
        // "upload --json" keeps standard output for the JSON alone; the progress goes to standard error.
        var jsonOut = o.Json && o.Command == "upload" && o.Error == null;
        if (jsonOut) console = Console.Error;
        using var log = o.LogFile != null ? new StreamWriter(o.LogFile, false) { AutoFlush = true } : null;
        // Every upload run is also saved with the window's past logs.
        StreamWriter? runLog = null;
        if (o.Command == "upload" && o.Error == null)
            try { runLog = new StreamWriter(RunLogs.NewPath(RunLogs.DefaultDir, RunLogs.Label((o.DryRun ? "dry run" : o.Test ? "test upload" : "upload") + " (command line)", o.Mods.Count > 0 ? o.Mods : new[] { o.Changed ? "changed mods" : "all mods" }), DateTime.Now), false) { AutoFlush = true }; }
            catch { runLog = null; }
        using var output = new TeeWriter(console, log, runLog);
        try
        {
            return Cli.RunAsync(o, settings.RepoPath, settings.EffectiveSteamCmd, settings.Account, TextWriter.Synchronized(output), cts.Token, credit: settings.UploadCredit,
                noteTemplate: settings.TemplateFor(settings.RepoPath), markdown: settings.MarkdownNotes,
                syncDescriptions: settings.DescriptionSync == DescriptionSync.Always,
                buildCommand: settings.BuildFor(settings.RepoPath), defaultRef: settings.RefFor(settings.RepoPath),
                retryAfter: settings.AutoRetry ? TimeSpan.FromSeconds(30) : null,
                webhook: settings.WebhookFor(settings.RepoPath), gitHubReleases: settings.GitHubReleasesFor(settings.RepoPath),
                json: jsonOut ? Console.Out : null,
                tagUpdater: settings.UpdateTags ? WorkshopTags.UpdateOnSteamAsync : null,
                oneSession: settings.OneSession, verifyUploads: settings.VerifyUploads,
                ownership: (account, appId, token) => SteamCmd.OwnsAsync(settings.EffectiveSteamCmd, account, appId, token),
                descriptionFooter: settings.FooterFor(settings.RepoPath)).GetAwaiter().GetResult();
        }
        finally { runLog?.Dispose(); }
    }

    /// <summary>Writes to the console and, when given, log files.</summary>
    sealed class TeeWriter(TextWriter a, params TextWriter?[] more) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;
        public override void Write(char value) { a.Write(value); foreach (var b in more) b?.Write(value); }
        public override void Write(string? value) { a.Write(value); foreach (var b in more) b?.Write(value); }
        public override void WriteLine(string? value) { a.WriteLine(value); foreach (var b in more) b?.WriteLine(value); }
        public override void Flush() { a.Flush(); foreach (var b in more) b?.Flush(); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        // Lets the Setup.exe install, update and uninstall this copy: Velopack runs the app with its own arguments at
        // those moments, and this handles them and exits. Does nothing otherwise. See AppUpdates.
        Velopack.VelopackApp.Build().Run();

        // The downloaded program installing itself once the old one has closed (see SelfUpdate.HandOff).
        if (args.Length > 0 && args[0] == SelfUpdate.FinishArg)
            return SelfUpdate.Finish(args[1..], message =>
            {
                ApplicationConfiguration.Initialize();
                MessageBox.Show(message, "SteamCMD Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });

        // Setting one Workshop item's tags through the Steam client, for a window or command line run (see WorkshopTags).
        if (args.Length > 0 && args[0] == WorkshopTags.ChildArg) return SteamworksTags.RunChild(args[1..]);

        if (Cli.IsCommand(args)) return RunCommandLine(args);

        // Headless checks used for testing (the real command line is "SteamCmdHelper upload ...", see Cli):
        //   SteamCmdHelper.exe --selftest <output.txt>                       list what the scanner sees
        //   SteamCmdHelper.exe --package <ModFolder> <output.txt> [--create] stage a dry-run package, write the .vdf path and file list
        var lt = Array.IndexOf(args, "--layouttest");
        if (lt >= 0 && lt + 1 < args.Length) { File.WriteAllText(args[lt + 1], LayoutTest()); return 0; }

        if (Array.IndexOf(args, "--sendtest") is var st and >= 0 && st + 1 < args.Length)
        {
            // Posts to a local fake endpoint and records what a real server would receive.
            var outFile = args[st + 1];
            var prefix = "http://localhost:47831/graphql/";
            using var listener = new System.Net.HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            var served = Task.Run(async () =>
            {
                var ctx = await listener.GetContextAsync();
                using var rd = new StreamReader(ctx.Request.InputStream);
                var body = await rd.ReadToEndAsync();
                File.WriteAllText(outFile, $"auth={ctx.Request.Headers["Authorization"]}\r\nmethod={ctx.Request.HttpMethod}\r\nbody={body}\r\n");
                var reply = System.Text.Encoding.UTF8.GetBytes("{\"data\":{\"issueCreate\":{\"success\":true,\"issue\":{\"identifier\":\"GR-7\"}}}}");
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(reply);
                ctx.Response.Close();
            });
            var (ok, msg) = RequestSender.SendAsync("Game layout request: Test \"Q\"", "line1\nline2 \\ x", endpointOverride: prefix).GetAwaiter().GetResult();
            served.Wait(5000);
            File.AppendAllText(outFile, $"available={RequestSender.Available} ok={ok} msg={msg}\r\n");
            return 0;
        }
        if (Array.IndexOf(args, "--urltest") is var ut and >= 0 && ut + 1 < args.Length)
        {
            var (u1, c1) = RequestForm.BuildIssueUrl("Test & Game", "123", "Custom", "line1\r\nline2 = \"x\" & y");
            var (u2, c2) = RequestForm.BuildIssueUrl("Big Game", "456", "Generic", string.Join("\r\n", Enumerable.Range(0, 3000).Select(n => "folder/sub/file_" + n + ".json")));
            File.WriteAllText(args[ut + 1], $"short: len={u1.Length} cut={c1}\r\n{u1}\r\nlong: len={u2.Length} cut={c2}\r\n{u2.Substring(0, 160)}...\r\n");
            return 0;
        }

        if (Array.IndexOf(args, "--shot") is var sh and >= 0 && sh + 1 < args.Length)
        {
            // Render the main window (also at 125% text), each section of its Options tab, the shortcuts list, the request form and the Workshop page editor to PNG files, in both themes (layout check without clicking).
            ApplicationConfiguration.Initialize();
            // A crash while drawing would otherwise sit behind an error dialog until CI times out; write it down and stop.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                File.AppendAllText(Path.Combine(args[sh + 1], "error.txt"), e.Exception + Environment.NewLine);
                Environment.Exit(1);
            };
            void Snap(Form f, string file)
            {
                // Real on-screen capture (DrawToBitmap leaves some controls blank); the window is visible for about two seconds.
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(20, 20); f.TopMost = true;
                f.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(1800); Application.DoEvents();
                using var bmp = new Bitmap(f.Width, f.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(f.Location, Point.Empty, f.Size);
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                f.Close();
            }
            foreach (var theme in new[] { Theme.DarkName, Theme.LightName })
            {
                var suffix = theme == Theme.DarkName ? "" : "-light";
                Snap(new MainForm(theme) { NoStartupPrompts = true }, Path.Combine(args[sh + 1], $"main{suffix}.png"));
                var demo = new MainForm(theme, new AppSettings { RepoPath = DemoRepo(), Account = "demo_account", AllowCreate = true, DoNotSave = true }) { NoStartupPrompts = true };
                demo.Shown += (_, _) => demo.SelectFirstMod();
                Snap(demo, Path.Combine(args[sh + 1], $"main-mods{suffix}.png"));
                var large = new MainForm(theme, new AppSettings { RepoPath = DemoRepo(), Account = "demo_account", AllowCreate = true, TextSize = 125, DoNotSave = true }) { NoStartupPrompts = true };
                large.Shown += (_, _) => large.SelectFirstMod();
                Snap(large, Path.Combine(args[sh + 1], $"main-mods-125{suffix}.png"));
                // The Options tab once per section: options.png is the first, then options-<section>.png for the rest.
                for (var s = 0; ; s++)
                {
                    var options = new MainForm(theme) { NoStartupPrompts = true };
                    var sections = options.OptionSections;
                    if (s >= sections.Count) { options.Dispose(); break; }
                    var section = s;
                    options.Shown += (_, _) => options.ShowOptionSection(section);
                    var name = section == 0 ? "options" : "options-" + new string(sections[section].ToLowerInvariant().Where(char.IsLetter).ToArray());
                    Snap(options, Path.Combine(args[sh + 1], $"{name}{suffix}.png"));
                }
                Snap(new ShortcutsForm(), Path.Combine(args[sh + 1], $"shortcuts{suffix}.png"));
                Snap(new RequestForm("RimWorld", "294100", "RimWorld", "About/About.xml\r\nAbout/Preview.png\r\nAssemblies/Mod.dll"), Path.Combine(args[sh + 1], $"request{suffix}.png"));
                var example = new ModInfo { Name = "Example Mod", Folder = "ExampleMod", Dir = Path.Combine(Path.GetTempPath(), "steamcmd_helper_shot_example") };
                Snap(new WorkshopPageForm(example, "About/steam_description.txt", "[h1]Example Mod[/h1]\nAdds an example to the game."), Path.Combine(args[sh + 1], $"workshop-page{suffix}.png"));
                var other = new ModInfo { Name = "Other Mod", Folder = "OtherMod", Version = "2.1.0", Dir = Path.Combine(Path.GetTempPath(), "steamcmd_helper_shot_other"), Dirty = true };
                example.Version = "1.4.0";
                Snap(new ConfirmUploadForm("Publish to the Steam Workshop (RimWorld):", new[] { example, other },
                    new Dictionary<string, string> { [example.Dir] = "update 2000000001", [other.Dir] = "update 2000000002  (uncommitted changes will NOT be uploaded)" },
                    new Dictionary<string, string> { [example.Dir] = "1.4.0\n- Storage shelves hold twice as much.\n- Fixed a crash when loading old saves.", [other.Dir] = "2.1.0\n- Faster hauling." },
                    dry: false, offerCommit: true), Path.Combine(args[sh + 1], $"confirm{suffix}.png"));
                Snap(new PackagePreviewForm(example, new PackageReport(
                    new[] { new PackageFile("About/About.xml", 812), new PackageFile("About/Preview.png", 412_330), new PackageFile("Assemblies/ExampleMod.dll", 48_640), new PackageFile("Textures/Icon.psd", 5_242_880) },
                    5_704_662, new[] { "has source art files (1): Textures/Icon.psd" })), Path.Combine(args[sh + 1], $"package{suffix}.png"));
            }
            return 0;
        }

        var i = Array.IndexOf(args, "--selftest");
        var j = Array.IndexOf(args, "--package");
        if (i >= 0 || j >= 0)
        {
            var settings = AppSettings.Load();
            var r = Array.IndexOf(args, "--repo");
            if (r >= 0 && r + 1 < args.Length) settings.RepoPath = args[r + 1]; // headless override, not saved
            var cfg = ProjectConfig.Load(settings.RepoPath);
            var sb = new System.Text.StringBuilder();
            var mods = ModScanner.Scan(settings.RepoPath, cfg);
            if (i >= 0)
            {
                var outFile = i + 1 < args.Length ? args[i + 1] : "selftest.txt";
                sb.AppendLine("repo: " + settings.RepoPath);
                sb.AppendLine("branch: " + ModScanner.GitBranch(settings.RepoPath));
                sb.AppendLine($"game: {cfg.GameName} app {cfg.AppId} layout: {cfg.Source}");
                sb.AppendLine("steamcmd: " + settings.EffectiveSteamCmd + " installed=" + SteamLogin.SteamCmdInstalled(settings.EffectiveSteamCmd));
                foreach (var m in mods)
                    sb.AppendLine($"{m.Folder} | {m.Name} | v{m.Version} | id={m.WorkshopId ?? "-"} | {m.StatusText}");
                File.WriteAllText(outFile, sb.ToString());
            }
            else
            {
                var folder = args[j + 1];
                var outFile = args[j + 2];
                var mod = mods.FirstOrDefault(x => x.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
                if (mod == null) { File.WriteAllText(outFile, "mod not found: " + folder); return 0; }
                var pub = new Publisher(settings.RepoPath, cfg, settings.EffectiveSteamCmd, settings.Account);
                pub.Log += l => sb.AppendLine(l);
                var res = pub.RunAsync(mod, false, CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine("RESULT ok=" + res.Ok + " " + res.Message);
                File.WriteAllText(outFile, sb.ToString());
            }
            return 0;
        }

        ApplicationConfiguration.Initialize();
        // The log box needs the code-page encodings when its window is destroyed. Load them now rather than as the app
        // closes, when .NET would have to read them from an exe that an update may be about to replace.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var u = Array.IndexOf(args, SelfUpdate.UpdatedFromArg);
        var updatedFrom = u >= 0 && u + 1 < args.Length ? args[u + 1] : null;
        // Right after an update the old version may still be closing; give it a moment instead of activating it.
        if (!SingleInstance.Claim(updatedFrom != null ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
        {
            SingleInstance.ActivateOther();
            return 0;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowCrash(e.Exception, canContinue: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) ShowCrash(ex, canContinue: false); };
        var form = new MainForm { UpdatedFrom = updatedFrom };
        SingleInstance.Listen(form);
        Application.Run(form);
        return 0;
    }

    static int _crashing;

    /// <summary>
    /// Saves a crash report and offers to send it as a bug report. <paramref name="canContinue"/> is false for errors
    /// off the window's thread, after which .NET ends the program anyway.
    /// </summary>
    static void ShowCrash(Exception e, bool canContinue)
    {
        if (Interlocked.Exchange(ref _crashing, 1) == 1) return; // an error while showing the last one
        try
        {
            var text = CrashReport.Build(e, UpdateCheck.CurrentVersion, DateTime.Now);
            var path = CrashReport.Save(text);
            var report = new TaskDialogButton("Report it");
            var copy = new TaskDialogButton("Copy details") { AllowCloseDialog = false };
            var keepGoing = new TaskDialogButton("Keep going");
            var quit = new TaskDialogButton("Close the app");
            var page = new TaskDialogPage
            {
                Caption = "SteamCMD Helper",
                Heading = "Something went wrong",
                Text = "SteamCMD Helper hit an error it did not expect. " +
                    (canContinue ? "You can keep going, but if anything looks wrong, restart the app. " : "It has to close. ") +
                    "Report it sends the details below as a bug report, after you review them." +
                    (path != null ? $"\n\nSaved to {path}" : ""),
                Icon = TaskDialogIcon.Error,
                Expander = new TaskDialogExpander(text) { CollapsedButtonText = "Details", Position = TaskDialogExpanderPosition.AfterFootnote },
                Buttons = { report, copy, canContinue ? keepGoing : quit },
            };
            if (canContinue) page.Buttons.Add(quit);
            copy.Click += (_, _) => { try { Clipboard.SetText(text); } catch { } };
            var owner = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is MainForm);
            var picked = owner != null && owner.IsHandleCreated && !owner.InvokeRequired ? TaskDialog.ShowDialog(owner, page) : TaskDialog.ShowDialog(page);
            if (picked == report)
            {
                using var f = new RequestForm("", "", "", "");
                f.Prefill(RequestSender.RequestKind.Bug, CrashReport.Summary(e),
                    "What were you doing when it happened:\n\n\nCrash report (edit out anything you don't want to share):\n" + text);
                f.ShowDialog(owner);
            }
            if (picked == quit && canContinue) Application.Exit();
        }
        catch { /* never let the crash handler crash */ }
        finally { _crashing = 0; }
    }
}
