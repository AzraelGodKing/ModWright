using System.IO.Compression;
using System.Xml;

namespace SteamCmdHelper.DropPod.RimWorld;

/// <summary>
/// Reads the header of RimWorld saves (.rws): the game version and the mods in load order, from the &lt;meta&gt; block at
/// the top. Only that block is read, so saves of hundreds of megabytes are quick, and a save cut off further down
/// still reads. Saves are plain XML; gzip-packed ones (by their first bytes) are read too.
/// </summary>
public static class RimWorldSaves
{
    public static IReadOnlyList<string> Files(GameInstall install)
    {
        var dir = SafeFiles.Child(install.DataFolder, "Saves", directory: true);
        if (dir == null) return Array.Empty<string>();
        try { return Directory.GetFiles(dir, "*.rws"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    public static SaveInfo Read(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        DateTime modified = default;
        long size = 0;
        try
        {
            var fi = new FileInfo(path);
            modified = fi.LastWriteTime;
            size = fi.Length;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var (version, mods) = ReadMeta(file, name);
            return new SaveInfo(path, name, modified, size, version, mods);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or InvalidDataException or FormatException)
        {
            return new SaveInfo(path, name, modified, size, "", new Modlist { Name = name, AppId = RimWorldModule.SteamAppId }, "Could not read the save: " + e.Message);
        }
    }

    /// <summary>The game version and mods from a save's &lt;meta&gt; block. Throws <see cref="FormatException"/> when there is none.</summary>
    public static (string Version, Modlist Mods) ReadMeta(Stream raw, string name)
    {
        var stream = new BufferedStream(raw, 64 * 1024);
        Span<byte> head = stackalloc byte[2];
        var n = stream.Read(head);
        stream.Position = 0;
        Stream input = n == 2 && head[0] == 0x1F && head[1] == 0x8B ? new GZipStream(stream, CompressionMode.Decompress) : stream;

        var settings = SafeFiles.XmlSettings(maxChars: 0);
        settings.IgnoreWhitespace = true;
        using var r = XmlReader.Create(input, settings);
        var version = "";
        var ids = new List<string>();
        var steam = new List<string>();
        var names = new List<string>();
        var sawMeta = false;
        // The readers below leave r on the node after what they read, so the loop only moves on by itself otherwise.
        var moved = false;
        while (moved || r.Read())
        {
            moved = false;
            if (r.NodeType == XmlNodeType.Element && r.Depth == 1 && r.LocalName != "meta") break; // past the header
            if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "meta") { sawMeta = true; break; }
            if (r.NodeType != XmlNodeType.Element || r.Depth != 2) continue;
            switch (r.LocalName)
            {
                case "gameVersion": version = r.ReadElementContentAsString().Trim(); moved = true; break;
                case "modIds": ReadList(r, ids); moved = true; break;
                case "modSteamIds": ReadList(r, steam); moved = true; break;
                case "modNames": ReadList(r, names); moved = true; break;
            }
        }
        if (!sawMeta && ids.Count == 0) throw new FormatException("it has no mod list at the top (not a RimWorld save, or cut off).");
        var list = new Modlist { Name = name, AppId = RimWorldModule.SteamAppId, GameVersion = version };
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i].Length == 0) continue;
            var ws = i < steam.Count && steam[i] is { Length: > 2 } s && s != "0" && s.All(char.IsAsciiDigit) ? s : null;
            list.Mods.Add(new ModlistEntry(ids[i], i < names.Count && names[i].Length > 0 ? names[i] : null, ws));
        }
        return (version, list);
    }

    /// <summary>Reads the &lt;li&gt; values of the list the reader is on, leaving it after the list.</summary>
    static void ReadList(XmlReader r, List<string> into)
    {
        if (r.IsEmptyElement) { r.Read(); return; }
        var depth = r.Depth;
        r.Read();
        while (!(r.NodeType == XmlNodeType.EndElement && r.Depth == depth) && !r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "li") into.Add(r.ReadElementContentAsString().Trim());
            else r.Read();
        }
        r.Read();
    }
}
