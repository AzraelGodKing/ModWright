using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Raising a mod's version: the next patch, minor or major number, and writing it into JSON and key=value files.</summary>
public sealed class VersionBumpTests : IDisposable
{
    readonly TempRepo _repo = new();
    public void Dispose() => _repo.Dispose();

    [Theory]
    [InlineData("1.2.3", ModScanner.Bump.Patch, "1.2.4")]
    [InlineData("1.2.3", ModScanner.Bump.Minor, "1.3.0")]
    [InlineData("1.2.3", ModScanner.Bump.Major, "2.0.0")]
    [InlineData("1.2", ModScanner.Bump.Patch, "1.2.1")]
    [InlineData("1.2", ModScanner.Bump.Minor, "1.3")]
    [InlineData("1.2.3.4", ModScanner.Bump.Patch, "1.2.3.5")]
    [InlineData("1.2.3.4", ModScanner.Bump.Minor, "1.3.0.0")]
    [InlineData("v1.9.9", ModScanner.Bump.Patch, "1.9.10")]
    [InlineData("1", ModScanner.Bump.Major, "2.0")]
    [InlineData("1", ModScanner.Bump.Patch, "1.0.1")]
    public void Next_version(string from, ModScanner.Bump part, string expected) =>
        Assert.Equal(expected, ModScanner.NextVersion(from, part));

    [Theory]
    [InlineData("")]
    [InlineData("1.2-beta")]
    [InlineData("dev")]
    public void Not_a_version_has_no_next(string from) => Assert.Null(ModScanner.NextVersion(from, ModScanner.Bump.Patch));

    [Theory]
    [InlineData("version=\"1.0\"\n", "version=\"1.1\"\n")]
    [InlineData("name: Foo\nversion: 1.0\n", "name: Foo\nversion: 1.1\n")]
    [InlineData("  version = \"1.0\",\r\n", "  version = \"1.1\",\r\n")]
    [InlineData("supported_version=\"3.*\"\nversion=\"1.0\"\n", "supported_version=\"3.*\"\nversion=\"1.1\"\n")]
    public void Key_value_keeps_its_style(string text, string expected) =>
        Assert.Equal(expected, ModScanner.ReplaceKeyValue(text, "version", "1.1"));

    [Fact]
    public void Key_value_without_the_key_is_not_added() => Assert.Null(ModScanner.ReplaceKeyValue("name=\"x\"\n", "version", "1.1"));

    static ModInfo Mod(string dir) => new() { Dir = dir, Folder = Path.GetFileName(dir) };

    [Fact]
    public void Paradox_descriptor_version_is_written()
    {
        var file = _repo.Write("Ships/descriptor.mod", "name=\"Ships\"\nversion=\"2.3.1\"\nsupported_version=\"3.12.*\"\n");
        var cfg = ProjectConfig.GetPreset(ProjectConfig.ParadoxName);

        Assert.Null(ModScanner.SetVersion(Mod(Path.Combine(_repo.Root, "Ships")), cfg, "2.4.0"));

        Assert.Equal("name=\"Ships\"\nversion=\"2.4.0\"\nsupported_version=\"3.12.*\"\n", File.ReadAllText(file));
    }

    [Fact]
    public void Json_marker_version_is_written_at_its_path()
    {
        var file = _repo.Write("Mod/mod.json", "{ \"info\": { \"title\": \"Ünïcode\", \"version\": \"0.9.0\" }, \"other\": 1 }");
        var cfg = new ProjectConfig { ModMarker = "mod.json", VersionKey = "info.version" };

        Assert.Null(ModScanner.SetVersion(Mod(Path.Combine(_repo.Root, "Mod")), cfg, "1.0.0"));

        Assert.Equal("1.0.0", ModScanner.ReadMeta(file, "info.version"));
        Assert.Equal("Ünïcode", ModScanner.ReadMeta(file, "info.title"));
        Assert.Equal("1", ModScanner.ReadMeta(file, "other"));
    }

    [Theory]
    [InlineData("{ // a comment\n \"version\": \"1.0\" }")] // would lose the comment
    [InlineData("{ \"version\": 1.0 }")]                    // a number, not a string
    [InlineData("{ \"name\": \"x\" }")]                     // no version at all
    public void Json_the_app_cannot_safely_write_is_left_alone(string json)
    {
        var file = _repo.Write("Mod/mod.json", json);
        var cfg = new ProjectConfig { ModMarker = "mod.json", VersionKey = "version" };

        Assert.NotNull(ModScanner.SetVersion(Mod(Path.Combine(_repo.Root, "Mod")), cfg, "2.0"));
        Assert.Equal(json, File.ReadAllText(file));
    }

    // ------------------------------------------------------------ the "version" command

    static async Task<(int code, string text)> Run(string repo, params string[] args)
    {
        var o = Cli.Parse(args);
        var sw = new StringWriter();
        var code = await Cli.RunAsync(o, repo, "steamcmd", "modder", sw, CancellationToken.None, Path.Combine(repo, "uploaded.json"));
        return (code, sw.ToString());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("version", "Alpha")]
    [InlineData("upload", "Alpha", "--note", "x")]
    public async Task Version_command_needs_mods_and_a_version(params string[] args)
    {
        _repo.AddMod("Alpha");
        var (code, text) = await Run(_repo.Root, args);
        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Usage:", text);
    }

    [Fact]
    public async Task Version_command_raises_the_version_and_writes_the_changelog_entry()
    {
        var dir = _repo.AddMod("Alpha");
        var (code, text) = await Run(_repo.Root, "version", "Alpha", "minor", "--note", "- Added a thing");

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Alpha: version 1.2.3 -> 1.3.0.", text);
        Assert.Contains("Added the 1.3.0 entry", text);
        var m = Assert.Single(ModScanner.Scan(_repo.Root, ProjectConfig.Load(_repo.Root)));
        Assert.Equal("1.3.0", m.Version);
        Assert.True(m.Ready, m.StatusText);
        Assert.StartsWith("1.3.0\n- Added a thing", File.ReadAllText(Path.Combine(dir, "About", "changelog.txt")));
    }

    [Fact]
    public async Task Version_command_without_a_note_says_the_entry_is_missing()
    {
        _repo.AddMod("Alpha");
        _repo.AddMod("Beta");
        var (code, text) = await Run(_repo.Root, "version", "Alpha", "Beta", "1.5.0");

        Assert.Equal(Cli.ExitOk, code);
        Assert.Contains("Alpha: version 1.2.3 -> 1.5.0.", text);
        Assert.Contains("Beta: version 1.2.3 -> 1.5.0.", text);
        Assert.Contains("has no 1.5.0 entry yet", text);
    }

    [Fact]
    public async Task Version_command_with_an_unknown_mod_changes_nothing()
    {
        _repo.AddMod("Alpha");
        var (code, text) = await Run(_repo.Root, "version", "Alpha", "Nope", "patch");

        Assert.Equal(Cli.ExitUsage, code);
        Assert.Contains("Nope: no such mod", text);
        Assert.Equal("1.2.3", Assert.Single(ModScanner.Scan(_repo.Root, ProjectConfig.Load(_repo.Root))).Version);
    }
}
