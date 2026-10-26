using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Mod groups, the after-upload command, kept packages, exports, the note command and the setup check.</summary>
public sealed class WorkflowExtrasTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly string _data = Path.Combine(Path.GetTempPath(), "steamcmd_helper_workflow_" + Guid.NewGuid().ToString("N"));

    public WorkflowExtrasTests() => Directory.CreateDirectory(_data);

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_data, true); } catch { }
    }

    async Task<(int code, string text)> Run(AppSettings? settings, params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, _repo.Root, "steamcmd", "modder", sw, CancellationToken.None, Path.Combine(_data, "uploaded.json"), Path.Combine(_data, "temp"), settings: settings);
        return (code, sw.ToString());
    }

    // ------------------------------------------------------------ groups

    [Fact]
    public void Groups_are_kept_per_mods_folder_and_follow_a_move()
    {
        var s = new AppSettings();
        s.SetGroup(@"D:\mods", "Core", new[] { "Alpha", "Beta", "alpha" });
        s.SetGroup(@"D:\mods\", "Patches", new[] { "Gamma" });
        Assert.Equal(new[] { "Core", "Patches" }, s.GroupsFor(@"d:\MODS").Keys.ToArray());
        Assert.Equal(new[] { "Alpha", "Beta" }, s.GroupsFor(@"D:\mods")["core"].ToArray());
        Assert.Empty(s.GroupsFor(@"E:\other"));
        s.SetGroup(@"D:\mods", "core", Array.Empty<string>());
        Assert.Equal(new[] { "Patches" }, s.GroupsFor(@"D:\mods").Keys.ToArray());
        Assert.Contains(@"D:\mods", s.Folders());
        s.MoveFolder(@"D:\mods", @"F:\mods");
        Assert.Single(s.GroupsFor(@"F:\mods"));
    }

    [Fact]
    public async Task Upload_group_picks_the_saved_mods()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        _repo.AddMod("Gamma");
        var s = new AppSettings();
        s.SetGroup(_repo.Root, "Core", new[] { "Alpha", "Gamma" });
        var (code, text) = await Run(s, "upload", "--group", "core", "--dry-run");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("--- Alpha Display", text);
        Assert.Contains("--- Gamma Display", text);
        Assert.DoesNotContain("--- Beta Display", text);

        (code, text) = await Run(s, "upload", "--group", "Nope", "--dry-run");
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Groups: Core", text);
        Assert.NotNull(Cli.Parse(new[] { "upload", "--group", "Core", "--all" }).Error);
        Assert.NotNull(Cli.Parse(new[] { "list", "--group", "Core" }).Error);
        Assert.Null(Cli.Parse(new[] { "upload", "--group", "Core" }).Error);
    }

    // ------------------------------------------------------------ command palette

    static PaletteEntry E(string name, string where = "") => new(name, where, "", () => { });

    [Fact]
    public void Palette_finds_actions_by_word_starts_text_and_letters()
    {
        var all = new[] { E("Tick all", "Upload"), E("Tick changed", "Upload"), E("Download from Steam", "My Mod"), E("Options: Appearance"), E("Upload history", "My Mod") };
        Assert.Equal("Tick changed", PaletteSearch.Rank("tick ch", all)[0].Name);
        Assert.Equal("Download from Steam", PaletteSearch.Rank("steam down", all)[0].Name);
        Assert.Equal("Download from Steam", PaletteSearch.Rank("dlst", all)[0].Name);
        Assert.Equal("Options: Appearance", PaletteSearch.Rank("appear", all)[0].Name);
        Assert.Empty(PaletteSearch.Rank("zzz", all));
        Assert.Equal(all.Length, PaletteSearch.Rank("", all).Count);
        Assert.Equal("Tick changed", PaletteSearch.Clean("Tick c&hanged"));
        Assert.Equal("Groups", PaletteSearch.Clean("Groups ▾"));
        Assert.Equal("Save as", PaletteSearch.Clean("Sa&ve as..."));
    }

    // ------------------------------------------------------------ the note as Steam gets it

    [Fact]
    public void As_sent_applies_template_credit_and_length()
    {
        var cfg = TestConfig.RimWorld();
        var pub = new Publisher(_repo.Root, cfg, "", "") { NoteTemplate = "{version}\n{note}", Credit = true };
        var sent = pub.AsSent(new ModInfo { Name = "M", Version = "1.2.3" }, "- [b]Fixed[/b] it");
        Assert.StartsWith("1.2.3\n- [b]Fixed[/b] it", sent);
        Assert.EndsWith(Publisher.CreditLine, sent);
        var big = pub.AsSent(new ModInfo { Name = "M", Version = "1" }, new string('x', 9000));
        Assert.True(big.Length <= ConfirmUploadForm_MaxNote);
        Assert.Contains("see the full changelog", big);
    }

    const int ConfirmUploadForm_MaxNote = 8000;

    // ------------------------------------------------------------ reports

    [Fact]
    public void Mod_list_as_markdown_bbcode_and_csv()
    {
        var mods = new[]
        {
            new ModInfo { RelPath = "Alpha", Name = "Alpha | One", Version = "1.2", WorkshopId = "111" },
            new ModInfo { RelPath = "New", Name = "New, Mod", Version = "0.1" },
        };
        Assert.Equal("| Mod | Version | Steam Workshop |\n|---|---|---|\n| Alpha \\| One | 1.2 | [111](https://steamcommunity.com/sharedfiles/filedetails/?id=111) |\n",
            Reports.ModList(mods, Reports.Markdown));
        Assert.Equal("[list]\n[*][url=https://steamcommunity.com/sharedfiles/filedetails/?id=111]Alpha | One[/url] (v1.2)\n[/list]\n", Reports.ModList(mods, Reports.BBCode));
        var csv = Reports.ModList(mods, Reports.Csv).Split('\n');
        Assert.Equal("folder,name,version,workshop_id,workshop_url,status", csv[0]);
        Assert.StartsWith("New,\"New, Mod\",0.1,,,", csv[2]);
        Assert.Equal("'=cmd()", Reports.CsvCell("=cmd()"));
        Assert.Equal("-1.5", Reports.CsvCell("-1.5"));
        Assert.Equal("\"say \"\"hi\"\"\"", Reports.CsvCell("say \"hi\""));
    }

    [Fact]
    public void History_csv_is_newest_first_with_the_note_quoted()
    {
        var t = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var csv = Reports.HistoryCsv(new[]
        {
            new UploadEntry(t, "111", "Alpha", "1.0", "- One\n- Two", false, "294100", 2048, 12.34),
            new UploadEntry(t.AddDays(1), "111", "Alpha", "1.1", "Fix", true),
        }).Split('\n');
        Assert.Equal("time_utc,mod,version,workshop_id,created,app_id,bytes,seconds,change_note", csv[0]);
        Assert.Equal("2026-10-21T12:00:00Z,Alpha,1.1,111,yes,,0,0,Fix", csv[1]);
        Assert.Equal("2026-10-20T12:00:00Z,Alpha,1.0,111,no,294100,2048,12.3,\"- One", csv[2]);
    }

    [Fact]
    public async Task List_markdown_note_and_history_commands()
    {
        _repo.AddMod("Alpha");
        var (code, text) = await Run(null, "list", "--markdown");
        Assert.Equal(Cli.ExitOk, code);
        Assert.StartsWith("| Mod | Version | Steam Workshop |", text);
        Assert.Contains("| Alpha Display | 1.2.3 | [123456789]", text);

        (code, text) = await Run(null, "note", "Alpha");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("--- Alpha Display v1.2.3 ---", text);
        Assert.Contains("1.2.3\n- Fixed the thing that was broken.", text.Replace("\r\n", "\n"));

        UploadHistory.Add(new UploadEntry(DateTimeOffset.UtcNow, "123456789", "Alpha Display", "1.2.3", "note"), Path.Combine(_data, "history.json"));
        (code, text) = await Run(null, "history", "Alpha", "--csv");
        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains(",Alpha Display,1.2.3,123456789,no,", text);
        Assert.NotNull(Cli.Parse(new[] { "list", "--markdown", "--csv" }).Error);
        Assert.NotNull(Cli.Parse(new[] { "upload", "Alpha", "--push-tags" }).Error);
        Assert.NotNull(Cli.Parse(new[] { "note" }).Error);
    }

    // ------------------------------------------------------------ after uploading

    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    string FakeSteamCmd()
    {
        var dir = Path.Combine(_data, "steamcmd");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "steamcmd.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + TestConfig.SkipSteamFlags
            + "echo \"Logging in user '$2' to Steam Public...OK\"\necho 'Waiting for user info...OK'\n[ \"$3\" = '+workshop_build_item' ] || exit 0\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    async Task<(int code, string text)> Upload(string steam, AppSettings settings, params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, _repo.Root, steam, "modder", sw, CancellationToken.None, Path.Combine(_data, "uploaded.json"), Path.Combine(_data, "temp"), settings: settings);
        return (code, sw.ToString());
    }

    [Fact]
    public async Task Upload_runs_the_after_command_keeps_the_package_and_pushes_tags()
    {
        if (!CanFakeSteamCmd) return;
        var origin = Path.Combine(_data, "origin.git");
        Directory.CreateDirectory(origin);
        GitOps.Run(origin, "init", "-q", "--bare");
        foreach (var a in new[] { new[] { "init", "-q" }, new[] { "config", "user.name", "T" }, new[] { "config", "user.email", "t@t" }, new[] { "config", "tag.gpgsign", "false" }, new[] { "config", "commit.gpgsign", "false" } })
            GitOps.Run(_repo.Root, a);
        _repo.AddMod("Alpha", workshopId: "1111111");
        GitOps.Run(_repo.Root, "add", "-A");
        GitOps.Run(_repo.Root, "commit", "-q", "-m", "mods");
        GitOps.Run(_repo.Root, "remote", "add", "origin", origin);

        var marker = Path.Combine(_data, "after.txt");
        var settings = new AppSettings { KeepPackages = true };
        settings.SetAfter(_repo.Root, $"echo \"$SCH_MOD_NAME|$SCH_VERSION|$SCH_WORKSHOP_ID|$(cat \"$SCH_NOTE_FILE\")\" > '{marker}'");
        var (code, text) = await Upload(FakeSteamCmd(), settings, "upload", "Alpha", "--tag", "--push-tags", "--run-after");
        Assert.True(code == Cli.ExitOk, text);
        Assert.Equal("Alpha Display|1.2.3|1111111|1.2.3", File.ReadAllText(marker).Split('\n')[0]);
        Assert.Contains("Pushed Alpha-v1.2.3.", text);
        Assert.Equal(0, GitOps.Run(origin, "rev-parse", "-q", "--verify", "refs/tags/Alpha-v1.2.3").code);
        var zip = Assert.Single(PackageArchive.List(Path.Combine(_data, "packages"), "1111111"));
        using var z = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Contains(z.Entries, e => e.FullName.Replace('\\', '/') == "About/About.xml");
    }

    [Fact]
    public void Package_archive_keeps_the_newest_few()
    {
        var stage = Path.Combine(_data, "stage");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "a.txt"), "a");
        var dir = Path.Combine(_data, "packages");
        for (var i = 0; i < 4; i++) PackageArchive.Save(dir, "42", "1." + i, stage, keep: 2, now: new DateTime(2026, 1, 1, 0, 0, i));
        var kept = PackageArchive.List(dir, "42");
        Assert.Equal(2, kept.Count);
        Assert.EndsWith("_v1.3.zip", kept[0]);
        Assert.EndsWith("_v1.2.zip", kept[1]);
        Assert.Empty(PackageArchive.List(dir, "nope"));
    }
}
