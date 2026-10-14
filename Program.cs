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
