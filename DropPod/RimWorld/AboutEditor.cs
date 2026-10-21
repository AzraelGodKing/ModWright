using System.Xml.Linq;

namespace SteamCmdHelper.DropPod.RimWorld;

/// <summary>The About.xml fields the editor shows. Lists are packageIds, except dependencies which carry a name and link.</summary>
public sealed class AboutFields
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> SupportedVersions { get; set; } = new();
    public List<ModDependency> Dependencies { get; set; } = new();
    public List<string> LoadAfter { get; set; } = new();
    public List<string> LoadBefore { get; set; } = new();
    public List<string> Incompatible { get; set; } = new();
}

/// <summary>
/// Reads and writes the common About.xml fields while keeping everything else in the file as it was (by-version lists,
/// comments, fields the editor doesn't know). Writing an empty field removes its element.
/// </summary>
public static class AboutEditor
{
    public static AboutFields Read(string xml)
    {
        var m = RimWorldAbout.Parse(xml);
        return new AboutFields
        {
            Name = m.Name, Author = string.Join(", ", m.Authors), PackageId = m.Id, ModVersion = m.ModVersion, Url = m.Url,
            Description = m.Description, SupportedVersions = m.SupportedVersions.ToList(),
            Dependencies = m.Dependencies.All.ToList(), LoadAfter = m.LoadAfter.All.ToList(),
            LoadBefore = m.LoadBefore.All.ToList(), Incompatible = m.Incompatible.All.ToList(),
        };
    }

    /// <summary>The About.xml text with <paramref name="f"/> written into <paramref name="xml"/> (a new file when it is empty).</summary>
    public static string Write(string? xml, AboutFields f)
    {
        XDocument doc;
        if (string.IsNullOrWhiteSpace(xml)) doc = new XDocument(new XElement("ModMetaData"));
        else
        {
            // Comments stay: they are the author's.
            var settings = SafeFiles.XmlSettings();
            settings.IgnoreComments = false;
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), settings);
            doc = XDocument.Load(reader);
        }
        var root = doc.Root ?? throw new FormatException("About.xml has no root element.");
        Text(root, "name", f.Name);
        // One author in <author>; several in <authors><li>, which RimWorld reads too.
        var authors = f.Author.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        if (authors.Count > 1) { root.Element("author")?.Remove(); List(root, "authors", authors); }
        else { root.Element("authors")?.Remove(); Text(root, "author", authors.FirstOrDefault() ?? ""); }
        Text(root, "packageId", f.PackageId);
        Text(root, "modVersion", f.ModVersion);
        Text(root, "url", f.Url);
        List(root, "supportedVersions", f.SupportedVersions);
        Text(root, "description", f.Description);
        var deps = root.Element("modDependencies");
        if (f.Dependencies.Count == 0) deps?.Remove();
        else
        {
            if (deps == null) root.Add(deps = new XElement("modDependencies"));
            deps.RemoveNodes();
            foreach (var d in f.Dependencies)
            {
                var li = new XElement("li", new XElement("packageId", d.Id));
                if (d.DisplayName is { Length: > 0 } n) li.Add(new XElement("displayName", n));
                if (d.Url is { Length: > 0 } u)
                    li.Add(new XElement(u.Contains("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ? "steamWorkshopUrl" : "downloadUrl", u));
                deps.Add(li);
            }
        }
        List(root, "loadAfter", f.LoadAfter);
        List(root, "loadBefore", f.LoadBefore);
        List(root, "incompatibleWith", f.Incompatible);
        var decl = doc.Declaration ?? new XDeclaration("1.0", "utf-8", null);
        return decl + "\n" + root.ToString() + "\n";
    }

    /// <summary>What the Drop Pod's checks say about <paramref name="f"/>, as it would be written.</summary>
    public static List<LintFinding> Lint(AboutFields f, string? xml = null) => AboutLint.Check(RimWorldAbout.Parse(Write(xml, f)));

    /// <summary>A dependency entry for an installed mod: its packageId, name and Workshop page when it has one.</summary>
    public static ModDependency DependencyOn(ModManifest m) =>
        new(m.Id, m.DisplayName, m.WorkshopId, m.WorkshopId is { } w ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + w : m.Url is { Length: > 0 } u ? u : null);

    static void Text(XElement root, string name, string value)
    {
        var el = root.Element(name);
        if (value.Trim().Length == 0) { el?.Remove(); return; }
        if (el == null) root.Add(el = new XElement(name));
        el.Value = value.Trim();
    }

    static void List(XElement root, string name, IReadOnlyCollection<string> items)
    {
        var el = root.Element(name);
        var clean = items.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (clean.Count == 0) { el?.Remove(); return; }
        if (el == null) root.Add(el = new XElement(name));
        el.RemoveNodes();
        foreach (var i in clean) el.Add(new XElement("li", i));
    }
}
