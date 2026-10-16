using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SteamCmdHelper;

/// <summary>How this copy of the program was installed, which decides what an update downloads.</summary>
public enum InstallKind
{
    /// <summary>The standalone self-contained exe: updated by swapping that one file.</summary>
    SingleFile,
    /// <summary>The framework-dependent folder build: updated from the release zip.</summary>
    Folder,
}

/// <summary>A downloaded, checked update waiting in <see cref="StagingDir"/> to be installed.</summary>
public sealed record PreparedUpdate(string Version, InstallKind Kind, string StagingDir);

/// <summary>
/// Updates the program in place from a GitHub release. The download is accepted only when its SHA-256 matches the
/// release's SHA256SUMS.txt and it carries the expected version. Installing renames each file being replaced to
/// "<name>.update-old", copies the new file in, and puts everything back if any step fails. The next start deletes the
/// leftovers.
/// <para>The running program never swaps its own files: .NET reads parts of the program from its exe (or DLLs) on
/// demand for as long as it runs, so once the file is replaced the next part it needs is missing and the app crashes
/// as it closes. Instead the downloaded program is started from the staging folder (<see cref="HandOff"/>); it waits
/// for this one to exit, installs itself (<see cref="Finish"/>) and, when asked, starts the installed copy.</para>
/// </summary>
public static class SelfUpdate
{
    public const string OldSuffix = ".update-old";
    public const string ExeName = "SteamCmdHelper.exe";
    const string DllName = "SteamCmdHelper.dll";
    const long MaxDownloadBytes = 400L * 1024 * 1024;

    /// <summary>The folder the running program lives in.</summary>
    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>A folder build has the app's own DLL next to the exe; the single-file exe has it bundled inside.</summary>
    public static InstallKind KindOf(string appDir) =>
        File.Exists(Path.Combine(appDir, DllName)) ? InstallKind.Folder : InstallKind.SingleFile;

    /// <summary>Where the running program's update comes from, or null when the release has no file for this kind of install.</summary>
    public static string? AssetFor(ReleaseInfo r, InstallKind kind) => kind == InstallKind.SingleFile ? r.ExeUrl : r.ZipUrl;

    /// <summary>
    /// Why this install cannot update itself from <paramref name="r"/> (shown with the browser download instead), or
    /// null when it can.
    /// </summary>
    public static string? CannotUpdateReason(ReleaseInfo r, string appDir)
    {
        if (AssetFor(r, KindOf(appDir)) == null) return "The release has no download for this kind of install.";
        if (r.ChecksumsUrl == null) return "The release has no SHA256SUMS.txt to check the download against.";
        if (!CanWrite(appDir)) return $"This program's folder ({appDir}) cannot be written to without administrator rights.";
        return null;
    }

    /// <summary>True when a file can be created in <paramref name="dir"/>.</summary>
    public static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>The file name at the end of a download URL ("…/SteamCmdHelper-1.2.0-win-x64.exe").</summary>
    public static string FileNameOf(string url) => Uri.UnescapeDataString(new Uri(url).Segments[^1]);

    /// <summary>Reads a sha256sum listing ("&lt;hex&gt;  &lt;name&gt;" or "&lt;hex&gt; *&lt;name&gt;"), keyed by file name.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var name = line[space..].TrimStart(' ', '*').Trim();
            if (name.Length > 0) d[name] = hash.ToLowerInvariant();
        }
        return d;
    }

    public static string Sha256Of(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    /// <summary>The staging folder used for a version's update.</summary>
    public static string StagingFor(string version, string? tempRoot = null) =>
        Path.Combine(tempRoot ?? Path.GetTempPath(), "SteamCmdHelper-update", version);

    /// <summary>
    /// Downloads the release's file for <paramref name="kind"/>, checks it against SHA256SUMS.txt, and unpacks it into a
    /// staging folder. <paramref name="progress"/> gets 0-100. Throws with a readable message when anything is wrong.
    /// </summary>
    public static async Task<PreparedUpdate> PrepareAsync(ReleaseInfo r, InstallKind kind, IProgress<int>? progress,
        CancellationToken ct, HttpClient? http = null, string? tempRoot = null)
    {
        var url = AssetFor(r, kind) ?? throw new InvalidOperationException("The release has no download for this kind of install.");
        if (r.ChecksumsUrl == null) throw new InvalidOperationException("The release has no SHA256SUMS.txt to check the download against.");
        http ??= UpdateCheck.CreateClient(TimeSpan.FromMinutes(10));

        var sums = ParseChecksums(await http.GetStringAsync(r.ChecksumsUrl, ct));
        var name = FileNameOf(url);
        if (!sums.TryGetValue(name, out var expected)) throw new InvalidDataException($"SHA256SUMS.txt does not list {name}.");

        var staging = StagingFor(r.Version, tempRoot);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        var download = Path.Combine(staging, name);
        await DownloadAsync(http, url, download, progress, ct);

        var actual = Sha256Of(download);
        if (actual != expected)
            throw new InvalidDataException($"The download does not match its checksum (expected {expected[..12]}…, got {actual[..12]}…). Nothing was changed.");

        var files = Path.Combine(staging, "files");
        Directory.CreateDirectory(files);
        if (kind == InstallKind.SingleFile)
            File.Move(download, Path.Combine(files, ExeName));
        else
        {
            ZipFile.ExtractToDirectory(download, files); // refuses entries that would land outside the folder
            File.Delete(download);
            if (!File.Exists(Path.Combine(files, ExeName)) || !File.Exists(Path.Combine(files, DllName)))
                throw new InvalidDataException($"The downloaded zip does not hold {ExeName} and {DllName}.");
        }

        var got = VersionOf(Path.Combine(files, kind == InstallKind.SingleFile ? ExeName : DllName));
        if (got != null && got != r.Version)
            throw new InvalidDataException($"The download says it is version {got}, not {r.Version}. Nothing was changed.");
        progress?.Report(100);
        return new PreparedUpdate(r.Version, kind, staging);
    }

    /// <summary>The product version stamped on a program file ("1.2.0"), or null when it has none or is not one.</summary>
    static string? VersionOf(string file)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(file).ProductVersion;
            return string.IsNullOrWhiteSpace(v) ? null : v.Split('+')[0].Trim();
        }
        catch { return null; }
    }

    static async Task DownloadAsync(HttpClient http, string url, string dest, IProgress<int>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        if (total > MaxDownloadBytes) throw new InvalidDataException($"The download is {total} bytes, far more than expected.");
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long done = 0;
        int read, last = -1;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (done > MaxDownloadBytes) throw new InvalidDataException("The download is far larger than expected.");
            if (total > 0)
            {
                var pct = (int)(done * 99 / total.Value);
                if (pct != last) { progress?.Report(pct); last = pct; }
            }
        }
        if (total is long n && n != done) throw new InvalidDataException($"The download is incomplete ({done} of {n} bytes).");
    }

    /// <summary>
    /// Installs a prepared update over <paramref name="appDir"/>. Every replaced file is kept as "&lt;name&gt;.update-old"
    /// until the next start; if anything fails, the files already swapped are put back and the error is thrown.
    /// <paramref name="exeName"/> is the running exe's file name: a downloaded standalone exe usually keeps its release
    /// name ("SteamCmdHelper-1.0.0-win-x64.exe"), and replacing that file keeps the user's shortcuts working.
    /// </summary>
    public static void Apply(PreparedUpdate u, string appDir, string exeName = ExeName, bool deleteStaging = true)
    {
        var src = Path.Combine(u.StagingDir, "files");
        var done = new List<(string target, bool hadOld)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, file);
                if (u.Kind == InstallKind.SingleFile && rel == ExeName) rel = exeName;
                var target = Path.Combine(appDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var hadOld = File.Exists(target);
                if (hadOld) File.Move(target, target + OldSuffix, overwrite: true);
                done.Add((target, hadOld));
                File.Copy(file, target);
            }
        }
        catch
        {
            for (var i = done.Count - 1; i >= 0; i--)
            {
                var (target, hadOld) = done[i];
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (hadOld) File.Move(target + OldSuffix, target);
                }
                catch { /* best effort: the .update-old copy is still there to restore by hand */ }
            }
            throw;
        }
        if (deleteStaging) try { Directory.Delete(u.StagingDir, true); } catch { }
    }

    /// <summary>
    /// Deletes the files a previous update left behind. The old process may still be closing, so it retries for a few
    /// seconds. Returns how many were removed.
    /// </summary>
    public static int CleanUp(string appDir, int attempts = 10)
    {
        // A standalone exe often sits in Downloads or on the desktop: only look next to it, not in every sub-folder.
        var depth = KindOf(appDir) == InstallKind.Folder ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var removed = 0;
        for (var i = 0; i < attempts; i++)
        {
            var left = Directory.EnumerateFiles(appDir, "*" + OldSuffix, depth).ToList();
            if (left.Count == 0) break;
            foreach (var f in left)
                try { File.Delete(f); removed++; } catch { }
            if (i + 1 < attempts) Thread.Sleep(500);
        }
        return removed;
    }

    /// <summary>Starts the freshly installed program, telling it which version it replaced.</summary>
    public static void Restart(string exePath, string fromVersion)
    {
        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath)! };
        psi.ArgumentList.Add(UpdatedFromArg);
        psi.ArgumentList.Add(fromVersion);
        Process.Start(psi);
    }

    /// <summary>Passed to the new program after an update, followed by the version it replaced.</summary>
    public const string UpdatedFromArg = "--updated-from";

    /// <summary>
    /// Starts the downloaded program as an installer: "--finish-update &lt;process id&gt; &lt;app folder&gt; &lt;exe name&gt;
    /// &lt;version replaced&gt; restart|quiet".
    /// </summary>
    public const string FinishArg = "--finish-update";

    /// <summary>The downloaded program's exe in its staging folder.</summary>
    public static string StagedExe(PreparedUpdate u) => Path.Combine(u.StagingDir, "files", ExeName);

    /// <summary>
    /// Starts the downloaded program to install itself over <paramref name="appDir"/> as soon as this process exits, and
    /// then to start the installed copy when <paramref name="restart"/> is true. Nothing is changed until this process
    /// is gone. Throws when the downloaded program cannot be started.
    /// </summary>
    public static void HandOff(PreparedUpdate u, string appDir, string exeName, string fromVersion, bool restart)
    {
        var exe = StagedExe(u);
        if (!File.Exists(exe)) throw new FileNotFoundException("The downloaded update is no longer in " + u.StagingDir + ".", exe);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in new[] { FinishArg, Environment.ProcessId.ToString(), appDir, exeName, fromVersion, restart ? "restart" : "quiet" })
            psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>
    /// The installer side of <see cref="HandOff"/>, run by the downloaded program from its staging folder: waits for the
    /// old program to exit, installs this program's files over it and, when asked, starts it. <paramref name="args"/>
    /// are the ones after <see cref="FinishArg"/>. Returns the exit code; <paramref name="fail"/> gets the reason when
    /// nothing could be installed.
    /// </summary>
    public static int Finish(IReadOnlyList<string> args, Action<string> fail)
    {
        if (args.Count < 5 || !int.TryParse(args[0], out var pid)) { fail("The update was started without its details."); return 2; }
        var (appDir, exeName, fromVersion, restart) = (args[1], args[2], args[3], args[4] == "restart");
        try
        {
            WaitForExit(pid, TimeSpan.FromMinutes(1));
            var files = AppDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var update = new PreparedUpdate(UpdateCheck.CurrentVersion, KindOf(files), Path.GetDirectoryName(files)!);
            Install(update, appDir, exeName);
            if (restart) Restart(Path.Combine(appDir, exeName), fromVersion);
            return 0;
        }
        catch (Exception e)
        {
            fail($"Could not install SteamCMD Helper {UpdateCheck.CurrentVersion}: {e.Message}\n\nNothing was changed.");
            return 1;
        }
    }

    /// <summary>Waits for the program being replaced to close; returns at once when it already has.</summary>
    static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(timeout)) throw new TimeoutException("The old copy of SteamCMD Helper did not close.");
        }
        catch (ArgumentException) { /* already gone */ }
    }

    /// <summary>
    /// <see cref="Apply"/> from the installer, retried for a few seconds while Windows still holds the old program's files right after it
    /// exited (or a virus scanner is reading the new ones).
    /// </summary>
    public static void Install(PreparedUpdate u, string appDir, string exeName = ExeName, int attempts = 20)
    {
        for (var i = 1; ; i++)
        {
            try { Apply(u, appDir, exeName, deleteStaging: false); return; } // the installer runs from the staging folder
            catch (Exception e) when (i < attempts && e is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
        }
    }

    /// <summary>
    /// Deletes downloaded updates that are installed or out of date (an installer removes its own folder only once it
    /// has exited, so the next start does it). A download newer than <paramref name="current"/> is kept.
    /// </summary>
    public static void CleanStaging(string current, string? tempRoot = null)
    {
        var root = Path.GetDirectoryName(StagingFor("x", tempRoot))!;
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root))
            if (!UpdateCheck.IsNewer(Path.GetFileName(dir), current))
                try { Directory.Delete(dir, true); } catch { }
    }
}
