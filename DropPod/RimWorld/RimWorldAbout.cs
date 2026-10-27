using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SteamCmdHelper.DropPod.RimWorld;

/// <summary>
/// Reads RimWorld's About/About.xml (the &lt;ModMetaData&gt; file every mod has) into a <see cref="ModManifest"/>. Written from
/// the documented file format. Tolerant on purpose: element names are matched in any case, old mods with targetVersion
/// instead of supportedVersions still read, and a file that is not valid XML is read as far as possible with a note.
/// </summary>
public static class RimWorldAbout
{
    public const string AboutPath = "About/About.xml";
    public const string PreviewPath = "About/Preview.png";
    public const string PublishedIdPath = "About/PublishedFileId.txt";

    /// <summary>
    /// Reads the mod in <paramref name="folder"/>; null when it has no About.xml (not a mod), except for the game's own
    /// Data folders (Core and the expansions), which the game loads with or without one.
    /// </summary>
    public static ModManifest? ReadFolder(string folder, ModSource source)
    {
        var about = SafeFiles.Find(folder, AboutPath);
        if (about == null)
            return source == ModSource.Official ? Finish(new ModManifest { Folder = folder, Source = source }) : null;
        string text;
        try { text = File.ReadAllText(about); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var broken = new ModManifest { Folder = folder, Source = source };
            broken.Issues.Add("About.xml could not be read: " + e.Message);
            return broken;
        }
        var m = Parse(text, folder, source);
        m.PreviewPath = SafeFiles.Find(folder, PreviewPath);
        m.WorkshopId = WorkshopIdOf(folder, source);
        return m;
    }

    /// <summary>The Workshop id from About/PublishedFileId.txt, or for a Workshop copy its folder name (Steam names the folder by id).</summary>
    public static string? WorkshopIdOf(string folder, ModSource source)
    {
        try
        {
            if (SafeFiles.Find(folder, PublishedIdPath) is { } f && File.ReadAllText(f).Trim() is var id && Regex.IsMatch(id, @"^\d{3,}$")) return id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var name = Path.GetFileName(folder.TrimEnd('/', '\\'));
        return source == ModSource.Workshop && Regex.IsMatch(name, @"^\d{3,}$") ? name : null;
    }

    /// <summary>Reads About.xml text. <paramref name="folder"/> is only recorded on the result.</summary>
    public static ModManifest Parse(string xml, string folder = "", ModSource source = ModSource.Local)
    {
        var m = new ModManifest { Folder = folder, Source = source };
        XElement? root;
        try { root = SafeFiles.ParseXml(xml).Root; }
        catch (XmlException e)
        {
            m.Issues.Add($"About.xml is not valid XML (line {e.LineNumber}): {e.Message.Split(" Line ")[0]}. Read what could be found.");
            ReadLoosely(xml, m);
            Finish(m);
            return m;
        }
        if (root == null) { m.Issues.Add("About.xml is empty."); return m; }
        if (!root.Name.LocalName.Equals("ModMetaData", StringComparison.OrdinalIgnoreCase))
            m.Issues.Add($"About.xml starts with <{root.Name.LocalName}> instead of <ModMetaData>.");

        m.Name = Text(root, "name");
        m.Id = Text(root, "packageId");
        m.Description = Text(root, "description");
        m.ModVersion = Text(root, "modVersion");
        m.Url = Text(root, "url");
        foreach (var a in Items(El(root, "authors"))) AddAuthors(m, a);
        AddAuthors(m, Text(root, "author"));
        m.SupportedVersions.AddRange(Items(El(root, "supportedVersions")).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct());
        if (m.SupportedVersions.Count == 0 && Text(root, "targetVersion") is { Length: > 0 } target) m.SupportedVersions.Add(GameVersion.Short(target));

        ReadDeps(El(root, "modDependencies"), m.Dependencies.All);
        foreach (var (v, el) in ByVersion(El(root, "modDependenciesByVersion")))
            ReadDeps(el, m.Dependencies.ByVersion[v] = new List<ModDependency>());
        ReadIds(root, "loadAfter", m.LoadAfter);
        ReadIds(root, "loadBefore", m.LoadBefore);
        ReadIds(root, "forceLoadAfter", m.ForceLoadAfter);
        ReadIds(root, "forceLoadBefore", m.ForceLoadBefore);
        ReadIds(root, "incompatibleWith", m.Incompatible);
        Finish(m);
        return m;
    }

    static ModManifest Finish(ModManifest m)
    {
        // The game names its Data folders' content itself (Core is Ludeon.RimWorld, Royalty is Ludeon.RimWorld.Royalty),
        // and Ludeon's own content never needs the fields a mod author is asked for.
        if (m.Id.Length == 0 && m.Source == ModSource.Official) m.Id = RimWorldIds.OfficialIdOf(m.FolderName);
        if (RimWorldIds.IsOfficial(m.Key))
        {
            if (m.Authors.Count == 0) m.Authors.Add("Ludeon Studios");
            return m;
        }
        if (m.Id.Length == 0) m.Issues.Add("About.xml has no packageId, so other mods can't refer to it and the game may refuse it.");
        else if (!RimWorldIds.IsValid(m.Id)) m.Issues.Add($"packageId \"{m.Id}\" is not valid for RimWorld (letters and digits with at least one dot).");
        if (m.Name.Length == 0) m.Issues.Add("About.xml has no name.");
        return m;
    }

    static void AddAuthors(ModManifest m, string text)
    {
        foreach (var a in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!m.Authors.Contains(a, StringComparer.OrdinalIgnoreCase)) m.Authors.Add(a);
    }

    static void ReadIds(XElement root, string name, VersionedList<string> into)
    {
        into.All.AddRange(Ids(El(root, name)));
        foreach (var (v, el) in ByVersion(El(root, name + "ByVersion"))) into.ByVersion[v] = Ids(el).ToList();
    }

    static IEnumerable<string> Ids(XElement? list) => Items(list).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    static void ReadDeps(XElement? list, List<ModDependency> into)
    {
        if (list == null) return;
        foreach (var li in list.Elements().Where(e => e.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            var id = Text(li, "packageId");
            var url = Text(li, "steamWorkshopUrl");
            var download = Text(li, "downloadUrl");
            var workshop = Dependencies.FindIn(url).Concat(Dependencies.FindIn(download)).FirstOrDefault();
            var display = Text(li, "displayName");
            if (id.Length == 0 && workshop == null && display.Length == 0 && url.Length == 0 && download.Length == 0) continue;
            into.Add(new ModDependency(id, NullIfEmpty(display), workshop, NullIfEmpty(url.Length > 0 ? url : download)));
        }
    }

    /// <summary>The children of a "...ByVersion" element, keyed by "major.minor" ("v1.4" gives "1.4").</summary>
    static IEnumerable<(string Version, XElement El)> ByVersion(XElement? el) =>
        el == null ? Enumerable.Empty<(string, XElement)>() : el.Elements().Select(e => (GameVersion.Short(e.Name.LocalName), e));

    static XElement? El(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    static string Text(XElement parent, string name) => El(parent, name)?.Value.Trim() ?? "";

    static IEnumerable<string> Items(XElement? list) =>
        list == null ? Enumerable.Empty<string>() : list.Elements().Where(e => e.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)).Select(e => e.Value);

    static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    // ---------------------------------------------------------------- broken XML

    const RegexOptions Ic = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    /// <summary>For About.xml that does not parse (a stray "&amp;" is the usual cause): picks out the simple fields by pattern.</summary>
    static void ReadLoosely(string xml, ModManifest m)
    {
        string One(string tag) => Regex.Match(xml, $@"<{tag}\s*>(.*?)</{tag}\s*>", Ic) is { Success: true } x ? Unescape(x.Groups[1].Value.Trim()) : "";
        IEnumerable<string> List(string tag) => Regex.Match(xml, $@"<{tag}\s*>(.*?)</{tag}\s*>", Ic) is { Success: true } x
            ? Regex.Matches(x.Groups[1].Value, @"<li\s*>(.*?)</li\s*>", Ic).Select(l => Unescape(l.Groups[1].Value.Trim())).Where(s => s.Length > 0 && !s.Contains('<'))
            : Enumerable.Empty<string>();
        m.Name = One("name");
        m.Id = One("packageId");
        AddAuthors(m, One("author"));
        foreach (var a in List("authors")) AddAuthors(m, a);
        m.SupportedVersions.AddRange(List("supportedVersions"));
        m.LoadAfter.All.AddRange(List("loadAfter"));
        m.LoadBefore.All.AddRange(List("loadBefore"));
        m.Incompatible.All.AddRange(List("incompatibleWith"));
        var deps = Regex.Match(xml, @"<modDependencies\s*>(.*?)</modDependencies\s*>", Ic);
        if (deps.Success)
            foreach (Match li in Regex.Matches(deps.Groups[1].Value, @"<li\s*>(.*?)</li\s*>", Ic))
                if (Regex.Match(li.Groups[1].Value, @"<packageId\s*>(.*?)</packageId\s*>", Ic) is { Success: true } id)
                    m.Dependencies.All.Add(new ModDependency(Unescape(id.Groups[1].Value.Trim()), null, Dependencies.FindIn(li.Groups[1].Value).FirstOrDefault(), null));
    }

    static string Unescape(string s) =>
        s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
}

/// <summary>RimWorld's rules for packageIds.</summary>
public static class RimWorldIds
{
    // RimWorld's own rule (ModMetaData.PackageIdIsValid): letters, digits and dots, with at least one dot inside.
    static readonly Regex Valid = new(@"^[A-Za-z0-9]+(\.[A-Za-z0-9]+)+$");

    public static bool IsValid(string id) => Valid.IsMatch(id.Trim());

    /// <summary>The base game's id; every list starts with it.</summary>
    public const string Core = "ludeon.rimworld";

    /// <summary>
    /// True for Ludeon's own content: the base game, its expansions and anything Ludeon adds later (ids starting
    /// "ludeon."), so a new expansion needs no change here.
    /// </summary>
    public static bool IsOfficial(string key) => ModManifest.KeyOf(key).StartsWith("ludeon.", StringComparison.Ordinal);

    /// <summary>The id the game gives a folder in its Data folder: Core is Ludeon.RimWorld, Royalty Ludeon.RimWorld.Royalty.</summary>
    public static string OfficialIdOf(string folderName) =>
        folderName.Equals("Core", StringComparison.OrdinalIgnoreCase) ? "Ludeon.RimWorld" : "Ludeon.RimWorld." + folderName;

    /// <summary>
    /// The id without the "_steam" ending the game gives the Workshop copy of a mod that is also in the Mods folder, so an
    /// entry in ModsConfig.xml matches either copy.
    /// </summary>
    public static string WithoutSteamSuffix(string key) => key.EndsWith("_steam", StringComparison.Ordinal) ? key[..^"_steam".Length] : key;
}
