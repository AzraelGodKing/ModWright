using System.Diagnostics;
using System.Text.Json;

namespace SteamCmdHelper.DropPod;

/// <summary>
/// Folder links (Windows directory junctions, which need no administrator rights): a folder in the game's Mods folder
/// that is really another folder somewhere else. Used to play a mod straight from its development folder, and to put an
/// instance's frozen Workshop copies in front of Steam's while it plays.
/// </summary>
public static class FolderLinks
{
    public static bool IsLink(string path)
    {
        try { return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Where a link points, or null when <paramref name="path"/> is not a link.</summary>
    public static string? Target(string path)
    {
        if (!IsLink(path)) return null;
        try { return new DirectoryInfo(path).LinkTarget is { } t ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, t)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Makes <paramref name="link"/> point at <paramref name="target"/>. Throws <see cref="IOException"/> when something is already there.</summary>
    public static void Create(string link, string target)
    {
        if (Directory.Exists(link) || File.Exists(link)) throw new IOException($"{link} already exists.");
        if (!Directory.Exists(target)) throw new IOException($"{target} does not exist.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(link))!);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        // A junction, not a symbolic link: symbolic links need administrator rights or Developer Mode.
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException("Could not start cmd.exe to make the link.");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        if (p.ExitCode != 0 || !IsLink(link)) throw new IOException("Windows could not make the link: " + (err.Trim().Length > 0 ? err.Trim() : $"exit code {p.ExitCode}"));
    }

    /// <summary>Removes a link (never what it points at). Does nothing when <paramref name="link"/> is not a link.</summary>
    public static bool Remove(string link)
    {
        if (!IsLink(link)) return false;
        Directory.Delete(link, recursive: false);
        return true;
    }
}

/// <summary>A mod's development folder linked into the game's Mods folder.</summary>
public sealed record DevLink(string Link, string Folder);

/// <summary>
/// Play the mod you're working on: its folder is linked into RimWorld's Mods folder, so each build is in the game at
/// once with nothing copied. The links this app made are remembered so it only ever removes its own.
/// </summary>
public sealed class DevLinks
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    readonly string _file;

    public DevLinks(string file) => _file = file;

    /// <summary>The links made, dropping ones that are gone.</summary>
    public List<DevLink> All()
    {
        try
        {
            var list = File.Exists(_file) ? JsonSerializer.Deserialize<List<DevLink>>(File.ReadAllText(_file), Opts) ?? new() : new();
            return list.Where(l => FolderLinks.IsLink(l.Link)).ToList();
        }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    public DevLink? For(string modFolder) =>
        All().FirstOrDefault(l => string.Equals(Path.GetFullPath(l.Folder), Path.GetFullPath(modFolder), StringComparison.OrdinalIgnoreCase));

    /// <summary>Links <paramref name="modFolder"/> into <paramref name="gameMods"/> under its own folder name (RimWorld keys mod settings by it).</summary>
    public DevLink Add(string gameMods, string modFolder)
    {
        if (For(modFolder) is { } have) return have;
        var link = Path.Combine(gameMods, Path.GetFileName(Path.GetFullPath(modFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        if (Directory.Exists(link)) throw new IOException($"RimWorld's Mods folder already has a folder called {Path.GetFileName(link)}. Move it away first; it is not replaced.");
        FolderLinks.Create(link, modFolder);
        var all = All();
        all.Add(new DevLink(link, Path.GetFullPath(modFolder)));
        Save(all);
        return all[^1];
    }

    public bool Remove(string modFolder)
    {
        var all = All();
        var l = For(modFolder);
        if (l == null) return false;
        FolderLinks.Remove(l.Link);
        Save(all.Where(x => x != l).ToList());
        return true;
    }

    void Save(List<DevLink> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        SafeFiles.WriteWithBackup(_file, JsonSerializer.Serialize(all, Opts), keep: 0);
    }
}

/// <summary>One frozen Workshop mod: its id, name and when Steam last updated the copy that was frozen.</summary>
public sealed class FrozenMod
{
    public string WorkshopId { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset Updated { get; set; }
}

/// <summary>
/// Freeze a colony's mods: an instance keeps its own copy of each Workshop mod it uses, so a Steam update halfway
/// through a long run can't change them. RimWorld has no switch for extra mod folders, so while the frozen instance
/// plays, each copy is linked into the game's Mods folder under its Workshop id (RimWorld prefers a Mods folder copy over
/// the Workshop one with the same package id, and keeps the mod's settings, which go by folder name). The links come out
/// again when the game closes.
/// </summary>
public static class Freezer
{
    const string FolderName = "Frozen";
    const string FileName = "frozen.json";
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public static string Folder(GameInstance inst) => Path.Combine(inst.Folder, FolderName);

    public static List<FrozenMod> Read(GameInstance inst)
    {
        var f = Path.Combine(Folder(inst), FileName);
        try { return File.Exists(f) ? JsonSerializer.Deserialize<List<FrozenMod>>(File.ReadAllText(f), Opts) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    public static bool IsFrozen(GameInstance inst) => Read(inst).Count > 0;

    /// <summary>
    /// Copies the active Workshop mods (from Steam's Workshop folder) into the instance. <paramref name="steamTimes"/> is when
    /// Steam last updated each installed item (the client's appworkshop file). Returns what was frozen.
    /// </summary>
    public static List<FrozenMod> Freeze(GameInstance inst, IEnumerable<ModManifest> active, IReadOnlyDictionary<string, DateTimeOffset> steamTimes, Action<string>? progress = null)
    {
        var dir = Folder(inst);
        var list = new List<FrozenMod>();
        var tmp = dir + ".incoming";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        foreach (var m in active.Where(m => m.Source == ModSource.Workshop && m.WorkshopId != null).DistinctBy(m => m.WorkshopId))
        {
            progress?.Invoke(m.DisplayName);
            WorkshopDownload.CopyTo(m.Folder, Path.Combine(tmp, m.WorkshopId!));
            list.Add(new FrozenMod { WorkshopId = m.WorkshopId!, Key = m.Key, Name = m.DisplayName, Updated = steamTimes.GetValueOrDefault(m.WorkshopId!) });
        }
        File.WriteAllText(Path.Combine(tmp, FileName), JsonSerializer.Serialize(list, Opts));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(tmp, dir);
        return list;
    }

    public static void Unfreeze(GameInstance inst)
    {
        var dir = Folder(inst);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    /// <summary>Frozen mods that Steam has a newer version of now (waiting for when you unfreeze).</summary>
    public static List<FrozenMod> Waiting(GameInstance inst, IReadOnlyDictionary<string, DateTimeOffset> steamTimes) =>
        Read(inst).Where(f => steamTimes.TryGetValue(f.WorkshopId, out var now) && now - f.Updated > TimeSpan.FromMinutes(1)).ToList();

    /// <summary>
    /// Links each frozen copy into <paramref name="gameMods"/> as "&lt;workshop id&gt;". A Mods folder entry of that name that
    /// isn't such a link (a copy the Drop Pod downloaded) is left alone: it doesn't update by itself anyway. Returns the links made.
    /// </summary>
    public static List<string> LinkIn(GameInstance inst, string gameMods)
    {
        var made = new List<string>();
        foreach (var f in Read(inst))
        {
            var target = Path.Combine(Folder(inst), f.WorkshopId);
            var link = Path.Combine(gameMods, f.WorkshopId);
            if (!Directory.Exists(target)) continue;
            if (FolderLinks.IsLink(link))
            {
                if (IsFrozenLink(link)) FolderLinks.Remove(link); else continue;
            }
            else if (Directory.Exists(link)) continue;
            FolderLinks.Create(link, target);
            made.Add(link);
        }
        return made;
    }

    /// <summary>Removes every link into an instance's frozen copies from <paramref name="gameMods"/> (after the game closes, or left over from a crash).</summary>
    public static int Unlink(string gameMods)
    {
        var n = 0;
        try
        {
            if (!Directory.Exists(gameMods)) return 0;
            foreach (var d in Directory.GetDirectories(gameMods))
                if (IsFrozenLink(d) && FolderLinks.Remove(d)) n++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return n;
    }

    static bool IsFrozenLink(string path) =>
        FolderLinks.Target(path) is { } t && Path.GetFileName(Path.GetDirectoryName(t.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!) == FolderName;
}
