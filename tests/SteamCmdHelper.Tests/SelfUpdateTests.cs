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
    public async Task Installer_waits_out_a_file_that_is_still_in_use()
    {
        File.WriteAllText(Path.Combine(App, "SteamCmdHelper.exe"), "old exe");
        var staging = Path.Combine(Temp, "stage");
        var files = Path.Combine(staging, "files");
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "SteamCmdHelper.exe"), "new exe");
        var u = new PreparedUpdate("1.2.0", InstallKind.SingleFile, staging);

        // The first attempts fail as if Windows still held the old exe; the installer keeps trying.
        var blocker = Path.Combine(App, "SteamCmdHelper.exe" + SelfUpdate.OldSuffix);
        Directory.CreateDirectory(blocker);
        var free = Task.Run(async () => { await Task.Delay(700); Directory.Delete(blocker); });
        SelfUpdate.Install(u, App, attempts: 20);
        await free;

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(App, "SteamCmdHelper.exe")));
        Assert.True(Directory.Exists(files), "the installer runs from the staging folder, so it is left for the next start");
    }

    [Fact]
    public async Task Installer_waits_until_no_copy_has_the_exe_open()
    {
        var exe = Path.Combine(App, "SteamCmdHelper.exe");
        File.WriteAllText(exe, "old exe");
        var held = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None); // stands in for the running old copy
        var release = Task.Run(async () => { await Task.Delay(600); held.Dispose(); });

        var waited = System.Diagnostics.Stopwatch.StartNew();
        SelfUpdate.WaitUntilFree(exe, TimeSpan.FromSeconds(10));
        await release;

        Assert.True(waited.ElapsedMilliseconds >= 500, $"returned after {waited.ElapsedMilliseconds} ms, while the file was still open");
    }

    [Fact]
    public void Installer_gives_up_on_an_exe_that_stays_open()
    {
        var exe = Path.Combine(App, "SteamCmdHelper.exe");
        File.WriteAllText(exe, "old exe");
        using var held = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<TimeoutException>(() => SelfUpdate.WaitUntilFree(exe, TimeSpan.FromMilliseconds(600)));
    }

    [Fact]
    public void A_rejected_install_puts_the_old_exe_back()
    {
        File.WriteAllText(Path.Combine(App, "SteamCmdHelper-1.2.1-win-x64.exe"), "old exe");
        var staging = Path.Combine(Temp, "stage");
        Directory.CreateDirectory(Path.Combine(staging, "files"));
        File.WriteAllText(Path.Combine(staging, "files", "SteamCmdHelper.exe"), "new exe");
        var u = new PreparedUpdate("1.3.0", InstallKind.SingleFile, staging);
        SelfUpdate.Install(u, App, "SteamCmdHelper-1.2.1-win-x64.exe");
        File.Delete(Path.Combine(App, "SteamCmdHelper-1.2.1-win-x64.exe")); // antivirus removed the new copy

        Assert.True(SelfUpdate.PutBack(u, App, "SteamCmdHelper-1.2.1-win-x64.exe"));

        Assert.Equal("old exe", File.ReadAllText(Path.Combine(App, "SteamCmdHelper-1.2.1-win-x64.exe")));
        Assert.Empty(Directory.GetFiles(App, "*" + SelfUpdate.OldSuffix));
    }

    [Fact]
    public void Installer_needs_its_details()
    {
        string? said = null;
        Assert.Equal(2, SelfUpdate.Finish(new[] { "not a pid" }, m => said = m));
        Assert.NotNull(said);
    }

    [Fact]
    public void Next_start_removes_installed_downloads_but_keeps_a_newer_one()
    {
        foreach (var v in new[] { "1.1.0", "1.2.0", "1.3.0" })
            Directory.CreateDirectory(SelfUpdate.StagingFor(v, Temp));

        SelfUpdate.CleanStaging("1.2.0", Temp);

        Assert.False(Directory.Exists(SelfUpdate.StagingFor("1.1.0", Temp)));
        Assert.False(Directory.Exists(SelfUpdate.StagingFor("1.2.0", Temp)));
        Assert.True(Directory.Exists(SelfUpdate.StagingFor("1.3.0", Temp)));
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
        Assert.Contains("Modwright 1.2.3", text);
        Assert.Contains("InvalidOperationException: Boom", text);
        Assert.Equal("Crash: InvalidOperationException: Boom", CrashReport.Summary(new InvalidOperationException("Boom\nsecond line")));

        var folder = Path.Combine(_root, "crashes");
        for (var i = 0; i < CrashReport.Keep + 3; i++)
            Assert.NotNull(CrashReport.Save("x", folder, new DateTime(2026, 1, 1).AddMinutes(i)));
        Assert.Equal(CrashReport.Keep, Directory.GetFiles(folder).Length);
        Assert.False(File.Exists(Path.Combine(folder, "crash-20260101-000000-000.txt")));
    }

    [Fact]
    public void A_copy_not_installed_with_Setup_keeps_its_own_updater()
    {
        Assert.False(AppUpdates.IsInstalled);
        var r = new ReleaseInfo("9.9.9", UpdateCheck.ReleasesPage, null);
        Assert.Equal(SelfUpdate.CannotUpdateReason(r, SelfUpdate.AppDir), AppUpdates.CannotUpdateReason(r));
    }

    /// <summary>Progress&lt;T&gt; posts to the thread pool; this reports on the spot so the test can read it.</summary>
    sealed class SyncProgress(List<int> into) : IProgress<int> { public void Report(int v) => into.Add(v); }
}
