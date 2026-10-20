using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Adds a new RimWorld version to a mod when the game updates: the version goes into About.xml's supportedVersions,
/// and for a mod with version folders the newest folder is copied to the new version and loadFolders.xml gets a
/// matching entry. The files are edited as text, so their layout and comments stay as they were.
/// </summary>
public static class GameVersions
{
    static readonly Regex Short = new(@"^\d+\.\d+$");
    static readonly Regex Supported = new(@"<supportedVersions>(?<body>.*?)</supportedVersions>", RegexOptions.Singleline);
    static readonly Regex Li = new(@"(?<indent>[ \t]*)<li>\s*(?<v>[^<]*?)\s*</li>");

    /// <summary>What adding a version did to one mod, for the log. Empty <see cref="Done"/> and no error: it already had it.</summary>
    public sealed record Result(List<string> Done, string? Error);

    /// <summary>"1.6" + 0.1 = "1.7": the version after <paramref name="newest"/>, or "" when it is not a version.</summary>
    public static string Next(string newest) =>
        Regex.Match(newest.Trim(), @"^(\d+)\.(\d+)") is { Success: true } m ? $"{m.Groups[1].Value}.{int.Parse(m.Groups[2].Value) + 1}" : "";

    static Version V(string s) => Version.TryParse(s, out var v) ? v : new Version(0, 0);

    /// <summary>
    /// Adds <paramref name="version"/> (such as "1.7") to the mod in <paramref name="modDir"/>. With
    /// <paramref name="copyFolder"/>, a mod that has version folders gets the newest older one copied to the new
    /// version, and loadFolders.xml (when there is one) an entry for it made from the newest older entry.
    /// </summary>
    public static Result Add(string modDir, string version, bool copyFolder)
    {
        version = version.Trim();
        if (!Short.IsMatch(version)) return new Result(new(), $"\"{version}\" is not a RimWorld version such as 1.6.");
        var done = new List<string>();
        var aboutPath = Path.Combine(modDir, "About", "About.xml");
        if (!File.Exists(aboutPath)) return new Result(done, "it has no About/About.xml.");
        var about = File.ReadAllText(aboutPath);
        var sup = Supported.Match(about);
        if (!sup.Success) return new Result(done, "About.xml has no <supportedVersions> list.");
        var items = Li.Matches(sup.Groups["body"].Value).Cast<Match>().ToList();
        if (!items.Any(i => i.Groups["v"].Value.StartsWith(version) && (i.Groups["v"].Value.Length == version.Length || i.Groups["v"].Value[version.Length] == '.')))
        {
            var nl = about.Contains("\r\n") ? "\r\n" : "\n";
            string updated;
            if (items.Count > 0)
            {
                var last = items[^1];
                var at = sup.Groups["body"].Index + last.Index + last.Length;
                updated = about[..at] + nl + last.Groups["indent"].Value + $"<li>{version}</li>" + about[at..];
            }
            else
            {
                var at = sup.Groups["body"].Index;
                updated = about[..at] + $"<li>{version}</li>" + about[at..];
            }
            File.WriteAllText(aboutPath, updated, new UTF8Encoding(about.Length > 0 && File.ReadAllBytes(aboutPath).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })));
            done.Add($"added {version} to supportedVersions");
        }
        if (!copyFolder) return new Result(done, null);

        var folders = Directory.GetDirectories(modDir).Select(Path.GetFileName).Where(n => Short.IsMatch(n!)).Select(n => n!).ToList();
        var older = folders.Where(f => V(f) < V(version)).OrderBy(V).LastOrDefault();
        if (older != null && !folders.Contains(version))
        {
            CopyDir(Path.Combine(modDir, older), Path.Combine(modDir, version));
            done.Add($"copied the {older} folder to {version} (rebuild its Assemblies for {version} when the game's assemblies changed)");
        }

        var loadFolders = Path.Combine(modDir, "loadFolders.xml");
        if (File.Exists(loadFolders))
        {
            var text = File.ReadAllText(loadFolders);
            if (!Regex.IsMatch(text, $@"<v{Regex.Escape(version)}\s*/?>"))
            {
                var entries = Regex.Matches(text, @"(?<indent>[ \t]*)<v(?<v>\d+\.\d+)>(?<body>.*?)</v\k<v>>", RegexOptions.Singleline).Cast<Match>()
                    .Where(m => V(m.Groups["v"].Value) < V(version)).OrderBy(m => V(m.Groups["v"].Value)).ToList();
                if (entries.Count > 0)
                {
                    var src = entries[^1];
                    var from = src.Groups["v"].Value;
                    // Folder entries for the old version ("1.6", "1.6/Patches") point at the new one; shared ones ("/", "Common") stay.
                    var body = Regex.Replace(src.Groups["body"].Value, @"<li>\s*" + Regex.Escape(from) + @"(?<rest>(/[^<]*)?)\s*</li>", m => $"<li>{version}{m.Groups["rest"].Value}</li>");
                    var nl = text.Contains("\r\n") ? "\r\n" : "\n";
                    var at = src.Index + src.Length;
                    text = text[..at] + nl + src.Groups["indent"].Value + $"<v{version}>{body}</v{version}>" + text[at..];
                    File.WriteAllText(loadFolders, text, new UTF8Encoding(false));
                    done.Add($"added a v{version} entry to loadFolders.xml (from v{from})");
                }
            }
        }
        return new Result(done, null);
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
