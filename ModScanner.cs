using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace SteamCmdHelper;

public sealed class ModInfo
{
    public string Dir = "";
    public string Folder = "";
    /// <summary>Path of the mod folder relative to the repo root, with forward slashes (used by git).</summary>
    public string RelPath = "";
    public string Name = "";
    public string Version = "";
    public string? WorkshopId;
    /// <summary>Preview image: true = present and within Steam's 1 MB limit, false = missing or too big, null = the layout has none.</summary>
    public bool? PreviewOk;
    public string PreviewNote = "";
    /// <summary>Full path of the preview image when it exists, for the thumbnail in the list.</summary>
    public string PreviewPath = "";
    public readonly List<string> Problems = new();
    public readonly List<string> Warnings = new();
    /// <summary>The mod folder has uncommitted changes (git packaging uses HEAD, so they would not be uploaded).</summary>
    public bool Dirty;

    /// <summary>The id of the mod's private test copy on Steam (<see cref="TestIdFile"/>), or null when it has none yet.</summary>
    public string? TestWorkshopId;

    /// <summary>True for the copy <see cref="AsTestItem"/> makes: uploads go to the test item.</summary>
    public bool IsTest { get; private set; }

    /// <summary>The file in the mod folder that holds the test item's id; it is never packaged.</summary>
    public const string TestIdFile = "workshop_id.test.txt";

    /// <summary>This mod aimed at its test item: the test id stands in for the Workshop id (null = create one).</summary>
    public ModInfo AsTestItem()
    {
        var t = new ModInfo
        {
            Dir = Dir, Folder = Folder, RelPath = RelPath, Name = Name, Version = Version, WorkshopId = TestWorkshopId,
            PreviewOk = PreviewOk, PreviewNote = PreviewNote, PreviewPath = PreviewPath, Dirty = Dirty, TestWorkshopId = TestWorkshopId, IsTest = true,
        };
        t.Problems.AddRange(Problems);
        t.Warnings.AddRange(Warnings);
        return t;
    }

    public bool OnSteam => WorkshopId != null;
    public bool Ready => Problems.Count == 0;

    /// <summary>The short status shown in the list; the details are in <see cref="StatusText"/>.</summary>
    public string StatusLabel => Problems.Count > 0 ? "Fix" : OnSteam ? "Ready" : "Ready to create (private)";

    public string StatusText =>
        Problems.Count > 0 ? "Fix: " + string.Join("; ", Problems)
        : Warnings.Count > 0 ? "Ready (" + string.Join("; ", Warnings) + ")"
        : OnSteam ? "Ready" : "Ready to create (private)";
}

/// <summary>Reads each mod folder using the repo's <see cref="ProjectConfig"/> so problems show before uploading.</summary>
public static class ModScanner
{
    public static string? GitBranch(string repo)
    {
        var o = Git(repo, "rev-parse --abbrev-ref HEAD");
        return string.IsNullOrWhiteSpace(o) ? null : o.Trim();
    }

    public static bool IsGitRepo(string repo) => !string.IsNullOrWhiteSpace(Git(repo, "rev-parse --is-inside-work-tree"));

    /// <summary>
    /// Reads one value from a metadata file. .xml = XPath, .json = dotted path, anything else = a key name
    /// (matches name=Foo / name = "Foo" / name: Foo) or a regex whose first group is the value.
    /// </summary>
    public static string? ReadMeta(string file, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !File.Exists(file)) return null;
        string? v = null;
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext == ".xml")
        {
            var el = XDocument.Load(file).XPathSelectElement("/" + key.TrimStart('/'));
            v = el?.Value;
        }
        else if (ext == ".json")
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var cur = doc.RootElement;
            foreach (var part in key.Split('.'))
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(part, out cur)) return null;
            }
            v = cur.ValueKind == JsonValueKind.String ? cur.GetString() : cur.ToString();
        }
        else
        {
            var text = File.ReadAllText(file);
            var pattern = key.Contains('(') ? key : @"^\s*" + Regex.Escape(key) + "\\s*[=:]\\s*\"?(.*?)\"?\\s*,?\\s*$";
            var m = Regex.Match(text, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            v = m.Success && m.Groups.Count > 1 ? m.Groups[1].Value : null;
        }
        v = v?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>
    /// Sets <paramref name="key"/> to <paramref name="value"/> in a key=value text file such as a Paradox descriptor.mod,
    /// replacing the line that holds the key or adding one at the end. Returns the new text.
    /// </summary>
    public static string WriteKey(string text, string key, string value)
    {
        var line = $"{key}=\"{value}\"";
        var re = new Regex(@"^([ \t]*)" + Regex.Escape(key) + @"[ \t]*[=:][^\r\n]*", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (re.IsMatch(text)) return re.Replace(text, m => m.Groups[1].Value + line, 1);
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        return text.Length == 0 || text.EndsWith('\n') ? text + line + nl : text + nl + line + nl;
    }

    /// <summary>
    /// The changelog split into (version line or null, release notes). With <see cref="ProjectConfig.ChangelogByVersion"/>
    /// the notes are the entry for <paramref name="version"/> (see <see cref="ChangenoteFor"/>).
    /// </summary>
    public static (string? version, string notes) ReadChangelog(string file, ProjectConfig cfg, string? version = null)
    {
        var lines = File.ReadAllLines(file);
        if (cfg.ChangelogByVersion) return (null, ChangenoteFor(lines, version ?? ""));
        if (!cfg.ChangelogFirstLineIsVersion) return (null, string.Join("\n", lines).Trim());
        return (lines.FirstOrDefault()?.Trim() ?? "", string.Join("\n", lines.Skip(1)).Trim());
    }

    static readonly Regex VersionLine = new(@"^\d+(?:\.\d+){1,3}");

    /// <summary>
    /// The change note for <paramref name="version"/>, worked out exactly like the Add Changenote mod: lines starting with #
    /// are skipped, the entry starts at the first line that starts with the version and runs until the next line that starts
    /// with a version number. Empty when there is no entry.
    /// </summary>
    public static string ChangenoteFor(IEnumerable<string> lines, string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        var picked = new List<string>();
        var inEntry = false;
        foreach (var line in lines)
        {
            if (line.StartsWith('#')) continue;
            if (line.StartsWith(version)) { inEntry = true; picked.Add(line); continue; }
            if (!inEntry) continue;
            if (VersionLine.IsMatch(line)) break;
            picked.Add(line);
        }
        return string.Join("\n", picked).Trim();
    }

    /// <summary>True when the layout wants a changelog entry per version and <paramref name="m"/>'s version has none yet.</summary>
    public static bool NeedsChangelogEntry(ModInfo m, ProjectConfig cfg)
    {
        if (!cfg.ChangelogByVersion || m.Version.Length == 0) return false;
        var path = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.Changelog));
        try { return !File.Exists(path) || ReadChangelog(path, cfg, m.Version).notes.Length == 0; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// Adds an Add Changenote style entry for <paramref name="version"/> to the mod's changelog, creating the file if
    /// needed. Returns null on success, or why it cannot.
    /// </summary>
    public static string? AddChangelogEntry(ModInfo m, ProjectConfig cfg, string version, string notes)
    {
        version = version.Trim();
        if (!VersionLine.IsMatch(version)) return "Use a version number like 1.2.3.";
        if (notes.Trim().Length == 0) return "Write at least one line about what changed.";
        var path = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.Changelog));
        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        if (ChangenoteFor(existing.Replace("\r\n", "\n").Split('\n'), version).Length > 0)
            return $"{cfg.Changelog} already has an entry for {version}.";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, InsertChangelogEntry(existing, version, notes), new System.Text.UTF8Encoding(false));
        return null;
    }

    /// <summary>
    /// The changelog text with a new entry: the version on its own line, then the notes. It goes above the newest entry,
    /// or at the end when the file lists the oldest version first. Note lines that would end the entry early (a line
    /// starting with a version number) or be skipped (a line starting with #) get a "- " in front. Line endings are kept.
    /// </summary>
    public static string InsertChangelogEntry(string existing, string version, string notes)
    {
        var nl = existing.Contains("\r\n") ? "\r\n" : "\n";
        var noteLines = notes.Replace("\r\n", "\n").Trim('\n').Split('\n')
            .Select(l => l.TrimEnd())
            .Select(l => l.StartsWith('#') || VersionLine.IsMatch(l) ? "- " + l : l);
        var entry = new List<string> { version.Trim() };
        entry.AddRange(noteLines);

        var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
        if (existing.Length == 0) lines.Clear();
        var versionAt = lines.Select((l, i) => (l, i)).Where(x => !x.l.StartsWith('#') && VersionLine.IsMatch(x.l)).Select(x => x.i).ToList();

        var oldestFirst = versionAt.Count >= 2
            && ParseVersion(lines[versionAt[0]]) is { } first && ParseVersion(lines[versionAt[^1]]) is { } last && first < last;
        if (versionAt.Count == 0 || oldestFirst)
        {
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > 0) lines.Add("");
            lines.AddRange(entry);
            lines.Add("");
        }
        else
        {
            entry.Add("");
            lines.InsertRange(versionAt[0], entry);
        }
        return string.Join(nl, lines);
    }

    static Version? ParseVersion(string line)
    {
        var m = VersionLine.Match(line);
        return m.Success && Version.TryParse(m.Value, out var v) ? v : null;
    }

    /// <summary>The highest version an Add Changenote style changelog has an entry for (lines starting with # skipped), or null.</summary>
    public static Version? NewestChangelogVersion(IEnumerable<string> lines) =>
        lines.Where(l => !l.StartsWith('#')).Select(ParseVersion).Where(v => v != null).Max();

    /// <summary>
    /// A warning when the manifest and the marker file both give a version and they differ (the manifest's is the one
    /// uploaded, so the other is usually a forgotten edit); null when they agree or only one has a version.
    /// </summary>
    /// <summary>1.2 and 1.2.0 are the same version: missing parts count as 0.</summary>
    static Version Full(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    static string? VersionMismatch(string dir, string markerFile, ProjectConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.ManifestFile) || string.IsNullOrWhiteSpace(cfg.VersionKey)) return null;
        string? fromManifest, fromMarker;
        try
        {
            fromManifest = ReadMeta(Path.Combine(dir, ProjectConfig.Norm(cfg.ManifestFile)), "Manifest/version");
            fromMarker = ReadMeta(markerFile, cfg.VersionKey);
        }
        catch { return null; }
        if (fromManifest == null || fromMarker == null) return null;
        static string Plain(string v) => v.Trim().TrimStart('v', 'V');
        return Plain(fromManifest) == Plain(fromMarker) ? null
            : $"{cfg.ManifestFile} says {fromManifest} but {cfg.ModMarker} says {fromMarker}; {fromManifest} is uploaded (F2 sets both)";
    }

    /// <summary>The version of the mod in <paramref name="dir"/> (a mod folder or a staged package), or null when it has none.</summary>
    public static string? VersionIn(string dir, ProjectConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.ModMarker)) return null;
        try { return ReadVersion(dir, Path.Combine(dir, ProjectConfig.Norm(cfg.ModMarker)), cfg) is { Length: > 0 } v ? v : null; }
        catch { return null; }
    }

    /// <summary>The version: the manifest's &lt;version&gt; when the layout has one and it is set, else the marker's.</summary>
    static string ReadVersion(string dir, string markerFile, ProjectConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ManifestFile))
        {
            var manifest = Path.Combine(dir, ProjectConfig.Norm(cfg.ManifestFile));
            try { if (ReadMeta(manifest, "Manifest/version") is { } v) return v; } catch { }
        }
        return ReadMeta(markerFile, cfg.VersionKey) ?? "";
    }

    /// <summary>Which part of a version <see cref="NextVersion"/> raises.</summary>
    public enum Bump { Patch, Minor, Major }

    /// <summary>
    /// The version after <paramref name="version"/>: Major raises the first number, Minor the second and Patch the last of
    /// at least three, setting the ones after it to 0 (1.2.3 gives 1.2.4, 1.3.0 or 2.0.0; 1.2 gives 1.2.1). A leading "v"
    /// is dropped, as <see cref="SetVersion"/> writes plain numbers. Null when <paramref name="version"/> is not a version number.
    /// </summary>
    public static string? NextVersion(string version, Bump part)
    {
        var m = Regex.Match(version.Trim(), @"^[vV]?(\d+(?:\.\d+){0,3})$");
        if (!m.Success) return null;
        var n = m.Groups[1].Value.Split('.').Select(long.Parse).ToList();
        var at = part switch { Bump.Major => 0, Bump.Minor => 1, _ => Math.Max(2, n.Count - 1) };
        while (n.Count <= at) n.Add(0);
        n[at]++;
        while (n.Count < 2) n.Add(0); // SetVersion needs at least "1.0"
        for (var i = at + 1; i < n.Count; i++) n[i] = 0;
        return string.Join(".", n);
    }

    /// <summary>
    /// Writes a new version into the files it is read from: the manifest's &lt;version&gt; (when it has one) and the
    /// <see cref="ProjectConfig.VersionKey"/> of the marker file: an XML element, a JSON property (a dotted path), or a
    /// key=value line such as a Paradox descriptor.mod's <c>version="1.2.3"</c>. Returns null on success, or why it cannot.
    /// </summary>
    public static string? SetVersion(ModInfo m, ProjectConfig cfg, string version)
    {
        version = version.Trim();
        if (!Regex.IsMatch(version, @"^\d+(\.\d+){1,3}$")) return "Use a version number like 1.2.3.";
        var written = 0;
        if (!string.IsNullOrWhiteSpace(cfg.ManifestFile)
            && TrySetXml(Path.Combine(m.Dir, ProjectConfig.Norm(cfg.ManifestFile)), "Manifest/version", version, create: false))
            written++;
        if (!string.IsNullOrWhiteSpace(cfg.ModMarker) && !string.IsNullOrWhiteSpace(cfg.VersionKey))
        {
            var marker = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.ModMarker));
            var ok = Path.GetExtension(marker).ToLowerInvariant() switch
            {
                ".xml" => TrySetXml(marker, cfg.VersionKey, version, create: true),
                ".json" => TrySetJson(marker, cfg.VersionKey, version),
                _ => TrySetKey(marker, cfg.VersionKey, version),
            };
            if (ok) written++;
        }
        return written > 0 ? null : "This layout does not read the version from a file the app can edit. Change it in the mod's own files.";
    }

    /// <summary>
    /// Replaces the value of an existing <paramref name="key"/> line, keeping its spacing, separator and quotes
    /// (<c>version = "1.2"</c> stays in that style). Null when no line holds the key.
    /// </summary>
    public static string? ReplaceKeyValue(string text, string key, string value)
    {
        var re = new Regex(@"^([ \t]*" + Regex.Escape(key) + @"[ \t]*[=:][ \t]*)(""?)[^""\r\n]*?(""?)([ \t]*,?[ \t]*\r?)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!re.IsMatch(text)) return null;
        return re.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + value + m.Groups[3].Value + m.Groups[4].Value, 1);
    }

    // A key given as a regex (it has a group) cannot be written back, and a missing line is not added: a key=value file
    // of unknown format is safer left to its author.
    static bool TrySetKey(string file, string key, string value)
    {
        if (!File.Exists(file) || key.Contains('(')) return false;
        var text = File.ReadAllText(file);
        var updated = ReplaceKeyValue(text, key, value);
        if (updated == null) return false;
        File.WriteAllText(file, updated, new System.Text.UTF8Encoding(false));
        return true;
    }

    // Only an existing string property is changed. A file with comments is left alone, since writing it back would
    // drop them.
    static bool TrySetJson(string file, string key, string value)
    {
        if (!File.Exists(file)) return false;
        System.Text.Json.Nodes.JsonNode? root;
        try { root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (JsonException) { return false; }
        var parts = key.Split('.');
        var parent = root;
        foreach (var part in parts[..^1]) parent = parent is System.Text.Json.Nodes.JsonObject o && o.TryGetPropertyValue(part, out var next) ? next : null;
        if (parent is not System.Text.Json.Nodes.JsonObject obj || !obj.TryGetPropertyValue(parts[^1], out var cur)
            || cur is not System.Text.Json.Nodes.JsonValue jv || !jv.TryGetValue<string>(out _)) return false;
        obj[parts[^1]] = value;
        File.WriteAllText(file, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", new System.Text.UTF8Encoding(false));
        return true;
    }

    static bool TrySetXml(string file, string path, string value, bool create)
    {
        if (!File.Exists(file)) return false;
        path = path.TrimStart('/');
        var doc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
        var el = doc.XPathSelectElement("/" + path);
        if (el == null)
        {
            var cut = path.LastIndexOf('/');
            if (!create || cut < 0) return false;
            var parent = doc.XPathSelectElement("/" + path.Substring(0, cut));
            if (parent == null) return false;
            el = new XElement(path.Substring(cut + 1));
            parent.Add(el);
        }
        el.Value = value;
        var settings = new System.Xml.XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), OmitXmlDeclaration = doc.Declaration == null };
        using (var w = System.Xml.XmlWriter.Create(file, settings)) doc.Save(w);
        return true;
    }

    /// <summary>Steam refuses preview images larger than this.</summary>
    public const long MaxPreviewBytes = 1024 * 1024;

    public static string ModsRoot(string repo, ProjectConfig cfg) =>
        string.IsNullOrWhiteSpace(cfg.ModsFolder) ? repo : Path.Combine(repo, ProjectConfig.Norm(cfg.ModsFolder));

    public static List<ModInfo> Scan(string repo, ProjectConfig cfg)
    {
        var list = new List<ModInfo>();
        var root = ModsRoot(repo, cfg);
        if (!Directory.Exists(root)) return list;

        // Untracked files are not counted, so git is told not to look for them: in a folder full of build output that
        // search is most of the time a refresh takes.
        var dirtyLines = Git(repo, "status --porcelain --untracked-files=no")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length > 3 && !l.StartsWith("??"))
            .Select(l => l.Substring(3).Trim().Trim('"').Replace('\\', '/'))
            .ToList();

        var hasMarker = !string.IsNullOrWhiteSpace(cfg.ModMarker);
        var marker = hasMarker ? ProjectConfig.Norm(cfg.ModMarker) : "";
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(dir);
            if (folder.StartsWith('.')) continue;
            var markerFile = hasMarker ? Path.Combine(dir, marker) : "";
            if (hasMarker && !File.Exists(markerFile)) continue;
            if (!hasMarker && IgnoredFolder(folder)) continue;

            var rel = Path.GetRelativePath(repo, dir).Replace('\\', '/');
            var m = new ModInfo { Dir = dir, Folder = folder, RelPath = rel, Name = folder };
            if (hasMarker)
            {
                try
                {
                    m.Name = ReadMeta(markerFile, cfg.NameKey) ?? folder;
                    m.Version = ReadVersion(dir, markerFile, cfg);
                    if (VersionMismatch(dir, markerFile, cfg) is { } mismatch) m.Warnings.Add(mismatch);
                }
                catch (Exception e) { m.Problems.Add(cfg.ModMarker + " unreadable (" + e.Message + ")"); }
            }

            var idFile = Path.Combine(dir, ProjectConfig.Norm(cfg.WorkshopIdFile));
            if (File.Exists(idFile))
            {
                try
                {
                    var keyed = !string.IsNullOrWhiteSpace(cfg.WorkshopIdKey);
                    var id = keyed ? ReadMeta(idFile, cfg.WorkshopIdKey) : File.ReadAllText(idFile).Trim();
                    if (keyed && (id == null || Regex.IsMatch(id, @"^0+$"))) { } // not published yet
                    else if (id != null && Regex.IsMatch(id, @"^\d+$")) m.WorkshopId = id;
                    else m.Problems.Add((keyed ? cfg.WorkshopIdKey + " in " : "") + cfg.WorkshopIdFile + " is not a number");
                }
                catch (Exception e) { m.Problems.Add(cfg.WorkshopIdFile + " unreadable (" + e.Message + ")"); }
            }

            var testFile = Path.Combine(dir, ModInfo.TestIdFile);
            try { if (File.Exists(testFile) && File.ReadAllText(testFile).Trim() is var t && Regex.IsMatch(t, @"^\d+$")) m.TestWorkshopId = t; } catch { }

            // Only what would make the upload fail or go wrong blocks it (Problems). Everything else is a warning.
            var clPath = Path.Combine(dir, ProjectConfig.Norm(cfg.Changelog));
            if (cfg.ChangelogByVersion)
            {
                // Add Changenote's rules: the current version must be known and have its own changelog entry.
                if (m.Version.Length == 0)
                    m.Problems.Add("no version (" + (string.IsNullOrWhiteSpace(cfg.ManifestFile) ? "" : "<version> in " + cfg.ManifestFile + " or ") + "version in " + cfg.ModMarker + ")");
                else if (!File.Exists(clPath))
                    m.Problems.Add("no " + cfg.Changelog);
                else if (ReadChangelog(clPath, cfg, m.Version).notes.Length == 0)
                    m.Problems.Add($"{cfg.Changelog} has no entry for {m.Version}");
                if (m.Version.Length > 0 && File.Exists(clPath) && NewestChangelogVersion(File.ReadAllLines(clPath)) is { } newest
                    && ParseVersion(m.Version) is { } current && Full(newest) > Full(current))
                    m.Warnings.Add($"{cfg.Changelog} has an entry for {newest}, newer than the version to upload ({m.Version}): raise the version (F2)?");
            }
            else if (!File.Exists(clPath))
            {
                if (cfg.ChangelogRequired) m.Warnings.Add("no " + cfg.Changelog);
            }
            else
            {
                var (clVersion, notes) = ReadChangelog(clPath, cfg);
                if (clVersion != null)
                {
                    if (!Regex.IsMatch(clVersion, @"^\d+(\.\d+)*")) m.Warnings.Add("changelog line 1 is not a version");
                    else if (m.Version.Length == 0) m.Version = clVersion;
                    else if (clVersion != m.Version) m.Warnings.Add($"changelog {clVersion} vs {m.Version} in {cfg.ModMarker}");
                }
                if (cfg.ChangelogRequired && notes.Length == 0) m.Warnings.Add("changelog has no notes");
            }

            // Steam refuses a new item without a preview image, and any preview over 1 MB. Existing items keep the
            // preview they have on Steam (it is only sent on create), so for them this is shown but does not block.
            if (!string.IsNullOrWhiteSpace(cfg.Preview))
            {
                var preview = new FileInfo(Path.Combine(dir, ProjectConfig.Norm(cfg.Preview)));
                m.PreviewOk = preview.Exists && preview.Length <= MaxPreviewBytes;
                if (preview.Exists) m.PreviewPath = preview.FullName;
                m.PreviewNote = !preview.Exists ? "no " + cfg.Preview
                    : $"{cfg.Preview}: {preview.Length / 1024.0:0} KB of {MaxPreviewBytes / 1024} KB" + (m.PreviewOk == true ? "" : " (over Steam's limit)");
                if (!m.OnSteam && !preview.Exists) m.Problems.Add("needs " + cfg.Preview + " to create on Steam");
                else if (!m.OnSteam && preview.Length > MaxPreviewBytes) m.Problems.Add($"{cfg.Preview} is over 1 MB (Steam's limit)");
            }

            foreach (var glob in cfg.BuiltFiles ?? Array.Empty<string>())
                if (MatchGlob(dir, glob).Count == 0) m.Warnings.Add($"not built (nothing matches {glob})");

            if (dirtyLines.Any(p => p.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase)))
            {
                m.Dirty = true;
                m.Warnings.Add("uncommitted changes will NOT be uploaded");
            }

            list.Add(m);
        }
        return list;
    }

    static bool IgnoredFolder(string name) =>
        new[] { "bin", "obj", "node_modules", "_tools", "scripts", "docs", "site", "tools", "tests", "test" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Files inside <paramref name="dir"/> matching a simple glob such as "Assemblies/*.dll" (wildcards in the last segment only).</summary>
    public static List<string> MatchGlob(string dir, string glob)
    {
        var g = ProjectConfig.Norm(glob);
        var sub = Path.GetDirectoryName(g) ?? "";
        var pattern = Path.GetFileName(g);
        var folder = Path.Combine(dir, sub);
        return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).ToList() : new List<string>();
    }

    public static string Git(string repo, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return o;
        }
        catch { return ""; }
    }
}
