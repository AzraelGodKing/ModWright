using System.Text.Json;

namespace SteamCmdHelper.DropPod;

/// <summary>What one optimise run did for a mod.</summary>
public sealed record TextureResult(int Converted, int Skipped, long PngBytes, long DdsBytes, List<string> Errors);

/// <summary>
/// Turns a mod's PNG textures into DDS files next to them, which RimWorld (1.5 and later) loads instead of the PNG:
/// quicker to load and much smaller in video memory. The PNGs are never changed. Which DDS files Modwright made is kept
/// in its own folder, so they can be redone when a mod updates its PNGs and removed again with Undo; DDS files a mod
/// ships itself are never touched.
/// </summary>
public sealed class TextureOptimizer
{
    readonly string _records;

    /// <param name="records">The folder for the per-mod lists of DDS files made here.</param>
    public TextureOptimizer(string records) => _records = records;

    string RecordFor(ModManifest mod) => Path.Combine(_records, SafeFiles.SafeName(mod.Key, "mod") + ".json");

    /// <summary>The DDS files Modwright made in this mod (paths relative to its folder).</summary>
    public List<string> Made(ModManifest mod)
    {
        try { return File.Exists(RecordFor(mod)) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecordFor(mod))) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    void Save(ModManifest mod, IEnumerable<string> made)
    {
        var list = made.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) { try { File.Delete(RecordFor(mod)); } catch (IOException) { } return; }
        Directory.CreateDirectory(_records);
        File.WriteAllText(RecordFor(mod), JsonSerializer.Serialize(list));
    }

    /// <summary>The PNGs in the mod's Textures folders (the root's, Common's and version folders'), as full paths.</summary>
    public static List<string> Pngs(string modFolder)
    {
        var list = new List<string>();
        void Look(string dir, int depth)
        {
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (name.Equals("Textures", StringComparison.OrdinalIgnoreCase))
                    try { list.AddRange(Directory.EnumerateFiles(sub, "*.png", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive })); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                else if (depth < 2 && !name.StartsWith('.') && !name.Equals("Source", StringComparison.OrdinalIgnoreCase)) Look(sub, depth + 1);
            }
        }
        Look(modFolder, 0);
        return list;
    }

    /// <summary>
    /// The PNGs that need a DDS: no DDS beside them yet, or one Modwright made that is older than the PNG. A PNG whose
    /// DDS came with the mod is left alone.
    /// </summary>
    public List<string> ToDo(ModManifest mod)
    {
        var mine = Made(mod).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Pngs(mod.Folder).Where(png =>
        {
            var dds = Path.ChangeExtension(png, ".dds");
            if (!File.Exists(dds)) return true;
            return mine.Contains(Rel(mod, dds)) && File.GetLastWriteTimeUtc(dds) < File.GetLastWriteTimeUtc(png);
        }).ToList();
    }

    static string Rel(ModManifest mod, string path) => Path.GetRelativePath(mod.Folder, path).Replace('\\', '/');

    /// <summary>Makes the DDS files. <paramref name="decode"/> reads a PNG as RGBA pixels (top row first).</summary>
    public TextureResult Optimize(ModManifest mod, Func<string, (byte[] Rgba, int Width, int Height)> decode, CancellationToken ct, Action<int, int>? progress = null)
    {
        var todo = ToDo(mod);
        var made = Made(mod);
        var errors = new List<string>();
        int converted = 0, skipped = 0;
        long png = 0, dds = 0;
        for (var i = 0; i < todo.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(i, todo.Count);
            var file = todo[i];
            try
            {
                var (rgba, w, h) = decode(file);
                if (!DdsEncoder.CanEncode(w, h)) { skipped++; continue; } // RimWorld keeps using the PNG
                var bytes = DdsEncoder.Encode(rgba, w, h);
                var target = Path.ChangeExtension(file, ".dds");
                var tmp = target + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, target, overwrite: true);
                made.Add(Rel(mod, target));
                converted++;
                png += new FileInfo(file).Length;
                dds += bytes.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
            {
                errors.Add($"{Rel(mod, file)}: {e.Message}");
            }
        }
        Save(mod, made);
        return new TextureResult(converted, skipped, png, dds, errors);
    }

    /// <summary>Removes the DDS files Modwright made in the mod. Returns how many.</summary>
    public int Undo(ModManifest mod)
    {
        var n = 0;
        foreach (var rel in Made(mod))
        {
            var full = Path.GetFullPath(Path.Combine(mod.Folder, rel));
            if (!full.StartsWith(Path.GetFullPath(mod.Folder), StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) continue;
            try { if (File.Exists(full)) { File.Delete(full); n++; } }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        Save(mod, Array.Empty<string>());
        return n;
    }

    /// <summary>The mods worth offering: active, not the game's own, with PNG textures still to convert, most bytes first.</summary>
    public List<(ModManifest Mod, int Files, long Bytes)> Candidates(IEnumerable<ModManifest> active)
    {
        var list = new List<(ModManifest, int, long)>();
        foreach (var m in active.Where(m => m.Source != ModSource.Official))
        {
            var todo = ToDo(m);
            if (todo.Count > 0) list.Add((m, todo.Count, todo.Sum(f => new FileInfo(f).Length)));
        }
        return list.OrderByDescending(x => x.Item3).ToList();
    }
}
