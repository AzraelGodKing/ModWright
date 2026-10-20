using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Dependency updates, the shared description footer, moving settings and the what's-new note.</summary>
public sealed class MoreIdeasTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld(c => c.UseGitArchive = false);
    string Store => Path.Combine(_repo.Root, "_store", "pages.json");

    public void Dispose() => _repo.Dispose();

    // ------------------------------------------------------------ dependency updates

    [Fact]
    public void Dependencies_updated_after_the_last_upload_are_named_newest_first()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var lastUpload = now.AddDays(-10);
        var live = new Dictionary<string, WorkshopItem>
        {
            ["1"] = new("1", true, "Harmony", Updated: now.AddDays(-2)),
            ["2"] = new("2", true, "HugsLib", Updated: now.AddDays(-20)), // before the last upload
            ["3"] = new("3", true, "Framework", Updated: now.AddHours(-5)),
            ["4"] = new("4", false), // not public: nothing to say
        };

        var lines = Dependencies.UpdatedSince("Better Storage", new[] { "1", "2", "3", "4", "5" }, lastUpload, live, now);

        Assert.Equal(2, lines.Count);
        Assert.Contains("\"Framework\", which Steam updated 5 hours ago", lines[0]);
        Assert.Contains("\"Harmony\", which Steam updated 2 days ago", lines[1]);
        Assert.Empty(Dependencies.UpdatedSince("Better Storage", new[] { "1" }, null, live, now)); // never uploaded
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(30, "30 minutes ago")]
    [InlineData(70, "an hour ago")]
    [InlineData(300, "5 hours ago")]
    [InlineData(4000, "2 days ago")]
    public void Ago_reads_naturally(double minutes, string expected) => Assert.Equal(expected, Dependencies.Ago(TimeSpan.FromMinutes(minutes)));

    // ------------------------------------------------------------ description footer

    [Fact]
    public void Footer_is_added_once_with_placeholders_filled()
    {
        const string footer = "[url=https://discord.gg/x]Discord[/url]\r\n{name} on {game}: {id}";
        var once = DescriptionSync.WithFooter("My mod does things.\r\n\r\n", footer, "Better Storage", "123", "RimWorld");

        Assert.Equal("My mod does things.\n\n[url=https://discord.gg/x]Discord[/url]\nBetter Storage on RimWorld: 123", once);
        Assert.Equal(once, DescriptionSync.WithFooter(once, footer, "Better Storage", "123", "RimWorld")); // never doubled
        Assert.Equal("Text", DescriptionSync.WithFooter("Text", "  ", "n", "1", "g"));
    }

    [Fact]
    public async Task A_changed_footer_counts_as_a_changed_description()
    {
        _repo.AddMod("Alpha", workshopId: "111");
        _repo.Write("Alpha/About/steam_description.txt", "Alpha's description");
        var mods = ModScanner.Scan(_repo.Root, _cfg);
        var record = Path.Combine(_repo.Root, "_store", "descriptions.json");
        Task<string?> Steam(string id, CancellationToken _) => Task.FromResult<string?>("Alpha's description\n\nOld footer");

        Assert.Empty(await DescriptionSync.FindChangedAsync(mods, _cfg, Steam, record, CancellationToken.None, Store, footer: "Old footer"));

        var (_, text) = Assert.Single(await DescriptionSync.FindChangedAsync(mods, _cfg, Steam, record, CancellationToken.None, Store, footer: "New footer"));
        Assert.Equal("Alpha's description\n\nNew footer", text);
    }

    [Theory]
    [InlineData("Join us on discord.gg/old", "discord.gg/old", "discord.gg/new", "", "Join us on discord.gg/new")]
    [InlineData("Text\r\n", "", "", "Ko-fi link", "Text\n\nKo-fi link")]
    [InlineData("Text\n\nKo-fi link", "", "", "Ko-fi link\r\n", "Text\n\nKo-fi link")] // already there
    [InlineData("", "", "", "Only this", "Only this")]
    public void Many_pages_get_the_same_text_change(string current, string find, string replace, string append, string expected) =>
        Assert.Equal(expected, WorkshopPage.EditText(current, find, replace, append));

    [Fact]
    public void Footer_is_kept_per_mods_folder()
    {
        var s = new AppSettings();
        s.SetFooter(@"C:\Mods", "Links\r\n");
        Assert.Equal("Links", s.FooterFor(@"C:\Mods\"));
        Assert.Equal("", s.FooterFor(@"D:\Other"));
        s.SetFooter(@"C:\Mods", "");
        Assert.Empty(s.DescriptionFooters);
    }
}

public sealed class SettingsTransferTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_transfer_" + Guid.NewGuid().ToString("N"));
    public SettingsTransferTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Settings_and_history_travel_but_steamcmd_stays()
    {
        var oldPc = Path.Combine(_root, "old");
        var newPc = Path.Combine(_root, "new");
        Directory.CreateDirectory(Path.Combine(oldPc, "manifests"));
        Directory.CreateDirectory(Path.Combine(oldPc, "steamcmd", "config"));
        var s = new AppSettings { Account = "modder", RepoPath = @"C:\Mods", SteamCmdPath = @"C:\nowhere\steamcmd.exe", WindowBounds = new[] { 1, 2, 3, 4 } };
        s.SetTemplate(@"C:\Mods", "{version}");
        File.WriteAllText(Path.Combine(oldPc, "settings.json"), System.Text.Json.JsonSerializer.Serialize(s));
        File.WriteAllText(Path.Combine(oldPc, "history.json"), "[]");
        File.WriteAllText(Path.Combine(oldPc, "manifests", "111.json"), "{}");
        File.WriteAllText(Path.Combine(oldPc, "steamcmd", "config", "config.vdf"), "secret login");
        var zip = Path.Combine(_root, "export.zip");

        Assert.Equal(3, SettingsTransfer.Export(zip, oldPc));

        Directory.CreateDirectory(newPc);
        File.WriteAllText(Path.Combine(newPc, "history.json"), "[\"mine\"]");
        var (read, error) = SettingsTransfer.Read(zip);
        Assert.Null(error);
        Assert.Equal(new[] { @"C:\Mods" }, read!.Folders(recent: false));
        read.MoveFolder(@"C:\Mods\", @"D:\Modding\Mods");
        SettingsTransfer.FitToThisPc(read);
        var backup = SettingsTransfer.Import(zip, read, newPc, new DateTime(2026, 10, 20, 9, 0, 0));

        Assert.Equal("[\"mine\"]", File.ReadAllText(Path.Combine(backup, "history.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(newPc, "history.json")));
        Assert.True(File.Exists(Path.Combine(newPc, "manifests", "111.json")));
        Assert.False(Directory.Exists(Path.Combine(newPc, "steamcmd")));
        var now = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(newPc, "settings.json")))!;
        Assert.Equal(@"D:\Modding\Mods", now.RepoPath);
        Assert.Equal("{version}", now.TemplateFor(@"D:\Modding\Mods"));
        Assert.Equal("", now.SteamCmdPath); // not on this PC: back to the default
        Assert.Empty(now.WindowBounds);
        Assert.Equal("modder", now.Account);
    }

    [Fact]
    public void Other_files_are_not_settings_exports()
    {
        var zip = Path.Combine(_root, "other.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create)) z.CreateEntry("readme.txt");
        Assert.Contains("no settings.json", SettingsTransfer.Read(zip).Error);
        File.WriteAllText(Path.Combine(_root, "not.zip"), "text");
        Assert.NotNull(SettingsTransfer.Read(Path.Combine(_root, "not.zip")).Error);
    }

    [Fact]
    public void Copy_takes_every_saved_value()
    {
        var a = new AppSettings();
        var b = new AppSettings { Account = "x", TextSize = 150, OneSession = true };
        b.SetFooter("C:/m", "f");
        a.CopyFrom(b);
        Assert.Equal("x", a.Account);
        Assert.Equal(150, a.TextSize);
        Assert.True(a.OneSession);
        Assert.Equal("f", a.FooterFor("C:/m"));
    }
}

public sealed class WhatsNewTests
{
    const string Log = "# Changelog\n\n## 1.4.0\n\n- **Big thing.** See [docs](https://x.test).\n\n## 1.3.10\n\n- `fix` one\n\n## 1.3.9\n\n- old\n\n## 1.0.0\n\nFirst release.\n";

    [Fact]
    public void Sections_since_the_last_run_are_shown_newest_first()
    {
        var s = WhatsNew.Between(Log, "1.3.9", "1.4.0");
        Assert.Equal(new[] { "1.4.0", "1.3.10" }, s.Select(x => x.Version));
        Assert.Equal("- `fix` one", s[1].Text);
        Assert.Empty(WhatsNew.Between(Log, "1.4.0", "1.4.0"));
        Assert.Equal(4, WhatsNew.Between(Log, "0", "1.4.0").Count);
    }

    [Theory]
    [InlineData("", "1.4.0", false)] // first run ever
    [InlineData("1.3.9", "1.4.0", true)]
    [InlineData("1.4.0", "1.4.0", false)]
    [InlineData("1.5.0", "1.4.0", false)] // went back a version
    public void Shown_only_after_an_update(string last, string current, bool expected) => Assert.Equal(expected, WhatsNew.ShouldShow(last, current));

    [Fact]
    public void Markdown_reads_as_plain_text() =>
        Assert.Equal("• Big thing. See docs.", WhatsNew.Plain("- **Big thing.** See [docs](https://x.test)."));

}
