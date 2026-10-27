using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The .vdf build file Publisher hands to SteamCMD: escaping and the values it carries.</summary>
public sealed class PublisherVdfTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld();

    public void Dispose() => _repo.Dispose();

    // ------------------------------------------------------------ Esc

    // SteamCMD reads build files with escape sequences off: a "\n" in the file shows on Steam as backslash-n.
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("say \"hi\"", "say \u201Chi\u201D")]
    [InlineData(@"C:\mods\x", @"C:\mods\x")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("", "")]
    public void Esc_makes_text_safe_for_steamcmds_vdf_reader(string input, string expected)
    {
        Assert.Equal(expected, Publisher.Esc(input));
    }

    [Fact]
    public void Esc_never_writes_backslash_escapes()
    {
        Assert.Equal(@"\n", Publisher.Esc(@"\n"));
        Assert.DoesNotContain(@"\n", Publisher.Esc("one\ntwo\r\nthree"));
    }

    [Fact]
    public void Esc_output_has_no_straight_quotes()
    {
        var nasty = "Line \"one\"\r\nLine two\\\n\"}\n\"evil\" \"1\"";

        Assert.DoesNotContain('"', Publisher.Esc(nasty));
    }

    // ------------------------------------------------------------ build file

    /// <summary>Reads one quoted key's value from a .vdf file the way SteamCMD does: up to the next quote, nothing unescaped.</summary>
    static string VdfValue(string vdf, string key)
    {
        var m = Regex.Match(vdf, "\"" + key + "\"\\s+\"([^\"]*)\"");
        Assert.True(m.Success, "key " + key + " not found in:\n" + vdf);
        return m.Groups[1].Value;
    }

    // The fake SteamCMD is a shell script, so these run on Linux and macOS only (CI runs the tests on Linux too).
    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    /// <summary>Runs an upload against a fake SteamCMD that copies the build file it is given, and returns that copy.</summary>
    async Task<string> CapturedVdf(ModInfo mod, bool credit = false)
    {
        var copy = Path.Combine(_repo.Root, "captured.vdf");
        // Arguments are: +login <account> +workshop_build_item <vdf> +quit
        var steam = _repo.Write("_steamcmd/steamcmd.sh", "#!/bin/sh\n" + TestConfig.SkipSteamFlags + TestConfig.SkipSteamFlags + $"cp \"$4\" '{copy}'\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", steam })!.WaitForExit();

        var pub = new Publisher(_repo.Root, _cfg, steam, "account", Path.Combine(_repo.Root, "_temp")) { Credit = credit };
        await pub.RunAsync(mod, upload: true, CancellationToken.None);

        Assert.True(File.Exists(copy), "SteamCMD was not given a build file");
        return await File.ReadAllTextAsync(copy);
    }

    [Fact]
    public async Task Change_note_round_trips_through_the_build_file()
    {
        if (!CanFakeSteamCmd) return;
        var notes = "- Fixed \"quoted\" names\r\n- Paths like C:\\Steam\\mods work\n- \"}\" no longer breaks the file";
        _repo.AddMod("Alpha", changelog: "1.2.3\n" + notes);
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var vdf = await CapturedVdf(mod);

        // RimWorld sends the Add Changenote entry, which starts with its version line.
        Assert.Equal("1.2.3\n" + notes.Replace("\r\n", "\n").Replace("\"quoted\"", "\u201Cquoted\u201D").Replace("\"}\"", "\u201C}\u201D"),
            VdfValue(vdf, "changenote"));
        Assert.Equal("294100", VdfValue(vdf, "appid"));
        Assert.Equal("123456789", VdfValue(vdf, "publishedfileid"));
        Assert.DoesNotContain("\"title\"", vdf);
    }

    [Fact]
    public async Task New_item_title_and_description_round_trip_through_the_build_file()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", workshopId: null);
        _repo.Write("Alpha/About/steam_description.txt", "[b]My \"best\" mod[/b]\nLine two with a \\ backslash.");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));
        mod.Name = "Alpha \"Deluxe\"";

        var vdf = await CapturedVdf(mod);

        Assert.Equal("0", VdfValue(vdf, "publishedfileid"));
        Assert.Equal("Alpha \u201CDeluxe\u201D", VdfValue(vdf, "title"));
        Assert.Equal("[b]My \u201Cbest\u201D mod[/b]\nLine two with a \\ backslash.", VdfValue(vdf, "description"));
        Assert.Equal("2", VdfValue(vdf, "visibility"));
    }

    [Fact]
    public async Task Long_change_note_is_truncated()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", changelog: "1.2.3\n" + new string('x', 9000));
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var note = VdfValue(await CapturedVdf(mod), "changenote");

        Assert.StartsWith(("1.2.3\n" + new string('x', 9000)).Substring(0, 7900) + "\n[i]", note);
        Assert.True(note.Length < 8000);
    }

    [Fact]
    public async Task Credit_line_ends_the_change_note_when_on()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", changelog: "1.2.3\n- Fixed a thing");
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        Assert.Equal("1.2.3\n- Fixed a thing\n\n" + Publisher.CreditLine, VdfValue(await CapturedVdf(mod, credit: true), "changenote"));
        Assert.Equal("1.2.3\n- Fixed a thing", VdfValue(await CapturedVdf(mod), "changenote"));
        Assert.Contains("[url=https://github.com/AzraelGodKing/ModWright/releases/latest]", Publisher.CreditLine);
    }

    [Fact]
    public async Task Credit_line_survives_truncation()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", changelog: "1.2.3\n" + new string('x', 9000));
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var note = VdfValue(await CapturedVdf(mod, credit: true), "changenote");

        Assert.EndsWith("[/i]\n" + Publisher.CreditLine, note);
        Assert.True(note.Length <= 8000);
    }
}
