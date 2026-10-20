using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>Something in a package that looks like a secret: what kind, where, and whether it stops the upload.</summary>
/// <param name="Line">The line it is on (1-based), or 0 when the whole file is the problem.</param>
/// <param name="Certain">The shape is specific enough that it stops the upload; otherwise it is only a warning.</param>
public sealed record SecretHit(string Path, string Kind, int Line, bool Certain)
{
    /// <summary>Where it is and what it looks like, without the secret itself.</summary>
    public string Describe() => Line > 0 ? $"{Kind} in {Path} (line {Line})" : $"{Kind}: {Path}";
}

/// <summary>
/// Looks through a staged package for things that must never become public: Steam's own saved login, private keys,
/// .env files and tokens with a well-known shape. A Workshop upload can't be taken back once subscribers have it, so a
/// certain find stops the upload. Only the kind and place are ever reported, never the value.
/// </summary>
public static class SecretScan
{
    /// <summary>Text files larger than this are not searched (they are data, not config).</summary>
    public const long MaxTextBytes = 4L * 1024 * 1024;

    static readonly string[] KeyFiles = { "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519" };
    static readonly string[] KeyStores = { ".pfx", ".p12", ".keystore", ".jks" };
    static readonly Regex SteamSentry = new(@"^ssfn\d+$", RegexOptions.IgnoreCase);
    static readonly Regex EnvFile = new(@"^\.env(\..+)?$", RegexOptions.IgnoreCase);
    static readonly string[] EnvExamples = { ".env.example", ".env.sample", ".env.template", ".env.dist" };

    /// <summary>Token shapes: (kind, pattern, certain).</summary>
    static readonly (string Kind, Regex Pattern, bool Certain)[] Tokens =
    {
        ("a private key", new Regex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED |PGP )?PRIVATE KEY(?: BLOCK)?-----"), true),
        ("a GitHub token", new Regex(@"\b(?:gh[pousr]_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{60,})\b"), true),
        ("a Discord webhook link", new Regex(@"https://(?:ptb\.|canary\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w-]{20,}", RegexOptions.IgnoreCase), true),
        ("a Slack token", new Regex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}"), true),
        ("an AWS access key", new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b"), true),
        ("a Google API key", new Regex(@"\bAIza[0-9A-Za-z_-]{35}\b"), true),
        ("a Linear API key", new Regex(@"\blin_(?:api|oauth)_[A-Za-z0-9]{30,}\b"), true),
        ("an Anthropic API key", new Regex(@"\bsk-ant-[A-Za-z0-9_-]{20,}"), true),
        ("an OpenAI API key", new Regex(@"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}"), false),
        ("what looks like a Steam Web API key", new Regex(@"(?i)\b(?:steam_?)?api_?key\b[""'\s]*[:=][""'\s]*[0-9A-F]{32}\b"), false),
        ("Steam's saved login (ConnectCache)", new Regex(@"""ConnectCache""\s*\{", RegexOptions.IgnoreCase), true),
    };

    /// <summary>
    /// Searches every file of the package staged in <paramref name="stage"/> (paths as in <paramref name="files"/>).
    /// Certain finds come first.
    /// </summary>
    public static List<SecretHit> Find(string stage, IEnumerable<PackageFile> files)
    {
        var hits = new List<SecretHit>();
        foreach (var f in files)
        {
            var name = System.IO.Path.GetFileName(f.Path);
            var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
            if (SteamSentry.IsMatch(name)) { hits.Add(new SecretHit(f.Path, "a Steam login sentry file", 0, true)); continue; }
            if (KeyFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) { hits.Add(new SecretHit(f.Path, "an SSH private key", 0, true)); continue; }
            if (KeyStores.Contains(ext)) { hits.Add(new SecretHit(f.Path, "a certificate or key store", 0, true)); continue; }
            if (EnvFile.IsMatch(name) && !EnvExamples.Contains(name, StringComparer.OrdinalIgnoreCase)) { hits.Add(new SecretHit(f.Path, "an .env file (usually passwords and keys)", 0, true)); continue; }
            if (name.Equals("request.key", StringComparison.OrdinalIgnoreCase)) { hits.Add(new SecretHit(f.Path, "SteamCMD Helper's request key file", 0, true)); continue; }
            if (f.Bytes > MaxTextBytes) continue;
            var full = System.IO.Path.Combine(stage, ProjectConfig.Norm(f.Path));
            string? text;
            try { text = ReadText(full); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            if (text != null) hits.AddRange(InText(f.Path, text));
        }
        return hits.OrderByDescending(h => h.Certain).ThenBy(h => h.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The secrets in one file's text, at most one per kind.</summary>
    public static IEnumerable<SecretHit> InText(string path, string text)
    {
        foreach (var (kind, pattern, certain) in Tokens)
        {
            var m = pattern.Match(text);
            if (!m.Success || Placeholder(m.Value)) continue;
            var line = 1 + text.AsSpan(0, m.Index).Count('\n');
            yield return new SecretHit(path, kind, line, certain);
        }
    }

    /// <summary>Values made of one repeated character or "xxxx"/"0000" runs are examples in docs, not real secrets.</summary>
    static bool Placeholder(string value)
    {
        var tail = value.Length > 16 ? value[^16..] : value;
        return tail.Distinct().Count() <= 2;
    }

    /// <summary>The file as text, or null when it looks binary (a NUL byte in its first 8 KB).</summary>
    static string? ReadText(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var head = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < head; i++)
            if (bytes[i] == 0) return null;
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>The message an upload stops with, for the certain finds.</summary>
    public static string StopMessage(IReadOnlyList<SecretHit> certain) =>
        $"The package has {(certain.Count == 1 ? "what looks like a secret" : certain.Count + " things that look like secrets")}: " +
        string.Join("; ", certain.Take(3).Select(h => h.Describe())) + (certain.Count > 3 ? $" and {certain.Count - 3} more" : "") +
        ". An upload can't be taken back once players have it, so nothing was sent. Remove it from the mod, or leave the file out of the package " +
        "(the layout's Exclude list, or an export-ignore rule in .gitattributes), and revoke the key if it was ever pushed anywhere public.";
}
