using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>
/// Tells people about an upload: a Discord channel through a webhook, and a GitHub release made with the gh tool.
/// Both are opt-in per mods folder and run only after Steam has accepted the upload.
/// </summary>
public static class Announce
{
    static readonly Regex WebhookUrl = new(@"^https://(?:(?:canary|ptb)\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w-]+/?$", RegexOptions.IgnoreCase);

    /// <summary>True for a Discord webhook link ("https://discord.com/api/webhooks/&lt;id&gt;/&lt;token&gt;").</summary>
    public static bool IsDiscordWebhook(string url) => WebhookUrl.IsMatch(url.Trim());

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SteamCmdHelper", "1"));
        return c;
    }

    /// <summary>
    /// The Discord message for an upload: one embed linking to the Workshop item, with the change note. Mentions are
    /// switched off, so an "@everyone" in a changelog does not ping a whole server.
    /// </summary>
    public static string DiscordPayload(string mod, string version, string workshopId, string note, string game)
    {
        var body = SteamToMarkdown(note);
        if (body.Length > 3900) body = body[..3900] + "\n…";
        var payload = new
        {
            username = "Modwright",
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = $"{mod} {version}".Trim() + " is out",
                    url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}",
                    description = body.Length > 0 ? body : "Updated on the Steam Workshop.",
                    color = 0x3B82F6,
                    footer = new { text = game.Length > 0 ? $"{game} · Steam Workshop" : "Steam Workshop" },
                },
            },
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Posts to a Discord webhook. Returns null when Discord accepted it, else why not.</summary>
    public static async Task<string?> PostDiscordAsync(string webhook, string payload, HttpClient? http = null, CancellationToken ct = default)
    {
        if (!IsDiscordWebhook(webhook)) return "That is not a Discord webhook link.";
        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await (http ?? Http).PostAsync(webhook.Trim(), content, ct);
            if (resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync(ct);
            return $"Discord answered {(int)resp.StatusCode} {resp.ReasonPhrase}{(text.Length > 0 && text.Length < 300 ? ": " + text : "")}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { return "Could not reach Discord: " + e.Message; }
    }

    /// <summary>Steam's BBCode change notes as Discord Markdown ([b] to **, [url=x]y[/url] to [y](x), list items to "- ").</summary>
    public static string SteamToMarkdown(string s)
    {
        s = s.Replace("\r\n", "\n");
        s = Regex.Replace(s, @"\[url=([^\]]+)\](.*?)\[/url\]", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[/?(?:b|h1|h2|h3)\]", "**", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[/?i\]", "*", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[/?u\]", "__", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[/?strike\]", "~~", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[\*\]\s*", "- ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[/?(?:list|olist|quote|code|spoiler|noparse|hr|table|tr|td|th)[^\]]*\]", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
    }

    /// <summary>Runs gh in <paramref name="repo"/>; (exit code, output). Exit -1 when gh is not installed.</summary>
    public static (int code, string output) RunGh(string repo, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("gh")
            {
                WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            var o = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(120000)) { try { p.Kill(true); } catch { } return (-1, "gh did not finish within two minutes."); }
            return (p.ExitCode, (o + err.Result).Trim());
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, "The GitHub command-line tool (gh) is not installed. Get it from cli.github.com and run \"gh auth login\" once."); }
        catch (Exception e) { return (-1, "Could not run gh: " + e.Message); }
    }

    /// <summary>
    /// Makes a GitHub release for an upload on the commit that was packaged. Returns the release link, or throws with
    /// gh's message (not installed, not logged in, commit not pushed, release already there, ...).
    /// </summary>
    public static string CreateGitHubRelease(string repo, string tag, string title, string notes, string commit)
    {
        var file = Path.Combine(Path.GetTempPath(), "steamcmd_helper_notes_" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            File.WriteAllText(file, SteamToMarkdown(notes), new UTF8Encoding(false));
            var r = RunGh(repo, "release", "create", tag, "--title", title, "--notes-file", file, "--target", commit);
            if (r.code != 0) throw new InvalidOperationException(r.output.Length > 0 ? r.output : "gh failed with exit code " + r.code);
            return r.output.Split('\n').LastOrDefault(l => l.StartsWith("https://"))?.Trim() ?? r.output;
        }
        finally { try { File.Delete(file); } catch { } }
    }
}
