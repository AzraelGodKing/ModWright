using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace SteamCmdHelper.DropPod.SunHaven;

/// <summary>
/// Sun Haven for the Drop Pod. Its mods are BepInEx plugins: a folder (Thunderstore's "Author-Name", with manifest.json)
/// or a loose DLL in BepInEx/plugins. BepInEx loads every plugin there and has no load order of its own, so "active" means
/// in BepInEx/plugins and "inactive" means moved to BepInEx/plugins-disabled (the folder mod managers use).
/// </summary>
public sealed class SunHavenModule : IGameModule
{
    /// <summary>Sun Haven's Steam app id.</summary>
    public const string SteamAppId = "1432860";
    public const string InstallDir = "Sun Haven";
    public const string Exe = "Sun Haven.exe";
    /// <summary>Thunderstore's id for the BepInEx loader package, which every Sun Haven mod names as a dependency.</summary>
    public const string BepInExPack = "bepinex-bepinexpack";

    public string Name => "Sun Haven";
    public string AppId => SteamAppId;
    public IReadOnlyList<string> ProcessNames { get; } = new[] { "Sun Haven" };
    public bool HasLoadOrder => false;

    public GameInstall? Detect(SteamLibraries steam, string? gameFolder, string? dataFolder)
    {
        var game = !string.IsNullOrWhiteSpace(gameFolder) ? gameFolder
            : steam.FindApp(AppId, InstallDir) ?? steam.Libraries.Select(l => Path.Combine(l, "steamapps", "common", InstallDir)).FirstOrDefault(Directory.Exists);
        if (game == null || !Directory.Exists(game)) return null;
        return new GameInstall(game, null, null, !string.IsNullOrWhiteSpace(dataFolder) ? dataFolder : DefaultDataFolder());
    }

    public static string BepInExFolder(GameInstall install) => SafeFiles.Child(install.GameFolder, "BepInEx", directory: true) ?? Path.Combine(install.GameFolder, "BepInEx");
    public static string PluginsFolder(GameInstall install) => Path.Combine(BepInExFolder(install), "plugins");
    public static string DisabledFolder(GameInstall install) => Path.Combine(BepInExFolder(install), "plugins-disabled");

    /// <summary>True when BepInEx is installed (its core folder is there); without it no mod loads.</summary>
    public static bool HasBepInEx(GameInstall install) => Directory.Exists(Path.Combine(BepInExFolder(install), "core"));

    public IReadOnlyList<ModFolder> ModFolders(GameInstall install) =>
        new[] { new ModFolder(PluginsFolder(install), ModSource.Local), new ModFolder(DisabledFolder(install), ModSource.Local) };

    /// <summary>Plugin folders and loose plugin DLLs.</summary>
    public IEnumerable<string> ModEntries(string modFolder) =>
        Directory.GetDirectories(modFolder).Concat(Directory.GetFiles(modFolder, "*.dll"));

    public ModManifest? ReadMod(string folder, ModSource source) => Read(folder, source);

    /// <summary>A plugin folder or DLL as a mod; null for a folder with neither manifest.json nor a DLL in it.</summary>
    public static ModManifest? Read(string path, ModSource source)
    {
        if (File.Exists(path))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return null;
            var m = new ModManifest { Folder = path, Source = source, Id = Path.GetFileNameWithoutExtension(path) };
            if (PluginInfo(path) is { } p) { m.Name = p.Name; m.ModVersion = p.Version; m.Description = "BepInEx plugin " + p.Guid; }
            return m;
        }
        if (!Directory.Exists(path)) return null;
        var manifest = SafeFiles.Child(path, "manifest.json");
        var dlls = SafeDlls(path);
        if (manifest == null && dlls.Count == 0) return null;
        var mod = new ModManifest { Folder = path, Source = source, Id = Path.GetFileName(path.TrimEnd('/', '\\')) };
        if (manifest != null) ReadManifest(manifest, mod);
        if (mod.Name.Length == 0 && dlls.Select(PluginInfo).FirstOrDefault(p => p != null) is { } info)
        {
            mod.Name = info.Name;
            if (mod.ModVersion.Length == 0) mod.ModVersion = info.Version;
        }
        if (SafeFiles.Child(path, "icon.png") is { } icon) mod.PreviewPath = icon;
        return mod;
    }

    static List<string> SafeDlls(string dir)
    {
        try { return Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(); }
    }

    /// <summary>Thunderstore's manifest.json: name, version_number, website_url, description and dependencies ("Author-Name-1.2.3").</summary>
    static void ReadManifest(string file, ModManifest mod)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var r = doc.RootElement;
            string Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? "" : "";
            mod.Name = Str("name").Replace('_', ' ');
            mod.ModVersion = Str("version_number");
            mod.Url = Str("website_url");
            mod.Description = Str("description");
            if (Str("author") is { Length: > 0 } a) mod.Authors.Add(a);
            else if (mod.Id.Contains('-')) mod.Authors.Add(mod.Id[..mod.Id.IndexOf('-')]);
            if (r.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                foreach (var d in deps.EnumerateArray())
                    if (d.ValueKind == JsonValueKind.String && ThunderstoreMods.DependencyId(d.GetString()) is { } id && !ModManifest.KeyOf(id).StartsWith(BepInExPack, StringComparison.Ordinal))
                        mod.Dependencies.All.Add(new ModDependency(id, id[(id.IndexOf('-') + 1)..].Replace('_', ' '), null, ThunderstoreMods.PageUrl(id)));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            mod.Issues.Add("manifest.json could not be read: " + e.Message);
        }
    }

    /// <summary>The BepInPlugin attribute of a plugin DLL (GUID, name, version), read from its metadata without loading it.</summary>
    public static (string Guid, string Name, string Version)? PluginInfo(string dll)
    {
        try
        {
            using var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            foreach (var h in md.CustomAttributes)
            {
                var a = md.GetCustomAttribute(h);
                if (a.Constructor.Kind != HandleKind.MemberReference) continue;
                var ctor = md.GetMemberReference((MemberReferenceHandle)a.Constructor);
                if (ctor.Parent.Kind != HandleKind.TypeReference || md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name) != "BepInPlugin") continue;
                var blob = md.GetBlobReader(a.Value);
                if (blob.ReadUInt16() != 1) continue;
                var guid = blob.ReadSerializedString() ?? "";
                var name = blob.ReadSerializedString() ?? "";
                var version = blob.ReadSerializedString() ?? "";
                return (guid, name, version);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { }
        return null;
    }

    /// <summary>The mods in BepInEx/plugins, by name (BepInEx has no order). Null when there's no plugins folder yet.</summary>
    public IReadOnlyList<string>? ReadActiveMods(GameInstall install)
    {
        var plugins = PluginsFolder(install);
        if (!Directory.Exists(plugins)) return null;
        return ModEntries(plugins).Select(p => Read(p, ModSource.Local)).Where(m => m != null)
            .OrderBy(m => m!.DisplayName, StringComparer.CurrentCultureIgnoreCase).Select(m => m!.Key).Distinct().ToList();
    }

    /// <summary>Moves plugins between BepInEx/plugins (the given ids) and BepInEx/plugins-disabled (the rest).</summary>
    public void WriteActiveMods(GameInstall install, IReadOnlyList<string> ids)
    {
        var want = ids.Select(ModManifest.KeyOf).ToHashSet(StringComparer.Ordinal);
        var plugins = PluginsFolder(install);
        var disabled = DisabledFolder(install);
        Directory.CreateDirectory(plugins);
        void MoveAll(string from, string to, bool fromActive)
        {
            if (!Directory.Exists(from)) return;
            foreach (var entry in ModEntries(from).ToList())
            {
                if (Read(entry, ModSource.Local) is not { } m || want.Contains(m.Key) == fromActive) continue; // already where it belongs
                Directory.CreateDirectory(to);
                var target = Path.Combine(to, Path.GetFileName(entry));
                if (File.Exists(target) || Directory.Exists(target)) throw new IOException($"{m.DisplayName} is in both {from} and {to}; remove one copy first.");
                if (File.Exists(entry)) File.Move(entry, target); else Directory.Move(entry, target);
            }
        }
        MoveAll(disabled, plugins, fromActive: false);
        MoveAll(plugins, disabled, fromActive: true);
    }

    public string ActiveKey(string id) => ModManifest.KeyOf(id);

    public GameLaunch? Launch(GameInstall install, bool ownDataFolder, string? logFile)
    {
        var exe = SafeFiles.Child(install.GameFolder, Exe);
        return exe == null ? null : new GameLaunch(exe, logFile != null ? new[] { "-logFile", logFile } : Array.Empty<string>(), install.GameFolder);
    }

    /// <summary>BepInEx's own log, which has every plugin's errors.</summary>
    public string LogFile(GameInstall install) => Path.Combine(BepInExFolder(install), "LogOutput.log");

    public IReadOnlyList<string> SettingsFiles { get; } = Array.Empty<string>();

    public IReadOnlyList<string> SaveFiles(GameInstall install) => Array.Empty<string>();

    public SaveInfo ReadSave(string path) =>
        new(path, Path.GetFileNameWithoutExtension(path), DateTime.MinValue, 0, "", new Modlist(), "Sun Haven saves don't list their mods.");

    public int SortTier(ModManifest mod) => 100;

    /// <summary>Where Sun Haven keeps its settings and saves (Windows: AppData\LocalLow\Pixel Sprout Studios\Sun Haven).</summary>
    public static string DefaultDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Pixel Sprout Studios", "Sun Haven");
}
