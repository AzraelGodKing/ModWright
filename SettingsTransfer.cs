using System.IO.Compression;
using System.Text.Json;

namespace SteamCmdHelper;

/// <summary>
/// Moves Modwright to another PC: <see cref="Export"/> zips the settings (profiles, per-folder options), the upload
/// history and what the app remembers about past uploads; <see cref="Import"/> puts them back on the new PC. SteamCMD
/// and its saved Steam login are never included, so the new PC logs in once on its own.
/// </summary>
public static class SettingsTransfer
{
    /// <summary>The files in the data folder that travel. Everything else (SteamCMD, logs, crash reports, backups) stays.</summary>
    public static readonly string[] Files = { "settings.json", "history.json", "uploaded.json", "descriptions.json", "stats.json", "game-builds.json" };

    /// <summary>The upload manifests (what each upload sent, for "what changed since the last upload").</summary>
    public const string Manifests = "manifests";

    const string Settings = "settings.json";

    /// <summary>Writes the zip; returns how many files went into it.</summary>
    public static int Export(string zipPath, string? folder = null)
    {
        folder ??= AppSettings.Folder;
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var n = 0;
        foreach (var name in Files)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            zip.CreateEntryFromFile(path, name);
            n++;
        }
        var manifests = Path.Combine(folder, Manifests);
        if (Directory.Exists(manifests))
            foreach (var path in Directory.EnumerateFiles(manifests, "*.json"))
            {
                zip.CreateEntryFromFile(path, Manifests + "/" + Path.GetFileName(path));
                n++;
            }
        return n;
    }

    /// <summary>The settings inside an exported zip, or an error saying why it is not one.</summary>
    public static (AppSettings? Settings, string? Error) Read(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(Settings);
            if (entry == null) return (null, "This file is not a Modwright settings export (it has no settings.json).");
            using var r = new StreamReader(entry.Open());
            var s = JsonSerializer.Deserialize<AppSettings>(r.ReadToEnd());
            return s == null ? (null, "The settings in this file could not be read.") : (s, null);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException)
        {
            return (null, "This file could not be read as a settings export: " + e.Message);
        }
    }

    /// <summary>
    /// Copies the history and records out of the zip into the data folder, after saving the current ones in
    /// backups\before-import-&lt;time&gt;. <paramref name="settings"/> (as read, with any folders moved) is written as the
    /// new settings. Only the files an export makes are taken; anything else in the zip is ignored. Returns the backup folder.
    /// </summary>
    public static string Import(string zipPath, AppSettings settings, string? folder = null, DateTime? now = null)
    {
        folder ??= AppSettings.Folder;
        var backup = Path.Combine(folder, "backups", "before-import-" + (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach (var name in Files)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) File.Copy(path, Path.Combine(backup, name), true);
        }

        using (var zip = ZipFile.OpenRead(zipPath))
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                var wanted = name != Settings && Files.Contains(name)
                    || name.StartsWith(Manifests + "/", StringComparison.Ordinal) && name.Count(c => c == '/') == 1
                       && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !name.Contains("..");
                if (!wanted) continue;
                var target = Path.Combine(folder, ProjectConfig.Norm(name));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Settings), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        return backup;
    }

    /// <summary>
    /// Makes imported settings fit this PC: a SteamCMD path that does not exist here falls back to the default, and
    /// window places from the old PC's screens are dropped.
    /// </summary>
    public static void FitToThisPc(AppSettings s, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        if (s.SteamCmdPath.Length > 0 && !fileExists(s.SteamCmdPath)) s.SteamCmdPath = "";
        foreach (var p in s.Profiles)
            if (p.SteamCmdPath.Length > 0 && !fileExists(p.SteamCmdPath)) p.SteamCmdPath = "";
        s.WindowBounds = Array.Empty<int>();
        s.WindowMaximized = false;
        s.ColumnWidths = Array.Empty<int>();
        s.ColumnLayout = "";
    }
}
