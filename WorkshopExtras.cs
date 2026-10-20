using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// What to change on an item's Workshop page through the Steam client, beyond what SteamCMD's build file can carry.
/// A null list leaves that part of the page alone.
/// </summary>
public sealed class ExtrasRequest
{
    public string AppId { get; set; } = "";
    public string ItemId { get; set; } = "";
    /// <summary>The Workshop items players should be told to subscribe to as well ("Required items" on the page).</summary>
    public string[]? Required { get; set; }
    /// <summary>Required items this app set before, so ones the mod no longer lists can be taken off again.</summary>
    public string[] Managed { get; set; } = Array.Empty<string>();
    /// <summary>The gallery to show on the page: images from the mod's gallery folder and YouTube videos; null leaves it alone.</summary>
    public GalleryRequest? Gallery { get; set; }
    /// <summary>Titles and descriptions in other languages; null leaves them alone.</summary>
    public List<LanguagePage>? Languages { get; set; }
    /// <summary>Items to count comments on (the Steam client's statistics); null counts nothing. Needs no ownership.</summary>
    public string[]? CountComments { get; set; }
}

/// <summary>The gallery images (full paths, with a hash of each) and YouTube video ids an item should show.</summary>
public sealed class GalleryRequest
{
    public List<GalleryImage> Images { get; set; } = new();
    public string[] Videos { get; set; } = Array.Empty<string>();
}

public sealed class GalleryImage
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    /// <summary>True when this exact file was sent to the item before, so an image of that name on Steam is up to date.</summary>
    public bool Sent { get; set; }
}

/// <summary>An item's title (optional) and description in one of Steam's languages ("german", "schinese"...).</summary>
public sealed class LanguagePage
{
    public string Language { get; set; } = "";
    public string? Title { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>One extra preview on an item's page as Steam lists it: an image (by its file name) or a YouTube video (by id).</summary>
public sealed record PreviewOnSteam(uint Index, bool Video, string Key);

/// <summary>What the Steam client did with an <see cref="ExtrasRequest"/>.</summary>
public sealed class ExtrasResult
{
    public bool Ok { get; set; }
    /// <summary>Why it failed, when it did.</summary>
    public string Error { get; set; } = "";
    /// <summary>One line per change made, for the log.</summary>
    public List<string> Done { get; set; } = new();
    /// <summary>The required items this app now looks after (the wanted ones that are on the page).</summary>
    public string[]? RequiredNow { get; set; }
    /// <summary>Comment counts by item id, for <see cref="ExtrasRequest.CountComments"/>.</summary>
    public Dictionary<string, long>? Comments { get; set; }
    /// <summary>True when the gallery and the languages were sent, so their hashes can be remembered.</summary>
    public bool PageSent { get; set; }

    public static ExtrasResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Page changes SteamCMD cannot make (required items) and counts only the Steam client gives (comments), sent through the Steam client in a child copy of this
/// program, like Workshop tags (<see cref="WorkshopTags"/>). The planning is here so it can be tested; the Steam
/// calls are in SteamworksExtras.cs.
/// </summary>
public static class WorkshopExtras
{
    /// <summary>The argument that makes the program apply one request file and exit (run in a child process).</summary>
    public const string ChildArg = "--steam-page-extras";

    const string ResultPrefix = "RESULT ";

    public static string DefaultRecord => Path.Combine(AppSettings.Folder, "required-items.json");

    static readonly object Gate = new();

    /// <summary>
    /// Which required items to add and which to take off: every wanted item that is missing is added; an item on the
    /// page is only taken off when this app put it there (it is in <paramref name="managed"/>) and the mod no longer
    /// lists it, so items added by hand on the website stay.
    /// </summary>
    public static (List<string> Add, List<string> Remove) RequiredPlan(IEnumerable<string> current, IEnumerable<string> wanted, IEnumerable<string> managed)
    {
        var have = current.ToHashSet();
        var want = wanted.Distinct().ToList();
        var add = want.Where(id => !have.Contains(id)).ToList();
        var remove = managed.Distinct().Where(id => have.Contains(id) && !want.Contains(id)).ToList();
        return (add, remove);
    }

    /// <summary>The image types Steam shows in a Workshop gallery.</summary>
    public static readonly string[] GalleryTypes = { ".png", ".jpg", ".jpeg", ".gif" };

    /// <summary>
    /// How to make the page's gallery match the folder: images on Steam whose name the folder no longer has, and videos no
    /// longer listed, are removed (highest index first, so the others keep theirs); images whose file changed since it
    /// was sent are replaced; new images and videos are added.
    /// </summary>
    public static (List<uint> Remove, List<(uint Index, string Path)> Replace, List<string> AddImages, List<string> AddVideos) GalleryPlan(
        IReadOnlyList<PreviewOnSteam> current, GalleryRequest wanted)
    {
        var byName = wanted.Images.ToDictionary(i => Path.GetFileName(i.Path), StringComparer.OrdinalIgnoreCase);
        var remove = new List<uint>();
        var replace = new List<(uint, string)>();
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenVideos = new HashSet<string>();
        foreach (var p in current)
        {
            if (p.Video)
            {
                if (wanted.Videos.Contains(p.Key) && seenVideos.Add(p.Key)) continue;
                remove.Add(p.Index);
            }
            else if (byName.TryGetValue(p.Key, out var img) && seenImages.Add(p.Key))
            {
                if (!img.Sent) replace.Add((p.Index, img.Path));
            }
            else remove.Add(p.Index);
        }
        var addImages = wanted.Images.Where(i => !seenImages.Contains(Path.GetFileName(i.Path))).Select(i => i.Path).ToList();
        var addVideos = wanted.Videos.Where(v => !seenVideos.Contains(v)).Distinct().ToList();
        return (remove.OrderByDescending(i => i).ToList(), replace, addImages, addVideos);
    }

    static readonly Regex YouTube = new(@"(?:youtu\.be/|youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/))([A-Za-z0-9_-]{11})|^([A-Za-z0-9_-]{11})$");

    /// <summary>The YouTube video ids in a videos.txt: full links or bare ids, one per line; # starts a comment.</summary>
    public static string[] VideoIds(string text) => text.Replace("\r", "").Split('\n')
        .Select(l => l.Split('#')[0].Trim())
        .Select(l => YouTube.Match(l))
        .Where(m => m.Success)
        .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
        .Distinct().ToArray();

    /// <summary>The gallery for a mod (null when the layout has no gallery folder or the mod has none).</summary>
    public static GalleryRequest? GalleryFor(string modDir, ProjectConfig cfg, IReadOnlyDictionary<string, string> sentHashes)
    {
        if (string.IsNullOrWhiteSpace(cfg.GalleryFolder)) return null;
        var dir = Path.Combine(modDir, cfg.GalleryFolder.Trim());
        if (!Directory.Exists(dir)) return null;
        var g = new GalleryRequest();
        foreach (var f in Directory.GetFiles(dir).Where(f => GalleryTypes.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var hash = FileHash(f);
            g.Images.Add(new GalleryImage { Path = f, Hash = hash, Sent = sentHashes.GetValueOrDefault("img:" + Path.GetFileName(f).ToLowerInvariant()) == hash });
        }
        var videos = Path.Combine(dir, "videos.txt");
        if (File.Exists(videos)) g.Videos = VideoIds(File.ReadAllText(videos));
        return g;
    }

    /// <summary>Steam's names for its languages, and the short codes people often use for them.</summary>
    public static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["german"] = "german", ["de"] = "german", ["french"] = "french", ["fr"] = "french", ["spanish"] = "spanish", ["es"] = "spanish",
        ["latam"] = "latam", ["es-419"] = "latam", ["italian"] = "italian", ["it"] = "italian", ["portuguese"] = "portuguese", ["pt"] = "portuguese",
        ["brazilian"] = "brazilian", ["pt-br"] = "brazilian", ["russian"] = "russian", ["ru"] = "russian", ["ukrainian"] = "ukrainian", ["uk"] = "ukrainian",
        ["polish"] = "polish", ["pl"] = "polish", ["czech"] = "czech", ["cs"] = "czech", ["hungarian"] = "hungarian", ["hu"] = "hungarian",
        ["romanian"] = "romanian", ["ro"] = "romanian", ["bulgarian"] = "bulgarian", ["bg"] = "bulgarian", ["greek"] = "greek", ["el"] = "greek",
        ["turkish"] = "turkish", ["tr"] = "turkish", ["dutch"] = "dutch", ["nl"] = "dutch", ["danish"] = "danish", ["da"] = "danish",
        ["finnish"] = "finnish", ["fi"] = "finnish", ["norwegian"] = "norwegian", ["no"] = "norwegian", ["swedish"] = "swedish", ["sv"] = "swedish",
        ["schinese"] = "schinese", ["zh"] = "schinese", ["zh-cn"] = "schinese", ["tchinese"] = "tchinese", ["zh-tw"] = "tchinese",
        ["japanese"] = "japanese", ["ja"] = "japanese", ["koreana"] = "koreana", ["korean"] = "koreana", ["ko"] = "koreana",
        ["thai"] = "thai", ["th"] = "thai", ["vietnamese"] = "vietnamese", ["vi"] = "vietnamese", ["indonesian"] = "indonesian", ["id"] = "indonesian",
        ["arabic"] = "arabic", ["ar"] = "arabic",
    };

    /// <summary>
    /// The translated pages next to the layout's description file: "steam_description.german.txt" (or ".de.txt") is the
    /// German description; a first line "Title: ..." sets the German title too. Only files that changed since they were
    /// last sent to the item are returned; null when there are none at all.
    /// </summary>
    public static List<LanguagePage>? LanguagesFor(string modDir, ProjectConfig cfg, IReadOnlyDictionary<string, string> sentHashes, out List<LanguagePage> all)
    {
        all = new List<LanguagePage>();
        if (string.IsNullOrWhiteSpace(cfg.SteamDescriptionFile)) return null;
        var main = Path.Combine(modDir, ProjectConfig.Norm(cfg.SteamDescriptionFile));
        var dir = Path.GetDirectoryName(main)!;
        var stem = Path.GetFileNameWithoutExtension(main);
        var ext = Path.GetExtension(main);
        if (!Directory.Exists(dir)) return null;
        foreach (var f in Directory.GetFiles(dir, stem + ".*" + ext).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var code = Path.GetFileNameWithoutExtension(f)[(stem.Length + 1)..];
            if (!Languages.TryGetValue(code, out var lang)) continue;
            var text = DescriptionSync.Normalize(File.ReadAllText(f));
            string? title = null;
            if (text.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            {
                var nl = text.IndexOf('\n');
                title = (nl < 0 ? text[6..] : text[6..nl]).Trim();
                text = nl < 0 ? "" : text[(nl + 1)..].Trim();
            }
            if (text.Length == 0 && string.IsNullOrEmpty(title)) continue;
            all.Add(new LanguagePage { Language = lang, Title = string.IsNullOrEmpty(title) ? null : title, Description = text });
        }
        if (all.Count == 0) return null;
        return all.Where(p => sentHashes.GetValueOrDefault("lang:" + p.Language) != PageHash(p)).ToList();
    }

    public static string PageHash(LanguagePage p) => DescriptionSync.Hash((p.Title ?? "") + "\n\n" + p.Description);

    static string FileHash(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f));
    }

    /// <summary>
    /// The whole request for a mod after an upload (or from "Update Workshop page extras"): required items, gallery and
    /// translations, each only when there is something to look after. Null when there is nothing at all to send.
    /// </summary>
    public static ExtrasRequest? For(string appId, string itemId, ProjectConfig cfg, string modDir, IEnumerable<string> required,
        bool page, string? requiredRecord = null, string? sentRecord = null)
    {
        var req = RequiredRequest(appId, itemId, required, requiredRecord) ?? new ExtrasRequest { AppId = appId, ItemId = itemId };
        if (page)
        {
            var sent = LoadSent(sentRecord).GetValueOrDefault(itemId) ?? new();
            req.Gallery = GalleryFor(modDir, cfg, sent);
            // A gallery that is entirely sent already and whose videos have not changed still has to be compared with
            // Steam (an image may have been removed on the website), so it is always sent when the folder exists.
            var langs = LanguagesFor(modDir, cfg, sent, out _);
            req.Languages = langs is { Count: > 0 } ? langs : null;
        }
        return req.Required == null && req.Gallery == null && req.Languages == null ? null : req;
    }

    /// <summary>Remembers what was sent with a request that succeeded, so unchanged images and translations are not sent again.</summary>
    public static void RememberSent(ExtrasRequest req, string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultSentRecord;
                var all = LoadSent(p);
                var mine = all.GetValueOrDefault(req.ItemId) ?? new();
                if (req.Gallery != null)
                {
                    foreach (var k in mine.Keys.Where(k => k.StartsWith("img:")).ToList()) mine.Remove(k);
                    foreach (var i in req.Gallery.Images) mine["img:" + Path.GetFileName(i.Path).ToLowerInvariant()] = i.Hash;
                }
                foreach (var l in req.Languages ?? new()) mine["lang:" + l.Language] = PageHash(l);
                all[req.ItemId] = mine;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(all));
            }
            catch { }
        }
    }

    public static string DefaultSentRecord => Path.Combine(AppSettings.Folder, "page-extras.json");

    /// <summary>Per item: "img:&lt;file name&gt;" and "lang:&lt;language&gt;" to the hash last sent.</summary>
    public static Dictionary<string, Dictionary<string, string>> LoadSent(string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultSentRecord;
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }
    }

    /// <summary>The required items this app set on each Workshop item, by id.</summary>
    public static Dictionary<string, string[]> LoadRecord(string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultRecord;
                if (File.Exists(p)) return JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }
    }

    /// <summary>Remembers the required items this app looks after for an item. Never throws.</summary>
    public static void Remember(string itemId, string[] required, string? path = null)
    {
        lock (Gate)
        {
            try
            {
                var p = path ?? DefaultRecord;
                var all = LoadRecord(p);
                if (required.Length == 0) all.Remove(itemId); else all[itemId] = required;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(all));
            }
            catch { }
        }
    }

    /// <summary>
    /// Builds the request for a mod's required items: the Workshop links in its metadata (<paramref name="dir"/> is the
    /// mod folder or its staged package), or null when there is nothing to do (no links and none set before).
    /// </summary>
    public static ExtrasRequest? RequiredRequest(string appId, string itemId, IEnumerable<string> wanted, string? record = null)
    {
        var want = wanted.Where(id => id != itemId).Distinct().ToArray();
        var managed = LoadRecord(record).GetValueOrDefault(itemId) ?? Array.Empty<string>();
        if (want.Length == 0 && managed.Length == 0) return null;
        return new ExtrasRequest { AppId = appId, ItemId = itemId, Required = want, Managed = managed };
    }

    /// <summary>
    /// Applies a request through the Steam client, in a child copy of this program (SteamworksExtras.cs), for the same
    /// reasons as tags: the Steam client is told which game is asking once per process, and a crash in Valve's library
    /// must not take the window down. The caller keeps the record (<see cref="Remember"/>).
    /// </summary>
    public static async Task<ExtrasResult> ApplyOnSteamAsync(ExtrasRequest req, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return ExtrasResult.Fail("This needs the Steam client, which only works on Windows here.");
        var file = Path.Combine(Path.GetTempPath(), $"steamcmdhelper-extras-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(req), ct);
            var (output, error) = await SteamClientChild.RunAsync(new[] { ChildArg, file }, TimeSpan.FromMinutes(5), ct);
            return error != null ? ExtrasResult.Fail(error) : ParseChild(output!);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static string ChildLine(ExtrasResult r) => ResultPrefix + JsonSerializer.Serialize(r);

    public static ExtrasResult ParseChild(string output)
    {
        var line = output.Replace("\r", "").Split('\n').LastOrDefault(l => l.StartsWith(ResultPrefix, StringComparison.Ordinal));
        if (line == null) return ExtrasResult.Fail("The Steam client step stopped without saying why" + (output.Trim().Length > 0 ? ": " + output.Trim() : "."));
        try { return JsonSerializer.Deserialize<ExtrasResult>(line[ResultPrefix.Length..]) ?? ExtrasResult.Fail("The Steam client step gave no answer."); }
        catch (JsonException e) { return ExtrasResult.Fail("The Steam client step's answer could not be read: " + e.Message); }
    }
}

/// <summary>Starts a child copy of this program that talks to the Steam client, and collects what it prints.</summary>
public static class SteamClientChild
{
    /// <summary>The child's standard output, or an error saying why there is none.</summary>
    public static async Task<(string? Output, string? Error)> RunAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return (null, "Could not find this program's own exe to run.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            var read = p.StandardOutput.ReadToEndAsync(limit.Token);
            _ = p.StandardError.ReadToEndAsync(limit.Token);
            try { await p.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                if (ct.IsCancellationRequested) throw;
                return (null, $"Steam did not answer within {(timeout.TotalMinutes >= 1 ? $"{timeout.TotalMinutes:0} minutes" : $"{timeout.TotalSeconds:0} seconds")}.");
            }
            return (await read, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { return (null, e.Message); }
    }
}
