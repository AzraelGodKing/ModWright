using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace SteamCmdHelper.DropPod;

/// <summary>One mod in a modlist: its id, and enough to find it again when it is not installed.</summary>
public sealed record ModlistEntry(string Id, string? Name = null, string? WorkshopId = null);

/// <summary>A named load order the player keeps: switch to it, share it, or import it on another PC.</summary>
public sealed class Modlist
{
    public const int FormatVersion = 1;
    public int Format { get; set; } = FormatVersion;
    public string Name { get; set; } = "";
    /// <summary>The Steam app id of the game it is for.</summary>
    public string AppId { get; set; } = "";
    /// <summary>The game version it was saved on ("1.5.4104 rev435"), or "".</summary>
    public string GameVersion { get; set; } = "";
    public List<ModlistEntry> Mods { get; set; } = new();
    public DateTime Saved { get; set; }

    /// <summary>A list of the active mods (ids in load order), with names and Workshop ids filled in from the library.</summary>
    public static Modlist From(string name, string appId, string? gameVersion, IEnumerable<string> activeIds, IReadOnlyDictionary<string, ModManifest> byId, Func<string, string> key)
    {
        var list = new Modlist { Name = name, AppId = appId, GameVersion = gameVersion ?? "", Saved = DateTime.UtcNow };
        foreach (var id in activeIds)
        {
            var k = key(id);
            if (list.Mods.Any(e => key(e.Id) == k)) continue;
            list.Mods.Add(byId.TryGetValue(k, out var m) ? new ModlistEntry(m.Id.Length > 0 ? m.Id : id, m.DisplayName, m.WorkshopId) : new ModlistEntry(id));
        }
        return list;
    }

    /// <summary>Which entries are installed (in list order, as the game writes them) and which are missing.</summary>
    public (List<string> Installed, List<ModlistEntry> Missing) Match(LibraryScan scan, IGameModule game)
    {
        var have = scan.ById();
        var installed = new List<string>();
        var missing = new List<ModlistEntry>();
        foreach (var e in Mods)
        {
            var k = game.ActiveKey(e.Id);
            if (have.ContainsKey(k)) { if (!installed.Contains(k)) installed.Add(k); }
            else missing.Add(e);
        }
        return (installed, missing);
    }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    /// <summary>Reads a modlist file: this app's JSON, or RimWorld's own saved modlist (.rml). Throws <see cref="FormatException"/> when it is neither.</summary>
    public static Modlist Parse(string text, string fallbackName = "Imported")
    {
        var t = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (t.StartsWith('{'))
        {
            Modlist? m;
            try { m = JsonSerializer.Deserialize<Modlist>(t, Opts); }
            catch (JsonException e) { throw new FormatException("The modlist file is not valid: " + e.Message, e); }
            if (m == null) throw new FormatException("The modlist file is empty.");
            if (m.Format > FormatVersion) throw new FormatException($"The modlist was made by a newer version of the app (format {m.Format}).");
            m.Mods = m.Mods.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id)).ToList();
            if (m.Name.Length == 0) m.Name = fallbackName;
            return m;
        }
        if (t.StartsWith('<')) return ParseRml(t, fallbackName);
        throw new FormatException("Not a modlist file: expected a .json modlist or RimWorld's .rml.");
    }

    /// <summary>
    /// RimWorld's in-game "Save mod list" file: ids (and Workshop ids and names, when present) as &lt;li&gt; lists. The element
    /// names have changed between versions, so any list named like "modIds"/"ids", "modSteamIds"/"steamIds" and
    /// "modNames"/"names" is read.
    /// </summary>
    static Modlist ParseRml(string xml, string name)
    {
        XDocument doc;
        try { doc = SafeFiles.ParseXml(xml); }
        catch (System.Xml.XmlException e) { throw new FormatException("The modlist file is not valid XML: " + e.Message, e); }
        List<string>? Find(params string[] names) => doc.Descendants()
            .FirstOrDefault(e => names.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase) && e.Elements().Any())
            ?.Elements().Select(e => e.Value.Trim()).ToList();
        var ids = Find("modIds", "ids") ?? throw new FormatException("The .rml file has no list of mod ids.");
        var steam = Find("modSteamIds", "steamIds", "modSteamWorkshopIds") ?? new();
        var names = Find("modNames", "names") ?? new();
        var list = new Modlist
        {
            Name = name,
            AppId = "294100",
            GameVersion = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("gameVersion", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "",
        };
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i].Length == 0) continue;
            var ws = i < steam.Count && steam[i] is { Length: > 2 } s && s != "0" ? s : null;
            list.Mods.Add(new ModlistEntry(ids[i], i < names.Count && names[i].Length > 0 ? names[i] : null, ws));
        }
        return list;
    }

    // ------------------------------------------------------------ share codes

    const string CodePrefix = "DROPPOD1:";

    /// <summary>
    /// The list as one line of text to paste in a chat: "DROPPOD1:" then the name, app id and each "id|workshopId",
    /// deflated and base64url-encoded. Names of mods are left out to keep it short.
    /// </summary>
    public string ToShareCode()
    {
        var sb = new StringBuilder();
        sb.Append(Name.Replace('\n', ' ')).Append('\n').Append(AppId).Append('\n').Append(GameVersion.Replace('\n', ' ')).Append('\n');
        foreach (var e in Mods) sb.Append(e.Id).Append('|').Append(e.WorkshopId).Append('\n');
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize)) z.Write(Encoding.UTF8.GetBytes(sb.ToString()));
        return CodePrefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads a share code (spaces and line breaks from chat apps are ignored). Throws <see cref="FormatException"/> when it is not one.</summary>
    public static Modlist FromShareCode(string code)
    {
        var c = new string(code.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (!c.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase)) throw new FormatException("This is not a Drop Pod share code (they start with " + CodePrefix + ").");
        var b64 = c[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        string text;
        try
        {
            using var z = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var limited = new MemoryStream();
            var buf = new byte[8192];
            int read;
            while ((read = z.Read(buf)) > 0)
            {
                limited.Write(buf, 0, read);
                if (limited.Length > 4_000_000) throw new FormatException("The share code is too large.");
            }
            text = Encoding.UTF8.GetString(limited.ToArray());
        }
        catch (Exception e) when (e is FormatException or InvalidDataException) { throw new FormatException("The share code is damaged; copy all of it.", e); }
        var lines = text.Split('\n');
        if (lines.Length < 3) throw new FormatException("The share code is damaged; copy all of it.");
        var list = new Modlist { Name = lines[0], AppId = lines[1], GameVersion = lines[2] };
        foreach (var line in lines.Skip(3).Where(l => l.Length > 0))
        {
            var parts = line.Split('|');
            if (parts[0].Length > 0) list.Mods.Add(new ModlistEntry(parts[0], null, parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null));
        }
        return list;
    }
}

/// <summary>Modlists saved in a folder, one JSON file each.</summary>
public sealed class ModlistStore
{
    public string Folder { get; }

    public ModlistStore(string folder) => Folder = folder;

    public IReadOnlyList<Modlist> All(string appId)
    {
        if (!Directory.Exists(Folder)) return Array.Empty<Modlist>();
        var lists = new List<Modlist>();
        foreach (var f in Directory.GetFiles(Folder, "*.json"))
        {
            try
            {
                var m = Modlist.Parse(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f));
                if (m.AppId == appId) lists.Add(m);
            }
            catch (Exception e) when (e is FormatException or IOException) { }
        }
        return lists.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(Modlist list) => SafeFiles.WriteWithBackup(PathFor(list.Name), list.ToJson(), keep: 1);

    public void Delete(string name)
    {
        var p = PathFor(name);
        if (File.Exists(p)) File.Delete(p);
    }

    /// <summary>The file for a list name: unsafe characters replaced, so any name maps to a file in <see cref="Folder"/>.</summary>
    public string PathFor(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var safe = new string(name.Trim().Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (safe.Length == 0) safe = "modlist";
        if (safe.Length > 80) safe = safe[..80];
        return Path.Combine(Folder, safe + ".json");
    }
}
