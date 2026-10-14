using System.Text.RegularExpressions;

namespace SteamCmdHelper.Tests;

public class PublisherTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = new() { UseGitArchive = false };

    public void Dispose() => _repo.Dispose();

    // ------------------------------------------------------------ Esc

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("say \"hi\"", "say \\\"hi\\\"")]
    [InlineData(@"C:\mods\x", @"C:\\mods\\x")]
    [InlineData("a\nb", "a\\nb")]
    [InlineData("a\r\nb", "a\\nb")]
    [InlineData("", "")]
    public void Esc_escapes_vdf_values(string input, string expected)
    {
        Assert.Equal(expected, Publisher.Esc(input));
    }

    [Fact]
    public void Esc_escapes_backslashes_before_adding_its_own()
    {
        // A literal backslash-n in the text must stay distinguishable from a real newline.
        Assert.Equal(@"\\n", Publisher.Esc(@"\n"));
        Assert.Equal(@"\\\""", Publisher.Esc("\\\""));
    }

    [Fact]
    public void Esc_output_has_no_unescaped_quotes_or_newlines()
    {
        var nasty = "Line \"one\"\r\nLine two\\\n\"}\n\"evil\" \"1\"";

        var esc = Publisher.Esc(nasty);

        Assert.DoesNotContain('\n', esc);
        Assert.DoesNotMatch(new Regex(@"(?<!\\)(\\\\)*"""), esc);
    }

    // ------------------------------------------------------------ build file

    static string Unescape(string s) =>
        Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value == "n" ? "\n" : m.Groups[1].Value);

    /// <summary>Reads one quoted key's value from a .vdf file, undoing <see cref="Publisher.Esc"/>.</summary>
    static string VdfValue(string vdf, string key)
    {
        var m = Regex.Match(vdf, "\"" + key + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"");
        Assert.True(m.Success, "key " + key + " not found in:\n" + vdf);
        return Unescape(m.Groups[1].Value);
    }

    async Task<string> DryRunVdf(ModInfo mod)
    {
        var log = new List<string>();
        var pub = new Publisher(_repo.Root, _cfg, "steamcmd-not-used", "account");
        pub.Log += log.Add;

        var res = await pub.RunAsync(mod, upload: false, CancellationToken.None);

        Assert.True(res.Ok, res.Message);
        var line = Assert.Single(log, l => l.StartsWith("Dry run:"));
        var stageRoot = line.Substring(line.LastIndexOf(" in ", StringComparison.Ordinal) + 4);
        try { return await File.ReadAllTextAsync(Path.Combine(stageRoot, mod.Folder + ".vdf")); }
        finally { Directory.Delete(stageRoot, true); }
    }

    [Fact]
    public async Task Dry_run_writes_escaped_change_note()
    {
        var notes = "- Fixed \"quoted\" names\r\n- Paths like C:\\Steam\\mods work\n- \"}\" no longer breaks the file";
        _repo.AddMod("Alpha", changelog: "1.2.3\n" + notes);
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var vdf = await DryRunVdf(mod);

        Assert.Equal(notes.Replace("\r\n", "\n"), VdfValue(vdf, "changenote"));
        Assert.Equal("294100", VdfValue(vdf, "appid"));
        Assert.Equal("123456789", VdfValue(vdf, "publishedfileid"));
        Assert.DoesNotContain("\"title\"", vdf);
    }

    [Fact]
    public async Task Dry_run_for_new_item_escapes_title_and_description()
    {
        _repo.AddMod("Alpha", workshopId: null);
        _repo.Write("Alpha/About/steam_description.txt", "[b]My \"best\" mod[/b]\nLine two with a \\ backslash.");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));
        mod.Name = "Alpha \"Deluxe\"";

        var vdf = await DryRunVdf(mod);

        Assert.Equal("0", VdfValue(vdf, "publishedfileid"));
        Assert.Equal("Alpha \"Deluxe\"", VdfValue(vdf, "title"));
        Assert.Equal("[b]My \"best\" mod[/b]\nLine two with a \\ backslash.", VdfValue(vdf, "description"));
        Assert.Equal("2", VdfValue(vdf, "visibility"));
    }

    [Fact]
    public async Task New_item_without_description_fails()
    {
        _repo.AddMod("Alpha", workshopId: null);
        _repo.Write("Alpha/About/About.xml", "<ModMetaData><name>Alpha</name><modVersion>1.2.3</modVersion></ModMetaData>");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));
        var pub = new Publisher(_repo.Root, _cfg, "steamcmd-not-used", "account");

        var res = await pub.RunAsync(mod, upload: false, CancellationToken.None);

        Assert.False(res.Ok);
        Assert.Contains("No description found", res.Message);
    }

    [Fact]
    public async Task Long_change_note_is_truncated()
    {
        _repo.AddMod("Alpha", changelog: "1.2.3\n" + new string('x', 9000));
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var note = VdfValue(await DryRunVdf(mod), "changenote");

        Assert.StartsWith(new string('x', 7900) + "\n[i]", note);
        Assert.True(note.Length < 8000);
    }
}
