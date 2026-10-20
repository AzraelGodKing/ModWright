using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdHelper;

/// <summary>A translation of the app's window text: English text as it appears in the program, and the same text in the language.</summary>
public sealed class Translation
{
    /// <summary>The language's own name, as the language list shows it ("Deutsch").</summary>
    [JsonPropertyName("language")] public string Language { get; set; } = "";
    /// <summary>Short code ("de", "ru", "zh-Hans"); the file name without .json.</summary>
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    /// <summary>English text = translated text. A missing or empty entry stays in English.</summary>
    [JsonPropertyName("strings")] public Dictionary<string, string> Strings { get; set; } = new();
}

/// <summary>
/// The app in other languages. Window text (buttons, labels, menus, tooltips, titles) is written in English in the code
/// and looked up here by that English text, so a translation is one JSON file and anything it leaves out stays in
/// English. The built-in languages are embedded; a file in the data folder's "languages" folder adds a language or
/// overrides a built-in one, so anyone can fix or add a translation without a new release.
/// Log lines and messages built from names and numbers stay in English.
/// </summary>
public static partial class Localizer
{
    public const string ResourcePrefix = "SteamCmdHelper.Languages.";
    public static string UserFolder => Path.Combine(AppSettings.Folder, "languages");

    static Dictionary<string, string> _map = new();
    static HashSet<string> _values = new();

    /// <summary>The code of the language in use; "" for English.</summary>
    public static string Current { get; private set; } = "";

    /// <summary>Every English text the windows showed this run, for <see cref="Template"/>.</summary>
    public static readonly HashSet<string> Seen = new(StringComparer.Ordinal);

    /// <summary>The text in the current language, or the English text when the translation has none.</summary>
    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english)) return english;
        return _map.TryGetValue(english, out var t) && !string.IsNullOrWhiteSpace(t) ? t : english;
    }

    /// <summary>Whether this text is one the current language translates (and so is English text, not already translated).</summary>
    public static bool Translates(string text) => _map.TryGetValue(text, out var t) && !string.IsNullOrWhiteSpace(t) && t != text;

    /// <summary>Switches to a language by code ("" or "en" for English). An unknown code falls back to English.</summary>
    public static void Use(string code) => Use(code, Available(UserFolder));

    public static void Use(string code, IReadOnlyList<Translation> available)
    {
        var t = string.IsNullOrWhiteSpace(code) || code == "en" ? null
            : available.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        _map = t == null ? new() : new(t.Strings.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)), StringComparer.Ordinal);
        _values = new(_map.Values, StringComparer.Ordinal);
        Current = t?.Code ?? "";
    }

    public static Translation? Parse(string json, string fallbackCode)
    {
        try
        {
            var t = JsonSerializer.Deserialize<Translation>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (t == null) return null;
            if (t.Code.Length == 0) t.Code = fallbackCode;
            if (t.Language.Length == 0) t.Language = t.Code;
            t.Strings ??= new();
            return t;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The built-in translations, then any in <paramref name="userFolder"/> (which replace a built-in one with the same code).</summary>
    public static List<Translation> Available(string? userFolder)
    {
        var list = new List<Translation>();
        var asm = typeof(Localizer).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix) && n.EndsWith(".json")).OrderBy(n => n))
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s == null) continue;
            if (Parse(new StreamReader(s).ReadToEnd(), name[ResourcePrefix.Length..^5]) is { } t) list.Add(t);
        }
        if (userFolder != null && Directory.Exists(userFolder))
            foreach (var file in Directory.GetFiles(userFolder, "*.json").OrderBy(f => f))
            {
                Translation? t;
                try { t = Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)); }
                catch (IOException) { continue; }
                if (t == null) continue;
                list.RemoveAll(x => x.Code.Equals(t.Code, StringComparison.OrdinalIgnoreCase));
                list.Add(t);
            }
        return list;
    }

    /// <summary>
    /// A file to translate from: every English text known (seen in the windows this run, plus those any translation
    /// has), filled in from <paramref name="start"/> where it already has them and empty where not.
    /// </summary>
    public static string Template(Translation start, IEnumerable<string> english, IEnumerable<Translation> others)
    {
        var keys = new SortedSet<string>(english, StringComparer.Ordinal);
        foreach (var o in others) keys.UnionWith(o.Strings.Keys);
        keys.UnionWith(start.Strings.Keys);
        var t = new Translation
        {
            Language = start.Language, Code = start.Code,
            Strings = keys.ToDictionary(k => k, k => start.Strings.GetValueOrDefault(k) ?? "", StringComparer.Ordinal),
        };
        return JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
