using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Downloading, checking and installing an update of the program itself.</summary>
public class SelfUpdateTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmdhelper_selfupdate_" + Guid.NewGuid().ToString("N"));
    string App => Path.Combine(_root, "app");
    string Temp => Path.Combine(_root, "temp");

    public SelfUpdateTests() { Directory.CreateDirectory(App); Directory.CreateDirectory(Temp); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    /// <summary>Answers each URL from a table; anything else is a 404.</summary>
    sealed class FakeWeb(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    const string ExeUrl = "https://example.test/SteamCmdHelper-1.2.0-win-x64.exe";
    const string ZipUrl = "https://example.test/SteamCmdHelper-1.2.0-win-x64-framework-dependent.zip";
    const string SumsUrl = "https://example.test/SHA256SUMS.txt";
    static readonly ReleaseInfo Release = new("1.2.0", "https://example.test/page", ExeUrl, ExeUrl, ZipUrl, SumsUrl);

    static byte[] Zip(params (string name, string text)[] entries)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write(text);
            }
        return ms.ToArray();
    }

    HttpClient Web(byte[] exe, byte[] zip, string? sums = null) => new(new FakeWeb(new()
    {
        [ExeUrl] = exe,
        [ZipUrl] = zip,
        [SumsUrl] = Encoding.UTF8.GetBytes(sums ?? $"{Sha(zip)}  SteamCmdHelper-1.2.0-win-x64-framework-dependent.zip\n{Sha(exe)} *SteamCmdHelper-1.2.0-win-x64.exe\n"),
    }));

    [Fact]
    public void Release_lists_the_exe_zip_and_checksums()
    {
        var r = UpdateCheck.Parse("""
            { "tag_name": "v1.2.0", "assets": [
              { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.test/SHA256SUMS.txt" },
              { "name": "SteamCmdHelper-1.2.0-win-x64-framework-dependent.zip", "browser_download_url": "https://example.test/fd.zip" },
              { "name": "SteamCmdHelper-1.2.0-win-x64.exe", "browser_download_url": "https://example.test/app.exe" } ] }
            """)!;

        Assert.Equal("https://example.test/app.exe", r.ExeUrl);
        Assert.Equal("https://example.test/fd.zip", r.ZipUrl);
        Assert.Equal("https://example.test/SHA256SUMS.txt", r.ChecksumsUrl);
    }

    [Fact]
    public void Checksum_listing_is_read_in_both_sha256sum_styles()
    {
        var a = new string('a', 64);
        var d = SelfUpdate.ParseChecksums($"{a}  one.exe\r\n{new string('B', 64)} *two.zip\nnot a line\n{a[..10]}  short.txt\n");

        Assert.Equal(2, d.Count);
        Assert.Equal(a, d["one.exe"]);
        Assert.Equal(new string('b', 64), d["TWO.zip"]);
    }

    [Fact]
    public async Task Standalone_exe_is_downloaded_checked_and_swapped_in_under_the_running_name()
    {
        var exe = Encoding.UTF8.GetBytes("new exe");
        File.WriteAllText(Path.Combine(App, "SteamCmdHelper-1.0.0-win-x64.exe"), "old exe");
        var reports = new List<int>();

        var u = await SelfUpdate.PrepareAsync(Release, InstallKind.SingleFile, new SyncProgress(reports), CancellationToken.None, Web(exe, Zip()), Temp);
        SelfUpdate.Apply(u, App, "SteamCmdHelper-1.0.0-win-x64.exe");

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(App, "SteamCmdHelper-1.0.0-win-x64.exe")));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(App, "SteamCmdHelper-1.0.0-win-x64.exe" + SelfUpdate.OldSuffix)));
        Assert.False(File.Exists(Path.Combine(App, SelfUpdate.ExeName)));
        Assert.Equal(100, reports[^1]);
        Assert.False(Directory.Exists(u.StagingDir));

        Assert.Equal(1, SelfUpdate.CleanUp(App, attempts: 1));
        Assert.Single(Directory.GetFiles(App));
    }

    [Fact]
    public async Task Folder_install_takes_the_zip_and_keeps_its_sub_folders()
    {
        File.WriteAllText(Path.Combine(App, "SteamCmdHelper.dll"), "old dll");
        File.WriteAllText(Path.Combine(App, "SteamCmdHelper.exe"), "old exe");
        var zip = Zip(("SteamCmdHelper.exe", "new exe"), ("SteamCmdHelper.dll", "new dll"), ("de/SteamCmdHelper.resources.dll", "res"));
        Assert.Equal(InstallKind.Folder, SelfUpdate.KindOf(App));

        var u = await SelfUpdate.PrepareAsync(Release, InstallKind.Folder, null, CancellationToken.None, Web(Encoding.UTF8.GetBytes("x"), zip), Temp);
        SelfUpdate.Apply(u, App);

        Assert.Equal("new dll", File.ReadAllText(Path.Combine(App, "SteamCmdHelper.dll")));
        Assert.Equal("res", File.ReadAllText(Path.Combine(App, "de", "SteamCmdHelper.resources.dll")));
        Assert.Equal(2, SelfUpdate.CleanUp(App, attempts: 1));
    }

    [Fact]
    public async Task Download_that_does_not_match_its_checksum_changes_nothing()
    {
        var web = Web(Encoding.UTF8.GetBytes("tampered"), Zip(), $"{new string('0', 64)}  SteamCmdHelper-1.2.0-win-x64.exe\n");

        var e = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SelfUpdate.PrepareAsync(Release, InstallKind.SingleFile, null, CancellationToken.None, web, Temp));
        Assert.Contains("checksum", e.Message);
    }

    [Fact]
    public async Task Download_missing_from_the_checksum_list_is_refused()
    {
        var web = Web(Encoding.UTF8.GetBytes("new"), Zip(), $"{new string('0', 64)}  something-else.exe\n");

        var e = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SelfUpdate.PrepareAsync(Release, InstallKind.SingleFile, null, CancellationToken.None, web, Temp));
        Assert.Contains("does not list", e.Message);
    }

    [Fact]
    public async Task Zip_without_the_program_is_refused()
    {
        var zip = Zip(("readme.txt", "hi"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SelfUpdate.PrepareAsync(Release, InstallKind.Folder, null, CancellationToken.None, Web(Encoding.UTF8.GetBytes("x"), zip), Temp));
    }

    [Fact]
    public void Failed_install_puts_every_replaced_file_back()
    {
        File.WriteAllText(Path.Combine(App, "SteamCmdHelper.dll"), "old dll");
        File.WriteAllText(Path.Combine(App, "SteamCmdHelper.exe"), "old exe");
        Directory.CreateDirectory(Path.Combine(App, "blocker.txt")); // a folder where the update wants a file
        var staging = Path.Combine(Temp, "stage");
        var files = Path.Combine(staging, "files");
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "SteamCmdHelper.dll"), "new dll");
        File.WriteAllText(Path.Combine(files, "SteamCmdHelper.exe"), "new exe");
        File.WriteAllText(Path.Combine(files, "blocker.txt"), "x");

        Assert.ThrowsAny<Exception>(() => SelfUpdate.Apply(new PreparedUpdate("1.2.0", InstallKind.Folder, staging), App));

        Assert.Equal("old dll", File.ReadAllText(Path.Combine(App, "SteamCmdHelper.dll")));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(App, "SteamCmdHelper.exe")));
        Assert.Empty(Directory.GetFiles(App, "*" + SelfUpdate.OldSuffix));
    }

    [Fact]
    public void No_in_place_update_without_checksums_or_a_matching_file()
    {
        Assert.NotNull(SelfUpdate.CannotUpdateReason(Release with { ChecksumsUrl = null }, App));
        Assert.NotNull(SelfUpdate.CannotUpdateReason(Release with { ExeUrl = null }, App));
        Assert.Null(SelfUpdate.CannotUpdateReason(Release, App));
    }

    [Fact]
    public void Update_command_takes_check_and_nothing_else_does()
    {
        Assert.True(Cli.IsCommand(new[] { "update" }));
        var o = Cli.Parse(new[] { "update", "--check" });
        Assert.Null(o.Error);
        Assert.True(o.CheckOnly);
        Assert.NotNull(Cli.Parse(new[] { "list", "--check" }).Error);
    }

    [Fact]
    public void Crash_report_has_the_version_and_the_error_and_old_ones_are_pruned()
    {
        var text = CrashReport.Build(new InvalidOperationException("Boom\nsecond line"), "1.2.3", new DateTime(2026, 10, 15, 12, 0, 0));
        Assert.Contains("SteamCMD Helper 1.2.3", text);
        Assert.Contains("InvalidOperationException: Boom", text);
        Assert.Equal("Crash: InvalidOperationException: Boom", CrashReport.Summary(new InvalidOperationException("Boom\nsecond line")));

        var folder = Path.Combine(_root, "crashes");
        for (var i = 0; i < CrashReport.Keep + 3; i++)
            Assert.NotNull(CrashReport.Save("x", folder, new DateTime(2026, 1, 1).AddMinutes(i)));
        Assert.Equal(CrashReport.Keep, Directory.GetFiles(folder).Length);
        Assert.False(File.Exists(Path.Combine(folder, "crash-20260101-000000-000.txt")));
    }

    /// <summary>Progress&lt;T&gt; posts to the thread pool; this reports on the spot so the test can read it.</summary>
    sealed class SyncProgress(List<int> into) : IProgress<int> { public void Report(int v) => into.Add(v); }
}
