namespace SteamCmdHelper;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Headless checks used for testing:
        //   SteamCmdHelper.exe --selftest <output.txt>                       list what the scanner sees
        //   SteamCmdHelper.exe --package <ModFolder> <output.txt> [--create] stage a dry-run package, write the .vdf path and file list
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
