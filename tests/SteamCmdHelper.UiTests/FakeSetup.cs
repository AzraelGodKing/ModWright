using System.Text.Json;

namespace SteamCmdHelper.UiTests;

/// <summary>
/// A throwaway world for one run: a portable copy of the app (its settings in a "data" folder beside it), a RimWorld
/// install with two mods, and a Sun Haven install with BepInEx and two plugins.
/// </summary>
public sealed class FakeSetup : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "modwright-ui-" + Guid.NewGuid().ToString("N")[..8]);
    public string App => Path.Combine(Root, "app");
    public string Exe => Path.Combine(App, "SteamCmdHelper.exe");
    public string RimWorld => Path.Combine(Root, "RimWorld");
    public string RimWorldData => Path.Combine(Root, "RimWorldData");
    public string SunHaven => Path.Combine(Root, "Sun Haven");
    public string ModsConfig => Path.Combine(RimWorldData, "Config", "ModsConfig.xml");

    public FakeSetup(string game = "294100")
    {
        CopyApp();
        File.WriteAllText(Path.Combine(App, "portable.txt"), "");

        Write(Path.Combine(RimWorld, "Version.txt"), "1.5.4104 rev435");
        About(Path.Combine(RimWorld, "Data", "Core"), "ludeon.rimworld", "Core");
        About(Path.Combine(RimWorld, "Mods", "ModA"), "test.moda", "Test Mod A", "<modDependencies><li><packageId>test.modb</packageId><displayName>Test Mod B</displayName></li></modDependencies>");
        About(Path.Combine(RimWorld, "Mods", "ModB"), "test.modb", "Test Mod B");
        Write(ModsConfig, "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModsConfigData><version>1.5.4104 rev435</version><activeMods><li>ludeon.rimworld</li><li>test.moda</li></activeMods><knownExpansions /></ModsConfigData>");

        Directory.CreateDirectory(Path.Combine(SunHaven, "BepInEx", "core"));
        Write(Path.Combine(SunHaven, "Sun Haven.exe"), "");
        Plugin("plugins", "Author-Farming", "Author-Lib-1.0.0");
        Plugin("plugins-disabled", "Author-Lib");

        var settings = new Dictionary<string, object>
        {
            ["Sides"] = "Manager",
            ["CheckForUpdates"] = false,
            ["DropPodSaveBackups"] = false,
            ["DropPodGame"] = game,
            ["GameFolders"] = new Dictionary<string, string> { ["294100"] = RimWorld, ["1432860"] = SunHaven },
            ["GameDataFolders"] = new Dictionary<string, string> { ["294100"] = RimWorldData, ["1432860"] = Path.Combine(Root, "SunHavenData") },
        };
        Write(Path.Combine(App, "data", "settings.json"), JsonSerializer.Serialize(settings));
    }

    void CopyApp()
    {
        var exe = Environment.GetEnvironmentVariable("MODWRIGHT_EXE");
        if (string.IsNullOrEmpty(exe))
        {
            var repo = AppContext.BaseDirectory;
            while (repo != null && !File.Exists(Path.Combine(repo, "SteamCmdHelper.csproj"))) repo = Path.GetDirectoryName(repo);
            exe = repo == null ? null : Path.Combine(repo, "bin", "Release", "net8.0-windows", "SteamCmdHelper.exe");
        }
        if (exe == null || !File.Exists(exe)) throw new FileNotFoundException("Build or publish the app first, or set MODWRIGHT_EXE.", exe);
        // A published single-file exe is all there is; a normal build needs its whole folder.
        var from = Path.GetDirectoryName(Path.GetFullPath(exe))!;
        Directory.CreateDirectory(App);
        var files = File.Exists(Path.Combine(from, "SteamCmdHelper.dll")) ? Directory.GetFiles(from, "*", SearchOption.AllDirectories) : new[] { exe };
        foreach (var f in files)
        {
            var to = Path.Combine(App, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
    }

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    static void About(string mod, string id, string name, string extra = "") =>
        Write(Path.Combine(mod, "About", "About.xml"),
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?><ModMetaData><packageId>{id}</packageId><name>{name}</name><author>Tester</author><supportedVersions><li>1.5</li></supportedVersions>{extra}</ModMetaData>");

    void Plugin(string where, string id, params string[] deps)
    {
        var dir = Path.Combine(SunHaven, "BepInEx", where, id);
        Write(Path.Combine(dir, "manifest.json"),
            $"{{\"name\":\"{id[(id.IndexOf('-') + 1)..]}\",\"version_number\":\"1.0.0\",\"website_url\":\"\",\"description\":\"\",\"dependencies\":[{string.Join(",", deps.Select(d => $"\"{d}\""))}]}}");
        File.WriteAllBytes(Path.Combine(dir, id + ".dll"), new byte[] { 0 });
    }

    public void Dispose()
    {
        for (var i = 0; i < 10; i++)
        {
            try { Directory.Delete(Root, true); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(500); } // the exe may take a moment to let go
        }
    }
}
