using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper.DropPod;

/// <summary>A mod both lists have, at different versions.</summary>
public sealed record VersionDiff(string Key, string Name, string Mine, string Theirs);

/// <summary>Comparing your mods with a friend's list before playing together (the Multiplayer mod needs them to match).</summary>
public static class PlayTogether
{
    /// <summary>Mods both have whose versions differ, where both lists know the version.</summary>
    public static List<VersionDiff> Versions(Modlist theirs, LibraryScan scan, IGameModule game)
    {
        var byId = scan.ById();
        var list = new List<VersionDiff>();
        foreach (var e in theirs.Mods)
        {
            var k = game.ActiveKey(e.Id);
            if (e.Version is not { Length: > 0 } v || !byId.TryGetValue(k, out var m) || m.ModVersion.Length == 0) continue;
            if (!string.Equals(m.ModVersion.Trim(), v.Trim(), StringComparison.OrdinalIgnoreCase) && list.All(d => d.Key != k))
                list.Add(new VersionDiff(k, m.DisplayName, m.ModVersion.Trim(), v.Trim()));
        }
        return list;
    }
}

/// <summary>The player's own note and colour on a mod, kept across modlists.</summary>
public sealed class ModNote
{
    public string Note { get; set; } = "";
    /// <summary>One of <see cref="ModNotes.Colors"/>, or "".</summary>
    public string Color { get; set; } = "";
}

/// <summary>Notes and colour tags on mods, by mod key, in one file.</summary>
public sealed class ModNotes
{
    /// <summary>The colours a mod can be tagged with, in menu order.</summary>
    public static readonly string[] Colors = { "Red", "Orange", "Yellow", "Green", "Blue", "Purple" };

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    readonly string _file;
    readonly Dictionary<string, ModNote> _notes;

    public ModNotes(string file)
    {
        _file = file;
        try { _notes = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, ModNote>>(File.ReadAllText(file), Opts) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { _notes = new(); }
        _notes = new Dictionary<string, ModNote>(_notes, StringComparer.Ordinal);
    }

    public ModNote? Get(string key) => _notes.TryGetValue(key, out var n) && (n.Note.Length > 0 || n.Color.Length > 0) ? n : null;

    public IReadOnlyDictionary<string, ModNote> All => _notes;

    /// <summary>Sets a mod's note and colour; both empty removes it. Saves straight away.</summary>
    public void Set(string key, string note, string color)
    {
        note = note.Trim();
        color = Colors.FirstOrDefault(c => c.Equals(color.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
        if (note.Length == 0 && color.Length == 0) _notes.Remove(key);
        else _notes[key] = new ModNote { Note = note, Color = color };
        SafeFiles.WriteWithBackup(_file, JsonSerializer.Serialize(_notes, Opts), keep: 1);
    }
}

/// <summary>A mod folder nothing uses, with its size.</summary>
public sealed record UnusedMod(ModManifest Mod, long Bytes);

/// <summary>A settings file in the game's Config folder whose mod is no longer installed.</summary>
public sealed record OrphanSettings(string File, string ModFolder, long Bytes);

/// <summary>
/// Clean up the hangar: Workshop and Mods-folder mods that no modlist, instance or recent save uses, and settings
/// files left behind by mods that are gone. Nothing is removed here; the Drop Pod shows the list and asks.
/// </summary>
public static class Hangar
{
    /// <param name="used">Keys of every mod some modlist, instance or save uses (as <see cref="IGameModule.ActiveKey"/> gives them).</param>
    public static List<UnusedMod> Unused(LibraryScan scan, IEnumerable<string> used, IGameModule game, Func<string, long>? size = null)
    {
        size ??= FolderSize;
        var inUse = used.Select(game.ActiveKey).ToHashSet(StringComparer.Ordinal);
        return scan.Mods.Where(m => m.Source != ModSource.Official && !inUse.Contains(game.ActiveKey(m.Key)))
            .Select(m => new UnusedMod(m, size(m.Folder)))
            .OrderByDescending(u => u.Bytes).ToList();
    }

    /// <summary>Settings files (RimWorld: Config/Mod_&lt;folder&gt;_&lt;class&gt;.xml) whose mod folder no installed mod has.</summary>
    public static List<OrphanSettings> Orphans(string dataFolder, LibraryScan scan)
    {
        var folders = scan.Mods.Select(m => m.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<OrphanSettings>();
        foreach (var f in ModSettings.Files(dataFolder))
        {
            if (ModSettings.FolderOf(Path.GetFileName(f)) is not { } folder || folders.Contains(folder)) continue;
            long bytes = 0;
            try { bytes = new FileInfo(f).Length; } catch (IOException) { }
            list.Add(new OrphanSettings(f, folder, bytes));
        }
        return list.OrderBy(o => o.ModFolder, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static long FolderSize(string dir)
    {
        try { return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    public static string Size(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}

/// <summary>
/// Mods' own settings (what players set under Options > Mod settings). RimWorld keeps each in the data folder's Config
/// folder as "Mod_&lt;mod folder name&gt;_&lt;settings class&gt;.xml", so they can be found per mod and carried with a modlist.
/// </summary>
public static class ModSettings
{
    static readonly Regex Name = new(@"^Mod_(?<folder>.+)_(?<cls>[^_]+)\.xml$", RegexOptions.IgnoreCase);

    public static IEnumerable<string> Files(string dataFolder)
    {
        var config = Path.Combine(dataFolder, "Config");
        try { return Directory.Exists(config) ? Directory.GetFiles(config, "Mod_*.xml") : Array.Empty<string>(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>The mod folder a settings file belongs to, or null when the name isn't one.</summary>
    public static string? FolderOf(string fileName) => Name.Match(fileName) is { Success: true } m ? m.Groups["folder"].Value : null;

    /// <summary>The settings files of the given mods (by folder name).</summary>
    public static List<string> For(string dataFolder, IEnumerable<ModManifest> mods)
    {
        var folders = mods.Select(m => m.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Files(dataFolder).Where(f => FolderOf(Path.GetFileName(f)) is { } d && folders.Contains(d)).ToList();
    }

    const string ListEntry = "modlist.json";
    const string SettingsDir = "settings/";

    /// <summary>A modlist with its mods' settings files in one zip ("modlist.json" and "settings/Mod_*.xml").</summary>
    public static void Pack(Modlist list, IEnumerable<string> settingsFiles, string zipPath)
    {
        var tmp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry(ListEntry).Open())) w.Write(list.ToJson());
            foreach (var f in settingsFiles.Distinct(StringComparer.OrdinalIgnoreCase)) zip.CreateEntryFromFile(f, SettingsDir + Path.GetFileName(f));
        }
        File.Move(tmp, zipPath, overwrite: true);
    }

    /// <summary>Reads a packed modlist: the list, and its settings files' names and contents. Throws <see cref="FormatException"/> when it isn't one.</summary>
    public static (Modlist List, List<(string Name, byte[] Data)> Settings) Unpack(string zipPath, string fallbackName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var head = zip.GetEntry(ListEntry) ?? throw new FormatException("This zip is not a modlist with settings (it has no modlist.json).");
        Modlist list;
        using (var r = new StreamReader(head.Open())) list = Modlist.Parse(r.ReadToEnd(), fallbackName);
        var settings = new List<(string, byte[])>();
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith(SettingsDir, StringComparison.Ordinal)) continue;
            var name = e.FullName[SettingsDir.Length..];
            // Only plain settings file names: nothing that could land outside the Config folder.
            if (name.Contains('/') || name.Contains('\\') || FolderOf(name) == null || e.Length > 8_000_000) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            settings.Add((name, ms.ToArray()));
        }
        return (list, settings);
    }

    /// <summary>Writes settings files into the data folder's Config folder, keeping a backup of each it replaces. Returns how many were written.</summary>
    public static int Restore(string dataFolder, IEnumerable<(string Name, byte[] Data)> settings)
    {
        var config = Path.Combine(dataFolder, "Config");
        Directory.CreateDirectory(config);
        var n = 0;
        foreach (var (name, data) in settings)
        {
            if (FolderOf(name) == null || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            var dest = Path.Combine(config, name);
            if (File.Exists(dest)) File.Copy(dest, dest + $".{DateTime.Now:yyyyMMdd-HHmmss}.bak", overwrite: true);
            File.WriteAllBytes(dest, data);
            n++;
        }
        return n;
    }
}

/// <summary>Copies of saves made before starting the game with a mod list the save wasn't played with.</summary>
public static class SaveBackups
{
    /// <summary>
    /// Whether starting with <paramref name="active"/> puts <paramref name="save"/> at risk, and why: mods it was played
    /// with that are now off (removing mods can break a save), or new mods (usually fine, but a copy costs nothing).
    /// Null when the lists match or the save couldn't be read.
    /// </summary>
    public static string? Risk(SaveInfo save, IReadOnlyList<string> active, LibraryScan scan, IGameModule game)
    {
        if (save.Error != null || save.Mods.Mods.Count == 0) return null;
        var d = Saves.Compare(save, active, scan, game);
        if (d.Missing.Count == 0 && d.Inactive.Count == 0 && d.Extra.Count == 0) return null;
        var parts = new List<string>();
        var gone = d.Missing.Concat(d.Inactive).Select(e => e.Name ?? e.Id).ToList();
        if (gone.Count > 0) parts.Add($"{gone.Count} mod{(gone.Count == 1 ? "" : "s")} it was played with {(gone.Count == 1 ? "is" : "are")} off now: {string.Join(", ", gone.Take(6))}{(gone.Count > 6 ? ", ..." : "")}. Taking mods out of a running game can break the save.");
        if (d.Extra.Count > 0) parts.Add($"{d.Extra.Count} mod{(d.Extra.Count == 1 ? " is" : "s are")} new to it.");
        return string.Join(" ", parts);
    }

    /// <summary>Copies the save into <paramref name="folder"/> as "name yyyy-MM-dd HHmmss.rws", keeping the newest <paramref name="keep"/>. Returns the copy.</summary>
    public static string Backup(string savePath, string folder, int keep = 20, DateTime? now = null)
    {
        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(savePath)} {(now ?? DateTime.Now):yyyy-MM-dd HHmmss}{Path.GetExtension(savePath)}");
        File.Copy(savePath, dest, overwrite: true);
        foreach (var old in new DirectoryInfo(folder).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).Skip(keep))
            try { old.Delete(); } catch (IOException) { }
        return dest;
    }
}

/// <summary>Extra arguments for starting the game, as the player types them.</summary>
public static class LaunchArgs
{
    /// <summary>Splits a command line the way Windows programs do for simple cases: spaces separate, double quotes group.</summary>
    public static List<string> Split(string text)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (var c in text ?? "")
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any || cur.Length > 0) list.Add(cur.ToString());
                cur.Clear(); any = false;
                continue;
            }
            cur.Append(c);
        }
        if (any || cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    /// <summary>Arguments the Drop Pod sets itself and the player shouldn't repeat (they would point the game elsewhere).</summary>
    public static List<string> Clashes(IEnumerable<string> args) =>
        args.Where(a => a.StartsWith("-savedatafolder", StringComparison.OrdinalIgnoreCase) || a.Equals("-logFile", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Common RimWorld switches, for the launch options window.</summary>
    public static readonly (string Arg, string What)[] Common =
    {
        ("-quicktest", "start straight into a small test map"),
        ("-popupwindow", "borderless window"),
        ("-screen-fullscreen 0", "windowed"),
        ("-force-d3d11", "use DirectX 11"),
        ("-nolog", "write no log"),
    };
}

/// <summary>Where to report a problem to a mod's author, and the text to send.</summary>
public static class BugReport
{
    static readonly Regex GitHub = new(@"^https?://(www\.)?github\.com/(?<owner>[\w.-]+)/(?<repo>[\w.-]+?)(\.git)?(/.*)?$", RegexOptions.IgnoreCase);

    /// <summary>
    /// The page to report on: the mod's GitHub issues (new issue, title and text filled in) when its url is a GitHub
    /// repository, otherwise its Workshop comments, otherwise its url. Null when there is nowhere to go.
    /// </summary>
    public static string? Target(ModManifest mod, string title, string body)
    {
        if (GitHub.Match(mod.Url.Trim()) is { Success: true } g)
        {
            var q = "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body.Length > 6000 ? body[..6000] + "\n..." : body);
            return $"https://github.com/{g.Groups["owner"].Value}/{g.Groups["repo"].Value}/issues/new{q}";
        }
        if (mod.WorkshopId != null) return "https://steamcommunity.com/sharedfiles/filedetails/comments/" + mod.WorkshopId;
        return Uri.TryCreate(mod.Url.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u.ToString() : null;
    }

    /// <summary>A short report of one log problem: what happened, the game and mod versions, and the start of the error.</summary>
    public static string Text(LogIssue issue, ModManifest mod, string? gameVersion, int activeMods)
    {
        var lines = new List<string>
        {
            $"{(issue.Level == LogLevel.Error ? "Error" : "Warning")} with {mod.DisplayName}{(mod.ModVersion.Length > 0 ? " " + mod.ModVersion : "")} on RimWorld {gameVersion ?? "(version unknown)"}, {activeMods} mods active. It came up {issue.Count} time{(issue.Count == 1 ? "" : "s")}.",
            "",
            "```",
        };
        var text = issue.Text.Replace("\r", "");
        var cut = text.Split('\n').Take(30).ToList();
        lines.AddRange(cut);
        if (text.Split('\n').Length > 30) lines.Add("...");
        lines.Add("```");
        lines.Add("");
        lines.Add("(Found with Modwright's Drop Pod log reader.)");
        return string.Join("\n", lines);
    }
}
