using System.Diagnostics;
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
    /// <summary>Preview image: true = present and within Steam's 1 MB limit, false = missing or too big.</summary>
    public bool PreviewOk;
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

    /// <summary>Reads one value from an XML file by XPath (such as "ModMetaData/name"), or null when it is missing or empty.</summary>
    public static string? ReadMeta(string file, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !File.Exists(file)) return null;
        var v = XDocument.Load(file).XPathSelectElement("/" + key.TrimStart('/'))?.Value.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>The changelog's entry for <paramref name="version"/> (see <see cref="ChangenoteFor"/>).</summary>
    public static string ReadChangelog(string file, string version) => ChangenoteFor(File.ReadAllLines(file), version);

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

    /// <summary>True when <paramref name="m"/>'s version has no changelog entry yet.</summary>
    public static bool NeedsChangelogEntry(ModInfo m, ProjectConfig cfg)
    {
        if (m.Version.Length == 0) return false;
        var path = Path.Combine(m.Dir, ProjectConfig.Norm(cfg.Changelog));
        try { return !File.Exists(path) || ReadChangelog(path, m.Version).Length == 0; }
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

    /// <summary>1.2 and 1.2.0 are the same version: missing parts count as 0.</summary>
    static Version Full(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>
    /// A warning when the manifest and the marker file both give a version and they differ (the manifest's is the one
    /// uploaded, so the other is usually a forgotten edit); null when they agree or only one has a version.
    /// </summary>
    static string? VersionMismatch(string dir, string markerFile, ProjectConfig cfg)
    {
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
        try { return ReadVersion(dir, Path.Combine(dir, ProjectConfig.Norm(cfg.ModMarker)), cfg) is { Length: > 0 } v ? v : null; }
        catch { return null; }
    }

    /// <summary>The version: the manifest's &lt;version&gt; when it has one, else the marker's.</summary>
    static string ReadVersion(string dir, string markerFile, ProjectConfig cfg)
    {
        var manifest = Path.Combine(dir, ProjectConfig.Norm(cfg.ManifestFile));
        try { if (ReadMeta(manifest, "Manifest/version") is { } v) return v; } catch { }
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
    /// <see cref="ProjectConfig.VersionKey"/> element of the marker file. Returns null on success, or why it cannot.
    /// </summary>
    public static string? SetVersion(ModInfo m, ProjectConfig cfg, string version)
    {
        version = version.Trim();
        if (!Regex.IsMatch(version, @"^\d+(\.\d+){1,3}$")) return "Use a version number like 1.2.3.";
        var manifest = TrySetXml(Path.Combine(m.Dir, ProjectConfig.Norm(cfg.ManifestFile)), "Manifest/version", version, create: false);
        var marker = TrySetXml(Path.Combine(m.Dir, ProjectConfig.Norm(cfg.ModMarker)), cfg.VersionKey, version, create: true);
        return manifest || marker ? null : $"{cfg.ModMarker} has no <ModMetaData> to put the version in. Change it in the mod's own files.";
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

    /// <param name="strictAbout">RimWorld: About.xml warnings block the upload (Options > Uploading).</param>
    public static List<ModInfo> Scan(string repo, ProjectConfig cfg, bool strictAbout = false)
    {
        var list = new List<ModInfo>();
        if (!Directory.Exists(repo)) return list;

        // Untracked files are not counted, so git is told not to look for them: in a folder full of build output that
        // search is most of the time a refresh takes.
        var dirtyLines = Git(repo, "status --porcelain --untracked-files=no")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length > 3 && !l.StartsWith("??"))
            .Select(l => l.Substring(3).Trim().Trim('"').Replace('\\', '/'))
            .ToList();

        var marker = ProjectConfig.Norm(cfg.ModMarker);
        foreach (var dir in Directory.GetDirectories(repo).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(dir);
            if (folder.StartsWith('.')) continue;
            var markerFile = Path.Combine(dir, marker);
            if (!File.Exists(markerFile)) continue;

            var rel = Path.GetRelativePath(repo, dir).Replace('\\', '/');
            var m = new ModInfo { Dir = dir, Folder = folder, RelPath = rel, Name = folder };
            try
            {
                m.Name = ReadMeta(markerFile, cfg.NameKey) ?? folder;
                m.Version = ReadVersion(dir, markerFile, cfg);
                if (VersionMismatch(dir, markerFile, cfg) is { } mismatch) m.Warnings.Add(mismatch);
                if (WorkshopTags.SupportedVersions(markerFile, cfg.VersionTagsKey).Count == 0)
                    m.Warnings.Add($"{cfg.ModMarker} lists no supported game versions, so the Workshop gets no version tags");
            }
            catch (Exception e) { m.Problems.Add(cfg.ModMarker + " unreadable (" + e.Message + ")"); }

            var idFile = Path.Combine(dir, ProjectConfig.Norm(cfg.WorkshopIdFile));
            if (File.Exists(idFile))
            {
                try
                {
                    var id = File.ReadAllText(idFile).Trim();
                    if (Regex.IsMatch(id, @"^\d+$")) m.WorkshopId = id;
                    else m.Problems.Add(cfg.WorkshopIdFile + " is not a number");
                }
                catch (Exception e) { m.Problems.Add(cfg.WorkshopIdFile + " unreadable (" + e.Message + ")"); }
            }

            var testFile = Path.Combine(dir, ModInfo.TestIdFile);
            try { if (File.Exists(testFile) && File.ReadAllText(testFile).Trim() is var t && Regex.IsMatch(t, @"^\d+$")) m.TestWorkshopId = t; } catch { }

            // Only what would make the upload fail or go wrong blocks it (Problems). Everything else is a warning.
            // Add Changenote's rules: the current version must be known and have its own changelog entry.
            var clPath = Path.Combine(dir, ProjectConfig.Norm(cfg.Changelog));
            if (m.Version.Length == 0)
                m.Problems.Add("no version (<version> in " + cfg.ManifestFile + " or version in " + cfg.ModMarker + ")");
            else if (!File.Exists(clPath))
                m.Problems.Add("no " + cfg.Changelog);
            else if (ReadChangelog(clPath, m.Version).Length == 0)
                m.Problems.Add($"{cfg.Changelog} has no entry for {m.Version}");
            if (m.Version.Length > 0 && File.Exists(clPath) && NewestChangelogVersion(File.ReadAllLines(clPath)) is { } newest
                && ParseVersion(m.Version) is { } current && Full(newest) > Full(current))
                m.Warnings.Add($"{cfg.Changelog} has an entry for {newest}, newer than the version to upload ({m.Version}): raise the version (F2)?");

            // Steam refuses a new item without a preview image, and any preview over 1 MB. Existing items keep the
            // preview they have on Steam (it is only sent on create), so for them this is shown but does not block.
            var preview = new FileInfo(Path.Combine(dir, ProjectConfig.Norm(cfg.Preview)));
            m.PreviewOk = preview.Exists && preview.Length <= MaxPreviewBytes;
            if (preview.Exists) m.PreviewPath = preview.FullName;
            m.PreviewNote = !preview.Exists ? "no " + cfg.Preview
                : $"{cfg.Preview}: {preview.Length / 1024.0:0} KB of {MaxPreviewBytes / 1024} KB" + (m.PreviewOk ? "" : " (over Steam's limit)");
            if (!m.OnSteam && !preview.Exists) m.Problems.Add("needs " + cfg.Preview + " to create on Steam");
            else if (!m.OnSteam && preview.Length > MaxPreviewBytes) m.Problems.Add($"{cfg.Preview} is over 1 MB (Steam's limit)");
            // The preview is only sent when the item is created, so an existing item's file is not worth a warning.
            if (preview.Exists && !m.OnSteam) m.Warnings.AddRange(ImageHeader.PreviewWarnings(preview.FullName));

            // A build can land in several places: Assemblies or a version folder's Assemblies.
            var built = cfg.BuiltFiles;
            if (built.Length > 0 && built.All(glob => MatchGlob(dir, glob).Count == 0))
                m.Warnings.Add($"not built (nothing matches {string.Join(" or ", built)})");

            var (problems, warnings) = RimWorldLint.Check(dir, strictAbout);
            m.Problems.AddRange(problems);
            m.Warnings.AddRange(warnings);

            if (dirtyLines.Any(p => p.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase)))
            {
                m.Dirty = true;
                m.Warnings.Add("uncommitted changes will NOT be uploaded");
            }

            list.Add(m);
        }
        foreach (var (m, w) in RimWorldLint.DuplicatePackageIds(list.Select(m => (m, RimWorldLint.PackageIdOf(m.Dir)))))
            m.Warnings.Add(w);
        return list;
    }

    /// <summary>
    /// A cheap fingerprint of what <see cref="Scan"/> reads: the mod folders, the times of the files in each mod folder
    /// and one level down (About/About.xml, the changelog, the id file, the preview), the repo's top-level files (the
    /// layout) and git's HEAD and index. It changes when a mod is edited, added or removed, or a commit is made, so the
    /// window can refresh by itself only when there is something new. Empty when the folder can't be read.
    /// </summary>
    public static string FolderStamp(string repo)
    {
        try
        {
            if (!Directory.Exists(repo)) return "";
            var hash = new HashCode();
            void Files(string dir)
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles())
                {
                    hash.Add(f.Name, StringComparer.OrdinalIgnoreCase);
                    hash.Add(f.LastWriteTimeUtc.Ticks);
                    hash.Add(f.Length);
                }
            }
            Files(repo);
            foreach (var git in new[] { ".git/HEAD", ".git/index", ".git/logs/HEAD" })
            {
                var f = new FileInfo(Path.Combine(repo, git));
                if (f.Exists) hash.Add(f.LastWriteTimeUtc.Ticks);
            }
            foreach (var dir in Directory.GetDirectories(repo).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.')) continue;
                hash.Add(name, StringComparer.OrdinalIgnoreCase);
                Files(dir);
                foreach (var sub in Directory.GetDirectories(dir))
                    if (!Path.GetFileName(sub).StartsWith('.')) Files(sub);
            }
            return hash.ToHashCode().ToString("x8");
        }
        catch { return ""; }
    }

    /// <summary>
    /// Files inside <paramref name="dir"/> matching a simple glob such as "Assemblies/*.dll". Any segment may hold * or ?,
    /// so "*/Assemblies/*.dll" finds RimWorld's per-version folders (1.5/Assemblies, 1.6/Assemblies, ...).
    /// </summary>
    public static List<string> MatchGlob(string dir, string glob)
    {
        var parts = glob.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return new List<string>();
        IEnumerable<string> folders = Directory.Exists(dir) ? new[] { dir } : Array.Empty<string>();
        foreach (var part in parts.SkipLast(1))
            folders = folders.SelectMany(f => part.IndexOfAny(new[] { '*', '?' }) >= 0
                ? Directory.GetDirectories(f, part)
                : Directory.Exists(Path.Combine(f, part)) ? new[] { Path.Combine(f, part) } : Array.Empty<string>()).ToList();
        return folders.SelectMany(f => Directory.GetFiles(f, parts[^1])).ToList();
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
