using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

public sealed record PublishResult(bool Ok, string Message, string? NewId = null);

/// <summary>
/// Packages one mod, writes the SteamCMD build file and runs the upload. Updates send only the content and the
/// change note (title, description and preview on Steam are left alone); creating makes a PRIVATE item.
/// </summary>
public sealed class Publisher
{
    readonly string _repo;
    readonly ProjectConfig _cfg;
    readonly string _steamCmd;
    readonly string _account;

    public event Action<string>? Log;
    public event Action<string, int>? Stage;

    public Publisher(string repo, ProjectConfig cfg, string steamCmd, string account)
    {
        _repo = repo; _cfg = cfg; _steamCmd = steamCmd; _account = account;
    }

    void Say(string s) => Log?.Invoke(s);
    void At(string s, int pct) => Stage?.Invoke(s, pct);

    public async Task<PublishResult> RunAsync(ModInfo m, bool upload, CancellationToken ct)
    {
        At("Reading changelog...", 5);
        var clPath = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.Changelog));
        var (_, notes) = ModScanner.ReadChangelog(clPath, _cfg);
        if (notes.Length > 7900) notes = notes.Substring(0, 7900) + "\n[i]...see the full changelog on the project page.[/i]";

        var stageRoot = Path.Combine(Path.GetTempPath(), "steamcmd_helper_" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(stageRoot, m.Folder);
        Directory.CreateDirectory(stageRoot);

        At("Packaging...", 10);
        await PackageAsync(m, stageRoot, stage, ct);

        var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
        var mb = Math.Round(files.Sum(f => new FileInfo(f).Length) / 1048576.0, 2);

        var extra = "";
        if (!m.OnSteam)
        {
            var preview = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.Preview));
            var desc = ReadDescription(m);
            if (desc.Length < 20) return Fail(stageRoot, $"No description found. Add {_cfg.SteamDescriptionFile}.");
            extra = $"""

                "title"           "{Esc(m.Name)}"
                "description"     "{Esc(desc)}"
                "previewfile"     "{preview}"
                "visibility"      "2"
            """;
        }

        var vdf = $$"""
            "workshopitem"
            {
                "appid"           "{{_cfg.AppId}}"
                "publishedfileid" "{{m.WorkshopId ?? "0"}}"
                "contentfolder"   "{{stage}}"{{extra}}
                "changenote"      "{{Esc(notes)}}"
            }
            """;
        var vdfPath = Path.Combine(stageRoot, m.Folder + ".vdf");
        await File.WriteAllTextAsync(vdfPath, vdf, new UTF8Encoding(false), ct);

        Say($"{(m.OnSteam ? "Workshop id " + m.WorkshopId : "New private item")} | version {m.Version} | {files.Length} files, {mb} MB | notes {notes.Length} chars");
        At("Package ready", 20);

        if (!upload)
        {
            Say("Dry run: nothing uploaded. Package and build file are in " + stageRoot);
            return new PublishResult(true, "Dry run OK");
        }

        var failed = false;
        var exit = await RunProcAsync(_steamCmd,
            new[] { "+login", _account, "+workshop_build_item", vdfPath, "+quit" },
            Path.GetDirectoryName(_steamCmd) ?? "",
            line =>
            {
                Say(line);
                if (Regex.IsMatch(line, @"\bERROR\b|\bFAILED\b", RegexOptions.IgnoreCase)) failed = true;
                MapStage(line);
            }, ct);

        if (exit != 0 || failed)
            return Fail(stageRoot, $"SteamCMD reported a problem (exit code {exit})." + (m.OnSteam ? "" : " If this was a create, check your Steam profile before trying again."), keepFiles: true);

        string? newId = null;
        if (!m.OnSteam)
        {
            var mm = Regex.Match(await File.ReadAllTextAsync(vdfPath, ct), "\"publishedfileid\"\\s+\"(\\d+)\"");
            if (mm.Success && mm.Groups[1].Value != "0") newId = mm.Groups[1].Value;
            if (newId == null)
                return Fail(stageRoot, "Upload finished but the new Workshop id could not be found. Check your Steam profile (the item is private), put its number in " + _cfg.WorkshopIdFile + ", and do NOT create it again.", keepFiles: true);

            var idFile = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.WorkshopIdFile));
            Directory.CreateDirectory(Path.GetDirectoryName(idFile)!);
            await File.WriteAllTextAsync(idFile, newId, new UTF8Encoding(false), ct);
            Say($"Created {m.Name} {m.Version} as PRIVATE item {newId}. Saved to {_cfg.WorkshopIdFile} (commit it).");
        }
        else Say($"Uploaded {m.Name} {m.Version}");

        TryDelete(stageRoot);
        At("Uploaded", 100);
        return new PublishResult(true, m.OnSteam ? "Uploaded" : "Created", newId);
    }

    // ------------------------------------------------------------ packaging

    async Task PackageAsync(ModInfo m, string stageRoot, string stage, CancellationToken ct)
    {
        if (_cfg.UseGitArchive && ModScanner.IsGitRepo(_repo))
        {
            var zip = Path.Combine(stageRoot, "package.zip");
            var code = await RunProcAsync("git", new[] { "archive", "--format=zip", "-o", zip, "HEAD", m.Folder }, _repo, Say, ct);
            if (code != 0 || !File.Exists(zip)) throw new InvalidOperationException("git archive failed. Is this mod committed?");
            ZipFile.ExtractToDirectory(zip, stageRoot, true);
            File.Delete(zip);
            if (!Directory.Exists(stage)) throw new InvalidOperationException("git archive produced nothing for " + m.Folder + ". Commit the mod first.");
        }
        else
        {
            CopyDir(m.Dir, stage, _cfg.Exclude ?? Array.Empty<string>());
        }

        // Build outputs usually are not tracked by git, so take them from the working tree.
        foreach (var glob in _cfg.BuiltFiles ?? Array.Empty<string>())
        {
            foreach (var f in ModScanner.MatchGlob(m.Dir, glob))
            {
                var rel = Path.GetRelativePath(m.Dir, f);
                var dest = Path.Combine(stage, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }
    }

    static void CopyDir(string src, string dst, string[] exclude)
    {
        Directory.CreateDirectory(dst);
        bool Skip(string name) => exclude.Any(p => p.Contains('*')
            ? Regex.IsMatch(name, "^" + Regex.Escape(p).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase)
            : name.Equals(p, StringComparison.OrdinalIgnoreCase));
        foreach (var f in Directory.GetFiles(src))
            if (!Skip(Path.GetFileName(f))) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            if (!Skip(Path.GetFileName(d))) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)), exclude);
    }

    string ReadDescription(ModInfo m)
    {
        var f = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.SteamDescriptionFile));
        if (File.Exists(f)) return File.ReadAllText(f).Trim();
        var marker = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.ModMarker));
        try { if (marker.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return ModScanner.ReadXml(marker, _cfg.DescriptionXPath) ?? ""; }
        catch { }
        return "";
    }

    // ------------------------------------------------------------ helpers

    void MapStage(string line)
    {
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;
        if (line.Contains("Logging in", ic)) At("Logging in to Steam...", 30);
        else if (line.Contains("Logged in OK", ic) || line.Contains("Waiting for user info...OK", ic)) At("Logged in", 35);
        else if (line.Contains("Uploading content", ic)) At("Uploading files...", 50);
        else if (line.Contains("Uploading preview", ic)) At("Uploading preview image...", 70);
        else if (line.Contains("Committing", ic)) At("Committing update...", 88);
        else if (line.Contains("Success", ic)) At("Steam accepted the update", 95);
    }

    /// <summary>Escapes text for a Valve .vdf quoted value.</summary>
    public static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\n").Replace("\n", "\\n");

    static PublishResult Fail(string stageRoot, string message, bool keepFiles = false)
    {
        if (!keepFiles) TryDelete(stageRoot);
        return new PublishResult(false, message);
    }

    static void TryDelete(string dir) { try { Directory.Delete(dir, true); } catch { } }

    static async Task<int> RunProcAsync(string file, IEnumerable<string> args, string workDir, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.StandardInput.Close(); // no keyboard: a login prompt fails fast instead of hanging

        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        await Task.Run(() => p.WaitForExit());
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }
}
