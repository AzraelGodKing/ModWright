using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdHelper.DropPod;

/// <summary>
/// A separate copy of the player's game setup: its own config, mod list, saves and log, so a heavy modded run, a test
/// list or an old save can't touch the normal game. The mods themselves are shared (the game's Mods and Workshop
/// folders); an instance can use another install of the game (an older version, say) instead.
/// </summary>
public sealed class GameInstance
{
    public const int FormatVersion = 1;
    public int Format { get; set; } = FormatVersion;
    public string Name { get; set; } = "";
    public string AppId { get; set; } = "";
    /// <summary>Another install of the game to run, or "" for the usual one.</summary>
    public string GameFolder { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Extra arguments for starting the game with this instance ("-quicktest", "-popupwindow").</summary>
    public string LaunchArgs { get; set; } = "";
    public DateTime Created { get; set; }

    /// <summary>The instance's folder on disk (set when it is loaded).</summary>
    [JsonIgnore] public string Folder { get; set; } = "";
    /// <summary>Where the game keeps this instance's config and saves.</summary>
    [JsonIgnore] public string DataFolder => Path.Combine(Folder, DataDir);
    /// <summary>Where this instance's game log goes.</summary>
    [JsonIgnore] public string LogFile => Path.Combine(Folder, "Player.log");

    internal const string FileName = "instance.json";
    internal const string DataDir = "Data";

    /// <summary>The install this instance runs: <paramref name="usual"/> with this instance's data folder (and game folder, when it has its own).</summary>
    public GameInstall Apply(GameInstall usual, IGameModule game, SteamLibraries steam)
    {
        if (GameFolder.Length > 0 && !string.Equals(Path.GetFullPath(GameFolder), Path.GetFullPath(usual.GameFolder), StringComparison.OrdinalIgnoreCase)
            && game.Detect(steam, GameFolder, DataFolder) is { } other)
            return other;
        return usual with { DataFolder = DataFolder };
    }
}

/// <summary>The player's instances, one folder each under <see cref="Folder"/>.</summary>
public sealed class InstanceStore
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public string Folder { get; }

    public InstanceStore(string folder) => Folder = folder;

    public IReadOnlyList<GameInstance> All(string appId)
    {
        if (!Directory.Exists(Folder)) return Array.Empty<GameInstance>();
        var list = new List<GameInstance>();
        foreach (var dir in Directory.GetDirectories(Folder))
            if (Load(dir) is { } i && i.AppId == appId) list.Add(i);
        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public GameInstance? Get(string appId, string name) => All(appId).FirstOrDefault(i => i.Name == name);

    static GameInstance? Load(string dir)
    {
        try
        {
            var file = Path.Combine(dir, GameInstance.FileName);
            if (!File.Exists(file)) return null;
            var i = JsonSerializer.Deserialize<GameInstance>(File.ReadAllText(file), Opts);
            if (i == null || i.Format > GameInstance.FormatVersion || i.Name.Trim().Length == 0) return null;
            i.Folder = dir;
            return i;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Makes a new instance. <paramref name="settingsFrom"/> (the usual data folder) gives it the player's game options
    /// and key bindings; <paramref name="active"/> becomes its mod list. Throws <see cref="IOException"/> when the name is taken.
    /// </summary>
    public GameInstance Create(IGameModule game, GameInstall usual, string name, string gameFolder, string? settingsFrom, IReadOnlyList<string>? active)
    {
        name = name.Trim();
        if (name.Length == 0) throw new IOException("Give the instance a name.");
        if (All(game.AppId).Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))) throw new IOException($"There is already an instance called {name}.");
        var dir = FreeFolder(name);
        var inst = new GameInstance { Name = name, AppId = game.AppId, GameFolder = gameFolder.Trim(), Created = DateTime.UtcNow, Folder = dir };
        Directory.CreateDirectory(Path.Combine(inst.DataFolder, "Config"));
        Directory.CreateDirectory(Path.Combine(inst.DataFolder, "Saves"));
        if (settingsFrom != null)
            foreach (var rel in game.SettingsFiles)
            {
                var from = Path.Combine(settingsFrom, rel);
                if (File.Exists(from)) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(inst.DataFolder, rel))!); File.Copy(from, Path.Combine(inst.DataFolder, rel), true); }
            }
        Save(inst);
        if (active != null) game.WriteActiveMods(usual with { DataFolder = inst.DataFolder }, active);
        return inst;
    }

    public void Save(GameInstance inst)
    {
        Directory.CreateDirectory(inst.Folder);
        SafeFiles.WriteWithBackup(Path.Combine(inst.Folder, GameInstance.FileName), JsonSerializer.Serialize(inst, Opts), keep: 0);
    }

    /// <summary>Deletes the instance with its saves. Only folders inside <see cref="Folder"/> that hold an instance file are deleted.</summary>
    public void Delete(GameInstance inst)
    {
        var dir = Path.GetFullPath(inst.Folder);
        if (!IsInside(dir, Folder) || !File.Exists(Path.Combine(dir, GameInstance.FileName))) throw new IOException("That is not an instance folder.");
        Directory.Delete(dir, true);
    }

    /// <summary>How many saves an instance has (shown before deleting it).</summary>
    public static int SaveCount(GameInstance inst)
    {
        var saves = Path.Combine(inst.DataFolder, "Saves");
        try { return Directory.Exists(saves) ? Directory.GetFiles(saves, "*.rws").Length : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Packs the instance (its settings, mod list and, when asked, its saves) into a zip to move or share it.</summary>
    public static void Export(GameInstance inst, string zipPath, bool includeSaves)
    {
        var tmp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Path.Combine(inst.Folder, GameInstance.FileName), GameInstance.FileName);
            if (Directory.Exists(inst.DataFolder))
                foreach (var f in Directory.EnumerateFiles(inst.DataFolder, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(inst.DataFolder, f).Replace('\\', '/');
                    if (!includeSaves && rel.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    zip.CreateEntryFromFile(f, GameInstance.DataDir + "/" + rel);
                }
        }
        File.Move(tmp, zipPath, overwrite: true);
    }

    /// <summary>
    /// Unpacks an exported instance, under its own name or "name (2)" when that is taken. Only its instance file and its
    /// Data folder are taken from the zip; anything else, and any path that would land outside the instance, is skipped.
    /// Throws <see cref="FormatException"/> when the zip is not an instance.
    /// </summary>
    public GameInstance Import(string zipPath, string appId)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var head = zip.GetEntry(GameInstance.FileName) ?? throw new FormatException("This zip is not an exported instance (it has no instance.json).");
        GameInstance? inst;
        using (var r = new StreamReader(head.Open()))
        {
            try { inst = JsonSerializer.Deserialize<GameInstance>(r.ReadToEnd(), Opts); }
            catch (JsonException e) { throw new FormatException("The instance file in the zip is not valid: " + e.Message, e); }
        }
        if (inst == null || inst.Name.Trim().Length == 0) throw new FormatException("The instance file in the zip is empty.");
        if (inst.Format > GameInstance.FormatVersion) throw new FormatException($"The instance was made by a newer version of the app (format {inst.Format}).");
        if (inst.AppId != appId) throw new FormatException("The instance is for another game.");

        var names = All(appId).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = inst.Name.Trim();
        for (var n = 2; names.Contains(name); n++) name = $"{inst.Name.Trim()} ({n})";
        inst.Name = name;
        inst.Created = DateTime.UtcNow;
        // Another PC's game folder means nothing here.
        if (inst.GameFolder.Length > 0 && !Directory.Exists(inst.GameFolder)) inst.GameFolder = "";
        inst.Folder = FreeFolder(name);
        Directory.CreateDirectory(Path.Combine(inst.DataFolder, "Config"));
        foreach (var e in zip.Entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (!rel.StartsWith(GameInstance.DataDir + "/", StringComparison.Ordinal) || rel.EndsWith('/')) continue;
            var dest = Path.GetFullPath(Path.Combine(inst.Folder, rel));
            if (!IsInside(dest, inst.DataFolder)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
        Save(inst);
        return inst;
    }

    /// <summary>A folder for a new instance named <paramref name="name"/> that doesn't exist yet.</summary>
    string FreeFolder(string name)
    {
        var safe = SafeFiles.SafeName(name, "instance");
        var dir = Path.Combine(Folder, safe);
        for (var n = 2; Directory.Exists(dir); n++) dir = Path.Combine(Folder, $"{safe} ({n})");
        return dir;
    }

    static bool IsInside(string path, string folder)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
