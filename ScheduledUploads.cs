using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>An upload left for Windows Task Scheduler to start later: what it runs and when.</summary>
/// <param name="Name">The task's full name in Task Scheduler (under the "SteamCMD Helper" folder).</param>
/// <param name="What">For people: "Alpha, Beta", "every mod" or "group Core".</param>
public sealed record ScheduledUpload(string Name, DateTime At, string Repo, string What, List<string> Args);

/// <summary>
/// "Upload later": the ticked mods are uploaded at a time you pick by Windows Task Scheduler running this program's
/// command line ("SteamCmdHelper upload ... --repo ..."), so the window does not have to stay open. The task is made
/// from an XML definition, so the time never depends on the PC's date format. If the PC is off at that time, it runs
/// as soon as it can. This PC's scheduled uploads are remembered in the data folder, to list and cancel.
/// </summary>
public static class ScheduledUploads
{
    public const string TaskFolder = "SteamCMD Helper";

    public static string DefaultFile => Path.Combine(AppSettings.Folder, "scheduled.json");

    /// <summary>Runs schtasks.exe with the arguments; returns its exit code and output. Tests replace it.</summary>
    public static Func<IReadOnlyList<string>, CancellationToken, Task<(int Code, string Output)>> Runner { get; set; } = RunSchtasksAsync;

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    static readonly string[] Formats = { "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };

    /// <summary>
    /// When "<paramref name="text"/>" is: "2026-10-21 18:00", "18:00" (today, or tomorrow once that has passed),
    /// "tomorrow 09:30", or "+30m", "+2h", "+1d" from <paramref name="now"/>. Null when it is none of those.
    /// </summary>
    public static DateTime? ParseWhen(string text, DateTime now)
    {
        var t = text.Trim();
        var rel = Regex.Match(t, @"^\+\s*(\d{1,4})\s*([mhd])$", RegexOptions.IgnoreCase);
        if (rel.Success)
        {
            var n = int.Parse(rel.Groups[1].Value, CultureInfo.InvariantCulture);
            return Trim(char.ToLowerInvariant(rel.Groups[2].Value[0]) switch { 'm' => now.AddMinutes(n), 'h' => now.AddHours(n), _ => now.AddDays(n) });
        }
        if (DateTime.TryParseExact(t, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full)) return full;
        var tomorrow = t.StartsWith("tomorrow ", StringComparison.OrdinalIgnoreCase);
        if (tomorrow) t = t[9..].Trim();
        if (!TimeSpan.TryParseExact(t, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var time) || time >= TimeSpan.FromDays(1)) return null;
        var at = now.Date + time;
        if (tomorrow) return at.AddDays(1);
        return at <= now ? at.AddDays(1) : at;
    }

    static DateTime Trim(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Kind);

    /// <summary>The command line the task runs: an upload of the mods (or --all, or a group) in <paramref name="repo"/>.</summary>
    public static List<string> UploadArgs(string repo, IReadOnlyList<string> mods, bool all = false, string? group = null, IEnumerable<string>? extra = null)
    {
        var args = new List<string> { "upload" };
        if (all) args.Add("--all");
        else if (group != null) args.AddRange(new[] { "--group", group });
        else args.AddRange(mods);
        args.AddRange(new[] { "--repo", repo });
        if (extra != null) args.AddRange(extra);
        return args;
    }

    /// <summary>For people: what the upload covers.</summary>
    public static string Describe(IReadOnlyList<string> mods, bool all = false, string? group = null) =>
        all ? "every mod" : group != null ? "group " + group : mods.Count <= 3 ? string.Join(", ", mods) : $"{string.Join(", ", mods.Take(3))} and {mods.Count - 3} more";

    /// <summary>
    /// The arguments as one Windows command line, quoted the way programs split them again (quotes around spaces,
    /// backslashes doubled before a quote).
    /// </summary>
    public static string CommandLine(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    public static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"')) return arg;
        var sb = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
            else sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }

    /// <summary>The task's name: under the app's folder, with the time and what it uploads, minus characters Task Scheduler refuses.</summary>
    public static string TaskName(DateTime at, string what)
    {
        var clean = Regex.Replace(what, @"[\\/:*?""<>|]", "").Trim();
        if (clean.Length > 60) clean = clean[..60].TrimEnd();
        return $@"{TaskFolder}\Upload {at:yyyy-MM-dd HHmm} {clean}".TrimEnd();
    }

    /// <summary>
    /// Task Scheduler's XML for a task that runs <paramref name="exe"/> once at <paramref name="at"/>, as the signed-in
    /// user (so it uses their saved Steam login and needs no password), and as soon as possible if the PC was off.
    /// </summary>
    public static string TaskXml(string exe, string arguments, string workDir, DateTime at, string description)
    {
        static string X(string s) => SecurityElement.Escape(s) ?? "";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{X(description)}</Description>
              </RegistrationInfo>
              <Triggers>
                <TimeTrigger>
                  <StartBoundary>{at.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}</StartBoundary>
                  <Enabled>true</Enabled>
                </TimeTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <ExecutionTimeLimit>PT12H</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{X(exe)}</Command>
                  <Arguments>{X(arguments)}</Arguments>
                  <WorkingDirectory>{X(workDir)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>This PC's scheduled uploads, soonest first, without those whose time is more than a day past.</summary>
    public static List<ScheduledUpload> Load(DateTime now, string? file = null)
    {
        file ??= DefaultFile;
        try
        {
            if (!File.Exists(file)) return new();
            var all = JsonSerializer.Deserialize<List<ScheduledUpload>>(File.ReadAllText(file)) ?? new();
            return all.Where(s => s.At > now.AddDays(-1)).OrderBy(s => s.At).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    static void Save(List<ScheduledUpload> list, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file, JsonSerializer.Serialize(list, Opts));
    }

    /// <summary>
    /// Makes the task. Returns the scheduled upload, or an error for people. <paramref name="exe"/> is this program
    /// (null: the running one).
    /// </summary>
    public static async Task<(ScheduledUpload? Done, string? Error)> AddAsync(DateTime at, DateTime now, string repo, string what, List<string> args,
        string? exe = null, string? file = null, string? tempDir = null, CancellationToken ct = default)
    {
        if (at <= now) return (null, $"{at:yyyy-MM-dd HH:mm} has already passed. Pick a later time.");
        exe ??= Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return (null, "Could not tell where this program is, to schedule it.");
        var name = TaskName(at, what);
        var xml = Path.Combine(tempDir ?? Path.GetTempPath(), $"sch-task-{Guid.NewGuid():N}.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(xml)!);
        File.WriteAllText(xml, TaskXml(exe, CommandLine(args), repo, at, $"SteamCMD Helper uploads {what} from {repo}."), Encoding.Unicode);
        (int Code, string Output) r;
        try { r = await Runner(new[] { "/Create", "/TN", name, "/XML", xml, "/F" }, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return (null, "Task Scheduler could not be started: " + e.Message); }
        finally { try { File.Delete(xml); } catch (IOException) { } }
        if (r.Code != 0) return (null, "Task Scheduler refused it: " + r.Output.Trim());
        var done = new ScheduledUpload(name, at, repo, what, args);
        file ??= DefaultFile;
        var list = Load(now, file).Where(s => !s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(done);
        Save(list.OrderBy(s => s.At).ToList(), file);
        return (done, null);
    }

    /// <summary>Removes the task and forgets it. Returns an error for people, or null. A task already gone counts as cancelled.</summary>
    public static async Task<string?> CancelAsync(string name, DateTime now, string? file = null, CancellationToken ct = default)
    {
        file ??= DefaultFile;
        (int Code, string Output) r;
        try { r = await Runner(new[] { "/Delete", "/TN", name, "/F" }, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return "Task Scheduler could not be started: " + e.Message; }
        var gone = r.Code != 0 && Regex.IsMatch(r.Output, "cannot find|does not exist", RegexOptions.IgnoreCase);
        if (r.Code != 0 && !gone) return "Task Scheduler refused it: " + r.Output.Trim();
        Save(Load(now, file).Where(s => !s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList(), file);
        return null;
    }

    /// <summary>The scheduled upload a user means: its task name, or a word of what it uploads, or its number in the list (1 = soonest).</summary>
    public static ScheduledUpload? Find(IReadOnlyList<ScheduledUpload> list, string key)
    {
        if (int.TryParse(key, out var n) && n >= 1 && n <= list.Count) return list[n - 1];
        return list.FirstOrDefault(s => s.Name.Equals(key, StringComparison.OrdinalIgnoreCase) || s.Name.EndsWith("\\" + key, StringComparison.OrdinalIgnoreCase))
            ?? list.SingleOrDefault(s => s.What.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    static async Task<(int, string)> RunSchtasksAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return (1, "Upload later needs Windows Task Scheduler.");
        var psi = new ProcessStartInfo("schtasks.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, (await output) + (await error));
    }
}
