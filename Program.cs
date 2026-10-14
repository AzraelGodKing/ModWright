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

    static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

    [STAThread]
    static void Main(string[] args)
    {
        // Headless checks used for testing:
        //   SteamCmdHelper.exe --selftest <output.txt>                       list what the scanner sees
        //   SteamCmdHelper.exe --package <ModFolder> <output.txt> [--create] stage a dry-run package, write the .vdf path and file list
        var lt = Array.IndexOf(args, "--layouttest");
        if (lt >= 0 && lt + 1 < args.Length) { File.WriteAllText(args[lt + 1], LayoutTest()); return; }

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
            var (ok, msg) = RequestSender.SendAsync("Game layout request: Test \"Q\"", "line1\nline2 \\ x", prefix).GetAwaiter().GetResult();
            served.Wait(5000);
            File.AppendAllText(outFile, $"available={RequestSender.Available} ok={ok} msg={msg}\r\n");
            return;
        }
        if (Array.IndexOf(args, "--urltest") is var ut and >= 0 && ut + 1 < args.Length)
        {
            var (u1, c1) = RequestForm.BuildIssueUrl("Test & Game", "123", "Custom", "line1\r\nline2 = \"x\" & y");
            var (u2, c2) = RequestForm.BuildIssueUrl("Big Game", "456", "Generic", string.Join("\r\n", Enumerable.Range(0, 3000).Select(n => "folder/sub/file_" + n + ".json")));
            File.WriteAllText(args[ut + 1], $"short: len={u1.Length} cut={c1}\r\n{u1}\r\nlong: len={u2.Length} cut={c2}\r\n{u2.Substring(0, 160)}...\r\n");
            return;
        }

        if (Array.IndexOf(args, "--shot") is var sh and >= 0 && sh + 1 < args.Length)
        {
            // Render the main window and the request form to PNG files (layout check without clicking).
            ApplicationConfiguration.Initialize();
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
            Snap(new MainForm(), Path.Combine(args[sh + 1], "main.png"));
            Snap(new RequestForm("RimWorld", "294100", "RimWorld", "About/About.xml\r\nAbout/Preview.png\r\nAssemblies/Mod.dll"), Path.Combine(args[sh + 1], "request.png"));
            return;
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
                if (mod == null) { File.WriteAllText(outFile, "mod not found: " + folder); return; }
                var pub = new Publisher(settings.RepoPath, cfg, settings.EffectiveSteamCmd, settings.Account);
                pub.Log += l => sb.AppendLine(l);
                var res = pub.RunAsync(mod, false, CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine("RESULT ok=" + res.Ok + " " + res.Message);
                File.WriteAllText(outFile, sb.ToString());
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
