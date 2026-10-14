namespace SteamCmdHelper;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Headless check used for testing: SteamCmdHelper.exe --selftest <output.txt>
        var i = Array.IndexOf(args, "--selftest");
        if (i >= 0)
        {
            var settings = AppSettings.Load();
            var outFile = i + 1 < args.Length ? args[i + 1] : "selftest.txt";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("repo: " + settings.RepoPath);
            sb.AppendLine("branch: " + ModScanner.GitBranch(settings.RepoPath));
            sb.AppendLine("steamcmd: " + settings.EffectiveSteamCmd + " installed=" + SteamLogin.SteamCmdInstalled(settings.EffectiveSteamCmd));
            foreach (var m in ModScanner.Scan(settings.RepoPath))
                sb.AppendLine($"{m.Folder} | {m.Name} | v{m.Version} | id={m.WorkshopId ?? "-"} | {m.StatusText}");
            File.WriteAllText(outFile, sb.ToString());
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
