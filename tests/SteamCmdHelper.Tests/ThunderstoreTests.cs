using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Thunderstore: what it accepts, the zip it gets, and the upload steps its API expects.</summary>
public sealed class ThunderstoreTests : IDisposable
{
    readonly TempRepo _dir = new();

    public void Dispose() => _dir.Dispose();

    void Package(string manifest = """{ "name": "Better_Fishing", "version_number": "0.0.1", "website_url": "", "description": "Fish.", "dependencies": ["BepInEx-BepInExPack-5.4.2100"] }""")
    {
        _dir.Write("manifest.json", manifest);
        File.WriteAllBytes(Path.Combine(_dir.Root, "icon.png"), NewMod.Png(256, 256, 1, 2, 3));
        _dir.Write("README.md", "# Better Fishing");
        _dir.Write("plugins/BetterFishing.dll", "dll");
    }

    [Fact]
    public void A_complete_package_passes()
    {
        Package();
        Assert.True(Thunderstore.Has(_dir.Root));
        Assert.Equal("Better_Fishing", Thunderstore.NameOf(_dir.Root));
        Assert.Empty(Thunderstore.Check(_dir.Root, "1.2.0"));
    }

    [Fact]
    public void What_Thunderstore_refuses_is_named()
    {
        Package("""{ "name": "Better Fishing", "description": "%DESC%", "dependencies": ["Harmony"] }""".Replace("%DESC%", new string('x', 260)));
        File.WriteAllBytes(Path.Combine(_dir.Root, "icon.png"), NewMod.Png(512, 512, 1, 2, 3));
        File.Delete(Path.Combine(_dir.Root, "README.md"));
        var problems = string.Join("\n", Thunderstore.Check(_dir.Root, "1.2"));
        Assert.Contains("letters, digits and _", problems);
        Assert.Contains("Major.Minor.Patch", problems);
        Assert.Contains("250", problems);
        Assert.Contains("website_url", problems);
        Assert.Contains("Author-Name-1.0.0", problems);
        Assert.Contains("512 x 512", problems);
        Assert.Contains("README.md", problems);
        Assert.Equal(new[] { "has no manifest.json" }, Thunderstore.Check(Path.Combine(_dir.Root, "plugins"), "1.0.0"));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3.4", "1.2.3")]
    [InlineData("beta", "0.0.1")]
    public void The_version_sent_is_the_mods_when_it_fits(string mod, string sent) =>
        Assert.Equal(sent, Thunderstore.VersionFor(mod, JsonNode.Parse("""{ "version_number": "0.0.1" }""")!));

    [Fact]
    public void The_zip_carries_the_version_to_upload_in_its_manifest()
    {
        Package();
        var zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            Thunderstore.Zip(_dir.Root, zip, "1.4.0");
            using var z = ZipFile.OpenRead(zip);
            Assert.Equal(new[] { "README.md", "icon.png", "manifest.json", "plugins/BetterFishing.dll" }, z.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
            using var r = new StreamReader(z.GetEntry("manifest.json")!.Open());
            var m = JsonNode.Parse(r.ReadToEnd())!;
            Assert.Equal("1.4.0", m["version_number"]!.ToString());
            Assert.Equal("Better_Fishing", m["name"]!.ToString());
            Assert.Contains("0.0.1", File.ReadAllText(Path.Combine(_dir.Root, "manifest.json"))); // the mod's own file is left alone
        }
        finally { File.Delete(zip); }
    }

    sealed class FakeSite(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string Body, string? Auth)> Calls = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : request.Content is ByteArrayContent and not StringContent ? $"<{(await request.Content.ReadAsByteArrayAsync(ct)).Length} bytes>" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            return answer(request, body);
        }
    }

    static HttpResponseMessage Json(HttpStatusCode code, string json) => new(code) { Content = new StringContent(json) };

    [Fact]
    public async Task Publishing_starts_an_upload_sends_each_part_finishes_and_submits()
    {
        Package();
        var zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        Thunderstore.Zip(_dir.Root, zip, "1.0.0");
        var size = new FileInfo(zip).Length;
        var half = (int)(size / 2);
        var site = new FakeSite((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/experimental/usermedia/initiate-upload/" => Json(HttpStatusCode.Created, $$"""
                { "user_media": { "uuid": "u-1", "filename": "x.zip" }, "upload_urls": [
                  { "part_number": 1, "url": "https://storage.test/p1", "offset": 0, "length": {{half}} },
                  { "part_number": 2, "url": "https://storage.test/p2", "offset": {{half}}, "length": {{size - half}} } ] }
                """),
            "/p1" or "/p2" => new HttpResponseMessage(HttpStatusCode.OK) { Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"e" + req.RequestUri.AbsolutePath[^1] + "\"") } },
            _ => Json(HttpStatusCode.OK, "{}"),
        });
        try
        {
            var error = await Thunderstore.PublishAsync(new HttpClient(site), "tok", new ThunderstoreTarget("Azrael", "sun-haven", new[] { "mods" }), zip, CancellationToken.None);
            Assert.Null(error);
            Assert.Equal(new[] { "initiate-upload/", "p1", "p2", "finish-upload/", "submit/" }, site.Calls.Select(c => c.Url.Split('/')[^1] is "" ? c.Url.Split('/')[^2] + "/" : c.Url.Split('/')[^1]));
            Assert.All(site.Calls.Where(c => c.Url.Contains("thunderstore.io")), c => Assert.Equal("Bearer tok", c.Auth));
            Assert.Contains($"\"file_size_bytes\":{size}", site.Calls[0].Body);
            var finish = JsonNode.Parse(site.Calls[3].Body)!["parts"]!.AsArray();
            Assert.Equal("\"e1\"", finish[0]!["ETag"]!.ToString());
            Assert.Equal(2, finish[1]!["PartNumber"]!.GetValue<int>());
            var submit = JsonNode.Parse(site.Calls[4].Body)!;
            Assert.Equal("Azrael", submit["author_name"]!.ToString());
            Assert.Equal("u-1", submit["upload_uuid"]!.ToString());
            Assert.Equal("sun-haven", submit["communities"]![0]!.ToString());
            Assert.Equal("mods", submit["community_categories"]!["sun-haven"]![0]!.ToString());
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public async Task A_refused_token_and_a_failed_part_are_explained()
    {
        Package();
        var zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        Thunderstore.Zip(_dir.Root, zip, "1.0.0");
        var target = new ThunderstoreTarget("T", "c", Array.Empty<string>());
        try
        {
            var denied = new FakeSite((_, _) => Json(HttpStatusCode.Unauthorized, "{}"));
            Assert.Contains("token", await Thunderstore.PublishAsync(new HttpClient(denied), "bad", target, zip, CancellationToken.None));

            var size = new FileInfo(zip).Length;
            var broken = new FakeSite((req, _) => req.RequestUri!.Host == "storage.test" ? Json(HttpStatusCode.Forbidden, "expired")
                : req.RequestUri.AbsolutePath.EndsWith("initiate-upload/") ? Json(HttpStatusCode.Created,
                    $$"""{ "user_media": { "uuid": "u-2" }, "upload_urls": [ { "part_number": 1, "url": "https://storage.test/p1", "offset": 0, "length": {{size}} } ] }""")
                : Json(HttpStatusCode.OK, "{}"));
            var error = await Thunderstore.PublishAsync(new HttpClient(broken), "tok", target, zip, CancellationToken.None);
            Assert.Contains("part 1", error);
            Assert.Contains(broken.Calls, c => c.Url.EndsWith("u-2/abort-upload/"));
            Assert.DoesNotContain(broken.Calls, c => c.Url.EndsWith("submit/"));
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public async Task The_latest_version_is_read_from_the_package_page()
    {
        var site = new FakeSite((req, _) => req.RequestUri!.AbsolutePath == "/api/experimental/package/Azrael/Better_Fishing/"
            ? Json(HttpStatusCode.OK, """{ "latest": { "version_number": "1.3.0" } }""") : Json(HttpStatusCode.NotFound, "{}"));
        Assert.Equal("1.3.0", await Thunderstore.LatestAsync(new HttpClient(site), "Azrael", "Better_Fishing", CancellationToken.None));
        Assert.Null(await Thunderstore.LatestAsync(new HttpClient(site), "Azrael", "New_Mod", CancellationToken.None));
    }

    [Fact]
    public void Categories_and_addresses()
    {
        Assert.Equal(new[] { "mods", "quality-of-life" }, Thunderstore.Categories("Mods, quality of life;mods"));
        Assert.Equal("https://thunderstore.io/c/sun-haven/p/Azrael/Better_Fishing/", Thunderstore.PackageUrl("sun-haven", "Azrael", "Better_Fishing"));
    }

    [Fact]
    public void Settings_keep_the_target_per_mods_folder()
    {
        var s = new AppSettings();
        Assert.Null(s.ThunderstoreFor(@"C:\mods"));
        s.SetThunderstore(@"C:\mods\", "Azrael", "Sun-Haven", "mods", afterUpload: true);
        var t = s.ThunderstoreFor(@"C:\mods")!;
        Assert.Equal(("Azrael", "sun-haven"), (t.Team, t.Community));
        Assert.True(s.ThunderstoreAfterUpload(@"C:\mods"));
        Assert.False(s.ThunderstoreAfterUpload(@"D:\other"));
    }
}
