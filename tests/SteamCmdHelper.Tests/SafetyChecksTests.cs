using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Secrets in a package, a branch behind its upstream, broken BBCode and preview images Steam won't show.</summary>
public sealed class SafetyChecksTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_secrets_" + Guid.NewGuid().ToString("N"));
    readonly string _repo, _temp, _modDir;

    public SafetyChecksTests()
    {
        _repo = Path.Combine(_root, "repo");
        _temp = Path.Combine(_root, "temp");
        _modDir = Path.Combine(_repo, "MyMod");
        Directory.CreateDirectory(_temp);
        Directory.CreateDirectory(Path.Combine(_modDir, "About"));
        File.WriteAllText(Path.Combine(_modDir, "About", "About.xml"), "<ModMetaData><name>My Mod</name><packageId>Me.MyMod</packageId><modVersion>1.0.0</modVersion></ModMetaData>");
        File.WriteAllText(Path.Combine(_modDir, "About", "changelog.txt"), "1.0.0\nFirst.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    static ProjectConfig CopyConfig() => TestConfig.RimWorld(c => { c.BuiltFiles = Array.Empty<string>(); });

    ModInfo Mod() => new() { Dir = _modDir, Folder = "MyMod", Name = "My Mod", Version = "1.0.0", WorkshopId = "2001112223" };

    void Write(string rel, string text)
    {
        var p = Path.Combine(_modDir, ProjectConfig.Norm(rel));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    List<SecretHit> Scan() => SecretScan.Find(_modDir, PackageCheck.Inspect(_modDir).Files);

    // ------------------------------------------------------------ secrets

    [Theory]
    [InlineData("token: ghp_abcdefghijklmnopqrstuvwxyz0123456789", "GitHub")]
    [InlineData("url = https://discord.com/api/webhooks/123456789012/abcdefghijklmnopqrstuvwxyzABCD", "Discord")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIE", "private key")]
    [InlineData("AWS=AKIAIOSFODNN7EXAMPLQ", "AWS")]
    [InlineData("key = lin_api_abcdefghijklmnopqrstuvwxyz0123456789AB", "Linear")]
    [InlineData("\"ConnectCache\"\n{\n\"abc\" \"def\"\n}", "ConnectCache")]
    public void Certain_secrets_are_found_by_kind_and_line(string text, string kind)
    {
        var hits = SecretScan.InText("Config/settings.txt", "first line\n" + text).ToList();
        var hit = Assert.Single(hits);
        Assert.Contains(kind, hit.Kind);
        Assert.True(hit.Certain);
        Assert.Equal(2, hit.Line);
        Assert.DoesNotContain("ghp_", hit.Describe());
    }

    [Theory]
    [InlineData("ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [InlineData("See https://discord.com/api/webhooks/ for how webhooks work")]
    [InlineData("<li>Ludeon.RimWorld</li>")]
    [InlineData("sk-short")]
    public void Ordinary_text_and_placeholders_are_left_alone(string text) => Assert.Empty(SecretScan.InText("a.txt", text));

    [Fact]
    public void Looser_shapes_are_only_warnings()
    {
        var hit = Assert.Single(SecretScan.InText("a.cfg", "api_key = 0123456789ABCDEF0123456789ABCDEF"));
        Assert.False(hit.Certain);
    }

    [Fact]
    public void Login_and_key_files_are_found_by_name()
    {
        Write("ssfn1234567890", "x");
        Write(".env", "PASSWORD=hunter2");
        Write(".env.example", "PASSWORD=");
        Write("Keys/id_ed25519", "x");
        var hits = Scan();
        Assert.Equal(new[] { ".env", "Keys/id_ed25519", "ssfn1234567890" }, hits.Select(h => h.Path).ToArray());
        Assert.All(hits, h => Assert.True(h.Certain));
    }

    [Fact]
    public void Binary_files_are_not_read_as_text()
    {
        File.WriteAllBytes(Path.Combine(_modDir, "Assemblies.dll"), new byte[] { 77, 90, 0, 0 }.Concat(System.Text.Encoding.ASCII.GetBytes("ghp_abcdefghijklmnopqrstuvwxyz0123456789")).ToArray());
        Assert.Empty(Scan());
    }

    [Fact]
    public async Task A_secret_in_the_package_stops_the_upload_without_showing_it()
    {
        Write("About/notes.txt", "webhook https://discord.com/api/webhooks/123456789012/abcdefghijklmnopqrstuvwxyzABCD");
        var r = await new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp).RunAsync(Mod(), upload: false, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("About/notes.txt", r.Message);
        Assert.Contains("Discord", r.Message);
        Assert.DoesNotContain("abcdefghij", r.Message);
    }

    [Fact]
    public async Task A_loose_match_only_warns()
    {
        Write("About/notes.txt", "steam_api_key=0123456789ABCDEF0123456789ABCDEF");
        var log = new List<string>();
        var pub = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp);
        pub.Log += log.Add;
        var r = await pub.RunAsync(Mod(), upload: false, CancellationToken.None);
        Assert.True(r.Ok, r.Message);
        Assert.Contains(log, l => l.StartsWith("Warning:") && l.Contains("Steam Web API key"));
    }

    // ------------------------------------------------------------ branch behind or not pushed

    static void Git(string dir, params string[] args)
    {
        var r = GitOps.Run(dir, new[] { "-c", "user.email=t@t", "-c", "user.name=t" }.Concat(args).ToArray());
        Assert.True(r.code == 0, r.output);
    }

    [Fact]
    public void A_branch_behind_or_ahead_of_its_upstream_is_pointed_out()
    {
        var origin = Path.Combine(_root, "origin.git");
        var work = Path.Combine(_root, "work");
        var other = Path.Combine(_root, "other");
        Directory.CreateDirectory(origin);
        Git(origin, "init", "-q", "--bare", "-b", "main");
        Git(_root, "clone", "-q", origin, work);
        Git(work, "checkout", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(work, "a.txt"), "1");
        Git(work, "add", "-A");
        Git(work, "commit", "-q", "-m", "one");
        Git(work, "push", "-q", "-u", "origin", "main");
        Assert.Equal(new GitOps.SyncState("main", "origin/main", 0, 0), GitOps.Sync(work));
        Assert.Empty(GitOps.SyncWarnings(GitOps.Sync(work), true));

        // Someone else pushes; this PC fetches but doesn't pull. It also has a commit of its own.
        Git(_root, "clone", "-q", origin, other);
        File.WriteAllText(Path.Combine(other, "b.txt"), "2");
        Git(other, "add", "-A");
        Git(other, "commit", "-q", "-m", "two");
        Git(other, "push", "-q");
        Git(work, "fetch", "-q");
        File.WriteAllText(Path.Combine(work, "c.txt"), "3");
        Git(work, "add", "-A");
        Git(work, "commit", "-q", "-m", "three");

        var sync = GitOps.Sync(work)!;
        Assert.Equal((1, 1), (sync.Ahead, sync.Behind));
        var w = GitOps.SyncWarnings(sync, needsPush: true);
        Assert.Equal(2, w.Count);
        Assert.Contains("1 commit behind origin/main", w[0]);
        Assert.Contains("not pushed", w[1]);
        Assert.Single(GitOps.SyncWarnings(sync, needsPush: false));
    }

    [Fact]
    public void No_upstream_means_nothing_to_say()
    {
        Git(_repo, "init", "-q");
        Assert.Null(GitOps.Sync(_repo));
        Assert.Empty(GitOps.SyncWarnings(null, true));
    }

    // ------------------------------------------------------------ BBCode

    [Theory]
    [InlineData("[b]bold[/b] and [url=https://x.y]link[/url]\n[list]\n[*]one\n[*]two\n[/list]")]
    [InlineData("[code][b] is fine in code[/code]")]
    [InlineData("[noparse][/i][/noparse]")]
    [InlineData("Plain text with [brackets] and [unknown]tags")]
    public void Balanced_bbcode_is_fine(string text) => Assert.Empty(SteamText.Problems(text.Replace("\\n", "\n")));

    [Fact]
    public void Open_stray_and_crossed_tags_are_named()
    {
        Assert.Equal(new[] { "never closes its [b]" }, SteamText.Problems("[b]bold"));
        Assert.Equal(new[] { "has a [/i] with no [i] before it" }, SteamText.Problems("text[/i]"));
        Assert.Equal(new[] { "closes [b] before the [i] inside it" }, SteamText.Problems("[b][i]x[/b]"));
    }

    [Fact]
    public void Bbcode_becomes_safe_html()
    {
        var html = SteamText.ToHtml("[h1]Title[/h1]\n[b]Bold[/b] <script>\n[list]\n[*]One\n[*]Two\n[/list]\n[url=javascript:alert(1)]bad[/url] [url=https://example.com/a?b=1&c=2]good[/url]\n[code][b]raw[/b][/code]".Replace("\\n", "\n"));
        Assert.Contains("<h1>Title</h1>", html);
        Assert.Contains("<b>Bold</b> &lt;script&gt;", html);
        Assert.Contains("<ul><li>One</li><li>Two</li></ul>", html);
        Assert.DoesNotContain("javascript", html.Replace("bad", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"https://example.com/a?b=1&amp;c=2\"", html);
        Assert.Contains("<pre>[b]raw[/b]</pre>", html);
        Assert.Contains("<html>", SteamText.Page("My Mod", "x"));
    }

    [Fact]
    public async Task Broken_formatting_in_a_change_note_is_warned_about()
    {
        File.WriteAllText(Path.Combine(_modDir, "About", "changelog.txt"), "1.0.0\n[b]First.");
        var log = new List<string>();
        var pub = new Publisher(_repo, CopyConfig(), "steamcmd", "modder", _temp);
        pub.Log += log.Add;
        Assert.True((await pub.RunAsync(Mod(), upload: false, CancellationToken.None)).Ok);
        Assert.Contains(log, l => l.Contains("never closes its [b]"));
    }

    // ------------------------------------------------------------ preview images

    static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
        return b;
    }

    [Fact]
    public void Image_formats_and_sizes_are_read_from_the_bytes()
    {
        Assert.Equal(new ImageInfo("PNG", 640, 360), ImageHeader.Read(Png(640, 360)));
        Assert.Equal(new ImageInfo("GIF", 300, 200), ImageHeader.Read(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 44, 1, 200, 0 }));
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 11, 8, 1, 0x68, 2, 0x80, 3, 0, 0 };
        Assert.Equal(new ImageInfo("JPEG", 640, 360), ImageHeader.Read(jpeg));
        Assert.Equal("WebP", ImageHeader.Read("RIFF\0\0\0\0WEBPVP8 "u8.ToArray())?.Format);
        Assert.Null(ImageHeader.Read("png"u8.ToArray()));
    }

    [Fact]
    public void Preview_warnings_name_the_real_format_and_small_sizes()
    {
        var jpgName = Path.Combine(_root, "Preview.jpg");
        File.WriteAllBytes(jpgName, Png(640, 360));
        Assert.Contains("really a PNG", Assert.Single(ImageHeader.PreviewWarnings(jpgName)));
        var small = Path.Combine(_root, "Small.png");
        File.WriteAllBytes(small, Png(64, 64));
        Assert.Contains("64 x 64", Assert.Single(ImageHeader.PreviewWarnings(small)));
        var webp = Path.Combine(_root, "Web.png");
        File.WriteAllBytes(webp, "RIFF\0\0\0\0WEBPVP8 "u8.ToArray());
        Assert.Contains("WebP", ImageHeader.PreviewWarnings(webp)[0]);
        Assert.Contains("WebP", WorkshopPage.CheckPreview(webp));
        Assert.Null(WorkshopPage.CheckPreview(small));
    }
}
