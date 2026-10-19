using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>Everything one SteamCMD run printed, and how it ended.</summary>
public sealed record SteamCmdRun(int Exit, IReadOnlyList<string> Lines)
{
    public string Text => string.Join("\n", Lines);

    /// <summary>The line where Steam refused the login, or null when the login went through (or none was tried).</summary>
    public string? LoginFailure => SteamCmd.LoginFailure(Lines);
}

/// <summary>
/// Runs SteamCMD for the jobs besides uploading (game branches, licenses, downloads, game installs) and holds what
/// every run nobody types into needs.
///
/// Which login each job uses: reading a game's branches works with Steam's <see cref="Anonymous"/> login; anything
/// that touches an account's games or Workshop items (uploading, downloading items, installing a game, listing
/// licenses) needs the saved login of an account that owns the game.
/// </summary>
public static class SteamCmd
{
    public const string Anonymous = "anonymous";

    /// <summary>
    /// Put in front of every run nobody types into: SteamCMD stops at the first command that fails instead of running
    /// the rest, and never waits for a password or Steam Guard code it can't get. Without these an expired login can
    /// leave a scheduled or CI run waiting until it is killed.
    /// </summary>
    public static readonly string[] FailFast = { "+@ShutdownOnFailedCommand", "1", "+@NoPromptForPassword", "1" };

    /// <summary>Like <see cref="FailFast"/>, but carries on after a failed command, for runs where each command stands alone (one upload per mod).</summary>
    public static readonly string[] NoPrompt = { "+@ShutdownOnFailedCommand", "0", "+@NoPromptForPassword", "1" };

    /// <summary>"+login account", or "+login anonymous" when no account is given.</summary>
    public static string[] Login(string? account) => new[] { "+login", string.IsNullOrWhiteSpace(account) ? Anonymous : account.Trim() };

    const RegexOptions Ic = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]");
    static readonly Regex LoginFailed = new(@"Logging in user\b.*\.\.\.\s*FAILED|FAILED login|Login Failure|Cached credentials not found|Invalid Password|AccountLogonDenied|Rate ?Limit Exceeded|not logged on", Ic);

    public static string Clean(string line) => Ansi.Replace(line, "").Trim();

    /// <summary>The first line saying Steam refused the login, or null.</summary>
    public static string? LoginFailure(IEnumerable<string> lines) => lines.Select(Clean).FirstOrDefault(l => LoginFailed.IsMatch(l));

    /// <summary>Runs SteamCMD with no keyboard attached, passing each printed line to <paramref name="onLine"/>.</summary>
    public static async Task<SteamCmdRun> RunAsync(string steamCmd, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var lines = new List<string>();
        var exit = await Publisher.RunProcAsync(steamCmd, args, Path.GetDirectoryName(steamCmd) ?? "", l =>
        {
            lock (lines) lines.Add(l);
            onLine?.Invoke(l);
        }, ct);
        lock (lines) return new SteamCmdRun(exit, lines.ToList());
    }

    // ------------------------------------------------------------ licenses

    static readonly Regex AppsLine = new(@"^\s*-\s*Apps\s*:\s*(.*)$", Ic);

    /// <summary>
    /// The app ids in "licenses_print" output (the "- Apps : 294100, 294101, (2 in total)" line of each license), or
    /// null when the output has no license list at all (the login failed, or SteamCMD printed something else).
    /// </summary>
    public static HashSet<string>? ParseLicenses(IEnumerable<string> lines)
    {
        HashSet<string>? apps = null;
        foreach (var raw in lines)
        {
            var m = AppsLine.Match(Clean(raw));
            if (!m.Success) continue;
            apps ??= new HashSet<string>();
            var list = m.Groups[1].Value;
            var paren = list.IndexOf('(');
            if (paren >= 0) list = list[..paren];
            foreach (Match id in Regex.Matches(list, @"\d+")) apps.Add(id.Value);
        }
        return apps;
    }

    /// <summary>
    /// Whether <paramref name="account"/> has a license for <paramref name="appId"/>, from SteamCMD's licenses_print.
    /// Null when that could not be worked out (not logged in, SteamCMD failed). Games borrowed through Family Sharing
    /// are not licenses, and Steam does not let them upload to the Workshop anyway.
    /// </summary>
    public static async Task<bool?> OwnsAsync(string steamCmd, string account, string appId, CancellationToken ct)
    {
        var run = await RunAsync(steamCmd, FailFast.Concat(Login(account)).Concat(new[] { "+licenses_print", "+quit" }), null, ct);
        if (run.LoginFailure != null) return null;
        var apps = ParseLicenses(run.Lines);
        return apps == null ? null : apps.Contains(appId);
    }
}

/// <summary>
/// Reads Valve's text KeyValues format (what "app_info_print" prints): quoted keys, each followed by a quoted value or
/// a { } block. Keys are case-insensitive. A block is a <see cref="KeyValues"/>, a value a string.
/// </summary>
public sealed class KeyValues : Dictionary<string, object>
{
    public KeyValues() : base(StringComparer.OrdinalIgnoreCase) { }

    public KeyValues? Block(string key) => TryGetValue(key, out var v) ? v as KeyValues : null;
    public string? Value(string key) => TryGetValue(key, out var v) ? v as string : null;

    /// <summary>Parses text that starts with a key and its block (anything before the first quote is skipped).</summary>
    public static KeyValues Parse(string text)
    {
        var tokens = Tokens(text).GetEnumerator();
        var root = new KeyValues();
        ReadInto(root, tokens, topLevel: true);
        return root;
    }

    static void ReadInto(KeyValues into, IEnumerator<string?> tokens, bool topLevel)
    {
        while (tokens.MoveNext())
        {
            var key = tokens.Current;
            if (key == null) { if (!topLevel) return; continue; } // "}" ends this block
            if (key == "{") continue;
            if (!tokens.MoveNext()) { into[key] = ""; return; }
            var next = tokens.Current;
            if (next == "{")
            {
                var child = new KeyValues();
                ReadInto(child, tokens, topLevel: false);
                into[key] = child;
            }
            else if (next == null) { into[key] = ""; if (!topLevel) return; }
            else into[key] = next;
        }
    }

    /// <summary>The tokens: quoted strings (unescaped), "{" for a block start, and null for a block end.</summary>
    static IEnumerable<string?> Tokens(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                yield return sb.ToString();
            }
            else if (c == '{') { i++; yield return "{"; }
            else if (c == '}') { i++; yield return null; }
            else i++;
        }
    }
}
