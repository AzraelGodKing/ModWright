using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The .vdf build file Publisher hands to SteamCMD: escaping and the values it carries.</summary>
public sealed class PublisherVdfTests : IDisposable
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

    // The fake SteamCMD is a shell script, so these run on Linux and macOS only (CI runs the tests on Linux too).
    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    /// <summary>Runs an upload against a fake SteamCMD that copies the build file it is given, and returns that copy.</summary>
    async Task<string> CapturedVdf(ModInfo mod)
    {
        var copy = Path.Combine(_repo.Root, "captured.vdf");
        // Arguments are: +login <account> +workshop_build_item <vdf> +quit
        var steam = _repo.Write("_steamcmd/steamcmd.sh", $"#!/bin/sh\ncp \"$4\" '{copy}'\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", steam })!.WaitForExit();

        var pub = new Publisher(_repo.Root, _cfg, steam, "account", Path.Combine(_repo.Root, "_temp"));
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

        Assert.Equal(notes.Replace("\r\n", "\n"), VdfValue(vdf, "changenote"));
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
        Assert.Equal("Alpha \"Deluxe\"", VdfValue(vdf, "title"));
        Assert.Equal("[b]My \"best\" mod[/b]\nLine two with a \\ backslash.", VdfValue(vdf, "description"));
        Assert.Equal("2", VdfValue(vdf, "visibility"));
    }

    [Fact]
    public async Task Long_change_note_is_truncated()
    {
        if (!CanFakeSteamCmd) return;
        _repo.AddMod("Alpha", changelog: "1.2.3\n" + new string('x', 9000));
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));

        var note = VdfValue(await CapturedVdf(mod), "changenote");

        Assert.StartsWith(new string('x', 7900) + "\n[i]", note);
        Assert.True(note.Length < 8000);
    }
}
