using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace SteamCmdHelper.DropPod.RimWorld;

public enum XmlFindingKind
{
    /// <summary>Two mods define the same def (type and defName); the later one replaces the earlier one completely.</summary>
    DefOverride,
    /// <summary>A mod's patch changes or removes something another mod's patch already changed or added.</summary>
    PatchOverwrite,
    /// <summary>A patch finds nothing to change, so the game logs an error for it.</summary>
    PatchFails,
    /// <summary>Patches of a kind only a mod's own code knows, which can't be checked here.</summary>
    CustomPatches,
    /// <summary>An XML file that could not be read.</summary>
    BrokenFile,
}

/// <param name="Mods">The mods involved (keys): for overrides and overwrites the earlier mod first, the one that wins last.</param>
/// <param name="Targets">What it is about, as "ThingDef Gun_Rifle" or "ThingDef Gun_Rifle > statBases > MarketValue".</param>
public sealed record XmlFinding(XmlFindingKind Kind, IReadOnlyList<string> Mods, IReadOnlyList<string> Targets, string Detail = "");

/// <summary>One mod for the simulation, in load order.</summary>
/// <param name="Official">The base game or a DLC: their overrides of each other are by design and not reported.</param>
public sealed record XmlMod(string Key, string Name, string PackageId, string Folder, bool Official);

/// <summary>
/// Plays RimWorld's XML loading for a list of mods, without the game: every mod's Defs are combined into one document in
/// load order, then each mod's Patches run against it in load order, as the game does. Along the way it notes defs
/// that replace another mod's, patches that change what another mod already changed (the later one wins), and patches
/// that find nothing (the game logs a red error for those). The vanilla patch operations are understood; operations
/// from a mod's own code are counted and skipped.
/// </summary>
public sealed class XmlPatchSim
{
    readonly XmlDocument _doc = new();
    readonly XmlElement _root;
    /// <summary>Who put each def into the document (keyed by the def's element).</summary>
    readonly Dictionary<XmlNode, string> _defBy = new(ReferenceEqualityComparer.Instance);
    /// <summary>The last mod whose patch added or changed a node.</summary>
    readonly Dictionary<XmlNode, string> _changedBy = new(ReferenceEqualityComparer.Instance);
    Dictionary<(string Type, string Name), List<XmlElement>>? _index;
    readonly HashSet<string> _activeIds = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _activeNames = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _official = new(StringComparer.Ordinal);

    // Findings are grouped by kind and mods, so 40 overrides between the same two mods read as one line.
    readonly Dictionary<(XmlFindingKind, string), (List<string> Mods, List<string> Targets, string Detail)> _found = new();
    string _mod = "";

    XmlPatchSim()
    {
        _root = _doc.CreateElement("Defs");
        _doc.AppendChild(_root);
    }

    static readonly XmlReaderSettings ReadSettings = new() { IgnoreComments = true, IgnoreProcessingInstructions = true, DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

    /// <summary>Runs the mods (in load order) and returns what it found, most important first.</summary>
    public static List<XmlFinding> Run(IReadOnlyList<XmlMod> mods, string? gameVersion, CancellationToken ct = default)
    {
        var sim = new XmlPatchSim();
        foreach (var m in mods)
        {
            if (m.PackageId.Length > 0) sim._activeIds.Add(RimWorldIds.WithoutSteamSuffix(m.PackageId.ToLowerInvariant()));
            sim._activeNames.Add(m.Name);
            if (m.Official) sim._official.Add(m.Key);
        }
        bool IsActive(string id) => sim._activeIds.Contains(id.Trim().ToLowerInvariant());
        var folders = mods.ToDictionary(m => m.Key, m => LoadFolders.For(m.Folder, gameVersion, IsActive));

        foreach (var m in mods)
        {
            ct.ThrowIfCancellationRequested();
            sim._mod = m.Key;
            foreach (var file in LoadFolders.Files(folders[m.Key], "Defs")) sim.AddDefs(file);
        }
        foreach (var m in mods)
        {
            sim._mod = m.Key;
            foreach (var file in LoadFolders.Files(folders[m.Key], "Patches"))
            {
                ct.ThrowIfCancellationRequested();
                sim.RunPatchFile(file);
            }
        }
        return sim.Findings();
    }

    void Note(XmlFindingKind kind, IReadOnlyList<string> mods, string target, string detail = "")
    {
        var key = (kind, string.Join("|", mods) + "|" + detail);
        if (!_found.TryGetValue(key, out var f)) _found[key] = f = (mods.ToList(), new List<string>(), detail);
        if (!f.Targets.Contains(target)) f.Targets.Add(target);
    }

    List<XmlFinding> Findings() => _found
        .Select(p => new XmlFinding(p.Key.Item1, p.Value.Mods, p.Value.Targets, p.Value.Detail))
        .OrderBy(f => f.Kind switch { XmlFindingKind.PatchFails => 0, XmlFindingKind.PatchOverwrite => 1, XmlFindingKind.DefOverride => 2, XmlFindingKind.BrokenFile => 3, _ => 4 })
        .ThenByDescending(f => f.Targets.Count)
        .ToList();

    XmlDocument? Load(string file)
    {
        try
        {
            var d = new XmlDocument();
            using var r = XmlReader.Create(file, ReadSettings);
            d.Load(r);
            return d;
        }
        catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException)
        {
            Note(XmlFindingKind.BrokenFile, new[] { _mod }, Path.GetFileName(file), e.Message);
            return null;
        }
    }

    // ---- Defs ----

    void AddDefs(string file)
    {
        if (Load(file)?.DocumentElement is not { } src) return;
        foreach (var child in src.ChildNodes.OfType<XmlElement>().ToList())
        {
            var node = (XmlElement)_doc.ImportNode(child, true);
            if (DefName(node) is { } name && !IsAbstract(node))
            {
                var key = (node.Name, name);
                var index = Index();
                if (index.TryGetValue(key, out var earlier))
                    foreach (var e in earlier.Where(e => _defBy.TryGetValue(e, out var by) && by != _mod).ToList())
                    {
                        var by = _defBy[e];
                        if (!(_official.Contains(by) && _official.Contains(_mod)))
                            Note(XmlFindingKind.DefOverride, new[] { by, _mod }, $"{node.Name} {name}");
                    }
                if (!index.TryGetValue(key, out var list)) index[key] = list = new();
                list.Add(node);
            }
            _root.AppendChild(node);
            _defBy[node] = _mod;
        }
    }

    static string? DefName(XmlElement def) => def["defName"]?.InnerText.Trim() is { Length: > 0 } n ? n : null;

    static bool IsAbstract(XmlElement def) => string.Equals(def.GetAttribute("Abstract"), "true", StringComparison.OrdinalIgnoreCase);

    Dictionary<(string Type, string Name), List<XmlElement>> Index()
    {
        if (_index != null) return _index;
        _index = new();
        foreach (var e in _root.ChildNodes.OfType<XmlElement>())
            if (DefName(e) is { } n)
            {
                if (!_index.TryGetValue((e.Name, n), out var l)) _index[(e.Name, n)] = l = new();
                l.Add(e);
            }
        return _index;
    }

    // ---- Patches ----

    enum Success { Normal, Invert, Always, Never }

    void RunPatchFile(string file)
    {
        if (Load(file)?.DocumentElement is not { } patch) return;
        foreach (var op in patch.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "Operation"))
            if (!Apply(op, out var failed) && failed != null)
                Note(XmlFindingKind.PatchFails, new[] { _mod }, failed, Path.GetFileName(file));
    }

    /// <summary>Runs one operation. False when the game would report it as failed; <paramref name="failedXpath"/> then says which XPath found nothing.</summary>
    bool Apply(XmlElement op, out string? failedXpath)
    {
        failedXpath = null;
        if (!Required(op)) return true;
        var cls = op.GetAttribute("Class");
        var success = Enum.TryParse<Success>(op["success"]?.InnerText.Trim(), true, out var s) ? s : Success.Normal;
        bool ok;
        switch (Short(cls))
        {
            case "PatchOperationSequence":
                ok = true;
                foreach (var li in op["operations"]?.ChildNodes.OfType<XmlElement>() ?? Enumerable.Empty<XmlElement>())
                    if (!Apply(li, out failedXpath)) { ok = false; break; }
                break;
            case "PatchOperationFindMod":
            {
                var wanted = op["mods"]?.ChildNodes.OfType<XmlElement>().Select(e => e.InnerText.Trim()) ?? Enumerable.Empty<string>();
                // Without the branch it needs, the game treats the operation as done.
                var branch = wanted.Any(_activeNames.Contains) ? op["match"] : op["nomatch"];
                ok = branch == null || Apply(branch, out failedXpath);
                break;
            }
            case "PatchOperationConditional":
            {
                var found = Select(op["xpath"]?.InnerText).Count > 0;
                var branch = found ? op["match"] : op["nomatch"];
                ok = branch == null || Apply(branch, out failedXpath);
                break;
            }
            case "PatchOperationTest":
                ok = Select(op["xpath"]?.InnerText).Count > 0;
                if (!ok) failedXpath = op["xpath"]?.InnerText.Trim();
                break;
            case "PatchOperationAdd": ok = PathOp(op, out failedXpath, Add); break;
            case "PatchOperationInsert": ok = PathOp(op, out failedXpath, Insert); break;
            case "PatchOperationRemove": ok = PathOp(op, out failedXpath, (n, _) => Remove(n)); break;
            case "PatchOperationReplace": ok = PathOp(op, out failedXpath, Replace); break;
            case "PatchOperationAttributeAdd": ok = PathOp(op, out failedXpath, (n, o) => SetAttribute(n, o, overwrite: false)); break;
            case "PatchOperationAttributeSet": ok = PathOp(op, out failedXpath, (n, o) => SetAttribute(n, o, overwrite: true)); break;
            case "PatchOperationAttributeRemove": ok = PathOp(op, out failedXpath, (n, o) => { if (n is XmlElement e) { Touch(e, "changes"); e.RemoveAttribute(o["attribute"]?.InnerText.Trim() ?? ""); } }); break;
            case "PatchOperationAddModExtension": ok = PathOp(op, out failedXpath, AddModExtension); break;
            case "PatchOperationSetName": ok = PathOp(op, out failedXpath, SetName); break;
            default:
                Note(XmlFindingKind.CustomPatches, new[] { _mod }, cls);
                return true;
        }
        var result = success switch { Success.Always => true, Success.Never => false, Success.Invert => !ok, _ => ok };
        if (result) failedXpath = null;
        else failedXpath ??= op["xpath"]?.InnerText.Trim() ?? cls;
        // An operation set to "Never" or "Invert" fails on purpose; only a real miss is worth reporting.
        return result || success != Success.Normal;
    }

    static string Short(string cls) => cls[(cls.LastIndexOf('.') + 1)..];

    /// <summary>MayRequire / MayRequireAnyOf: the operation runs only when those mods are active.</summary>
    bool Required(XmlElement op)
    {
        static IEnumerable<string> Ids(string v) => v.Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0);
        var all = Ids(op.GetAttribute("MayRequire")).ToList();
        if (all.Count > 0 && !all.All(_activeIds.Contains)) return false;
        var any = Ids(op.GetAttribute("MayRequireAnyOf")).ToList();
        return any.Count == 0 || any.Any(_activeIds.Contains);
    }

    bool PathOp(XmlElement op, out string? failedXpath, Action<XmlNode, XmlElement> each)
    {
        var xpath = op["xpath"]?.InnerText.Trim();
        var nodes = Select(xpath);
        failedXpath = nodes.Count == 0 ? xpath ?? "(no xpath)" : null;
        foreach (var n in nodes) each(n, op);
        return nodes.Count > 0;
    }

    static readonly Regex ByDefName = new(@"^/?Defs/(\*|[\w.]+)\[\s*defName\s*=\s*[""']([^""']+)[""']\s*\](.*)$", RegexOptions.Compiled);

    /// <summary>The nodes an XPath finds. The usual "Defs/Type[defName="X"]/..." form goes through the def index instead of searching the whole document.</summary>
    List<XmlNode> Select(string? xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return new();
        try
        {
            var m = ByDefName.Match(xpath.Trim());
            if (m.Success && (m.Groups[3].Value.Length == 0 || m.Groups[3].Value.StartsWith('/')))
            {
                var type = m.Groups[1].Value;
                var defs = Index().Where(p => p.Key.Name == m.Groups[2].Value && (type == "*" || p.Key.Type == type))
                    .SelectMany(p => p.Value).Where(e => e.ParentNode == _root);
                var rest = m.Groups[3].Value;
                return rest.Length == 0 ? defs.Cast<XmlNode>().ToList()
                    : defs.SelectMany(d => d.SelectNodes("." + rest)?.Cast<XmlNode>() ?? Enumerable.Empty<XmlNode>()).ToList();
            }
            return _doc.SelectNodes(xpath)?.Cast<XmlNode>().ToList() ?? new();
        }
        catch (XPathException) { return new(); }
    }

    IEnumerable<XmlNode> Value(XmlElement op) =>
        op["value"]?.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType is XmlNodeType.Element or XmlNodeType.Text or XmlNodeType.CDATA)
            .Select(n => _doc.ImportNode(n, true)).ToList() ?? new List<XmlNode>();

    static bool Prepend(XmlElement op) => string.Equals(op["order"]?.InnerText.Trim(), "Prepend", StringComparison.OrdinalIgnoreCase);

    void Add(XmlNode target, XmlElement op)
    {
        var nodes = Value(op).ToList();
        if (Prepend(op)) nodes.Reverse();
        foreach (var n in nodes)
        {
            if (Prepend(op)) target.PrependChild(n); else target.AppendChild(n);
            Mark(n);
        }
        if (target == _root || target == _doc) _index = null;
    }

    void Insert(XmlNode target, XmlElement op)
    {
        if (target.ParentNode is not { } parent) return;
        var nodes = Value(op).ToList();
        var anchor = target;
        foreach (var n in nodes)
        {
            if (Prepend(op)) parent.InsertBefore(n, target);
            else { parent.InsertAfter(n, anchor); anchor = n; }
            Mark(n);
        }
        if (parent == _root) _index = null;
    }

    void Remove(XmlNode target)
    {
        Touch(target, "removes");
        target.ParentNode?.RemoveChild(target);
        if (target is XmlElement e && _defBy.ContainsKey(e)) _index = null;
    }

    void Replace(XmlNode target, XmlElement op)
    {
        if (target.ParentNode is not { } parent) return;
        Touch(target, "replaces");
        var anchor = target;
        foreach (var n in Value(op))
        {
            parent.InsertAfter(n, anchor);
            anchor = n;
            Mark(n);
            if (parent == _root && n is XmlElement ne) _defBy[ne] = _mod;
        }
        parent.RemoveChild(target);
        if (parent == _root) _index = null;
    }

    void SetAttribute(XmlNode target, XmlElement op, bool overwrite)
    {
        if (target is not XmlElement e) return;
        var name = op["attribute"]?.InnerText.Trim();
        if (string.IsNullOrEmpty(name) || (!overwrite && e.HasAttribute(name))) return;
        if (overwrite && e.HasAttribute(name)) Touch(e, "changes");
        e.SetAttribute(name, op["value"]?.InnerText ?? "");
    }

    void AddModExtension(XmlNode target, XmlElement op)
    {
        if (target is not XmlElement e) return;
        var list = e["modExtensions"] ?? (XmlElement)e.AppendChild(_doc.CreateElement("modExtensions"))!;
        foreach (var n in Value(op).Where(n => n is XmlElement))
        {
            var li = _doc.CreateElement("li");
            foreach (XmlAttribute a in n.Attributes!) li.SetAttribute(a.Name, a.Value);
            while (n.FirstChild != null) li.AppendChild(n.FirstChild);
            list.AppendChild(li);
            Mark(li);
        }
    }

    void SetName(XmlNode target, XmlElement op)
    {
        if (target is not XmlElement e || e.ParentNode is not { } parent || op["name"]?.InnerText.Trim() is not { Length: > 0 } name) return;
        Touch(e, "renames");
        var renamed = _doc.CreateElement(name);
        foreach (XmlAttribute a in e.Attributes) renamed.SetAttribute(a.Name, a.Value);
        while (e.FirstChild != null) renamed.AppendChild(e.FirstChild);
        parent.ReplaceChild(renamed, e);
        Mark(renamed);
    }

    /// <summary>Records that the current mod made this node.</summary>
    void Mark(XmlNode n)
    {
        if (n is XmlElement) _changedBy[n] = _mod;
    }

    /// <summary>
    /// The current mod changes or removes <paramref name="n"/>: when another mod's patch made or changed it (or a node
    /// inside it), that mod's change is lost and it's noted.
    /// </summary>
    void Touch(XmlNode n, string verb)
    {
        var others = new HashSet<string>();
        for (var p = n; p != null && p != _root; p = p.ParentNode)
            if (_changedBy.TryGetValue(p, out var by) && by != _mod) { others.Add(by); break; }
        if (n is XmlElement e)
            foreach (XmlNode inner in e.SelectNodes(".//*")!)
                if (_changedBy.TryGetValue(inner, out var by) && by != _mod) others.Add(by);
        foreach (var by in others.Where(o => !_official.Contains(o)))
            Note(XmlFindingKind.PatchOverwrite, new[] { by, _mod }, Describe(n), verb);
        _changedBy[n] = _mod;
    }

    /// <summary>"ThingDef Gun_Rifle > statBases > MarketValue" for a node.</summary>
    string Describe(XmlNode n)
    {
        var path = new List<string>();
        var p = n;
        while (p != null && p.ParentNode != _root && p.ParentNode != null && p != _root)
        {
            path.Add(p.Name);
            p = p.ParentNode;
        }
        if (p is not XmlElement def || p.ParentNode != _root) return n.Name;
        var name = DefName(def) ?? def.GetAttribute("Name");
        path.Reverse();
        return $"{def.Name} {(name.Length > 0 ? name : "(unnamed)")}" + (path.Count > 0 ? " > " + string.Join(" > ", path) : "");
    }
}
