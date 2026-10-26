using System.Diagnostics;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>What changed since the last upload, size jumps, paths that break for players, and files a package must have.</summary>
public sealed class PackageSafetyTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_safety_" + Guid.NewGuid().ToString("N"));
    readonly string _repo, _temp, _modDir, _manifests;

    public PackageSafetyTests()
    {
        _repo = Path.Combine(_root, "repo");
        _temp = Path.Combine(_root, "temp");
        _manifests = Path.Combine(_root, "manifests");
        _modDir = Path.Combine(_repo, "MyMod");
        Directory.CreateDirectory(_temp);
        Directory.CreateDirectory(Path.Combine(_modDir, "About"));
        File.WriteAllText(Path.Combine(_modDir, "About", "About.xml"), "<ModMetaData><name>My Mod</name><modVersion>1.0.0</modVersion></ModMetaData>");
        File.WriteAllText(Path.Combine(_modDir, "About", "changelog.txt"), "1.0.0\nFirst.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    static bool CanFakeSteamCmd => !OperatingSystem.IsWindows();

    static ProjectConfig CopyConfig() => TestConfig.RimWorld(c => { c.UseGitArchive = false; c.BuiltFiles = Array.Empty<string>(); });

    ModInfo Mod() => new() { Dir = _modDir, Folder = "MyMod", Name = "My Mod", Version = "1.0.0", WorkshopId = "2001112223" };

    string FakeSteamCmd()
    {
        var path = Path.Combine(_root, "steamcmd.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + TestConfig.SkipSteamFlags + "echo 'Uploading content...'\necho 'Success.'\nexit 0\n");
        Process.Start("chmod", new[] { "+x", path })!.WaitForExit();
        return path;
    }

    // ------------------------------------------------------------ paths

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("Com1.dat")]
    [InlineData("what?.png")]
    [InlineData("a:b")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void Names_Windows_refuses_are_bad(string name) => Assert.True(PackageCheck.BadName(name));

    [Theory]
    [InlineData("Console.xml")]
    [InlineData("nullable.dll")]
    [InlineData("About.xml")]
    [InlineData("Ünïcode.txt")]
    public void Ordinary_names_are_fine(string name) => Assert.False(PackageCheck.BadName(name));

    [Fact]
    public void Path_problems_are_warned_about()
    {
        var files = new[]
        {
            new PackageFile("Textures/icon.png", 10),
            new PackageFile("Textures/Icon.png", 10),
            new PackageFile("Sounds/aux.ogg", 10),
            new PackageFile("Deep/" + new string('x', PackageCheck.LongPath) + ".xml", 10),
        };
        var w = PackageCheck.Warn(files);
        Assert.Contains(w, x => x.Contains("differ only in capital letters") && x.Contains("Textures/Icon.png"));
        Assert.Contains(w, x => x.Contains("Windows cannot use") && x.Contains("aux.ogg"));
        Assert.Contains(w, x => x.Contains("260-character"));
    }

    // ------------------------------------------------------------ required files

    [Fact]
    public void Required_files_match_paths_and_globs_case_insensitively()
    {
        var files = new[] { new PackageFile("About/About.xml", 1), new PackageFile("Assemblies/Mod.dll", 1) };
        Assert.Empty(PackageCheck.MissingRequired(files, new[] { "about/about.xml", "Assemblies/*.dll" }));
        Assert.Equal(new[] { "Defs/*.xml" }, PackageCheck.MissingRequired(files, new[] { "Defs/*.xml" }));
        // A glob does not reach into sub-folders.
        Assert.Equal(new[] { "*.dll" }, PackageCheck.MissingRequired(files, new[] { "*.dll" }));
    }

    [Fact]
    public void Layouts_require_their_marker_file()
    {
        Assert.Contains("About/About.xml", TestConfig.RimWorld().RequiredInPackage());
        Assert.Empty(new ProjectConfig().RequiredInPackage());
    }

    [Fact]
    public async Task Package_without_a_required_file_is_refused()
    {
        var cfg = CopyConfig();
        cfg.RequiredFiles = new[] { "Assemblies/*.dll" };
        var pub = new Publisher(_repo, cfg, "steamcmd", "modder", _temp);
        var r = await pub.RunAsync(Mod(), upload: false, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("Assemblies/*.dll", r.Message);
        Assert.Contains("Exclude", r.Message);
    }

    // ------------------------------------------------------------ comparing with the last upload

    static PackageManifest Last(params PackageFile[] files) => new(DateTimeOffset.UtcNow.AddDays(-2), "0.9.0", files.ToList());

    [Fact]
    public void Diff_finds_added_changed_and_removed_files()
    {
        var last = Last(new PackageFile("a.txt", 1, "AA"), new PackageFile("b.txt", 2, "BB"), new PackageFile("c.txt", 3, "CC"));
        var d = PackageDiffs.Compare(last, new[] { new PackageFile("a.txt", 1, "AA"), new PackageFile("b.txt", 2, "B2"), new PackageFile("d.txt", 4, "DD") });
        Assert.Equal(new[] { "d.txt" }, d.Added);
        Assert.Equal(new[] { "b.txt" }, d.Changed);
        Assert.Equal(new[] { "c.txt" }, d.Removed);
        Assert.False(d.Identical);
        Assert.Equal("1 added, 1 changed, 1 removed", d.Summary);
        Assert.Contains("v0.9.0", d.Since);
    }

    [Fact]
    public void Same_files_are_identical()
    {
        var d = PackageDiffs.Compare(Last(new PackageFile("a.txt", 1, "AA")), new[] { new PackageFile("a.txt", 1, "aa") });
        Assert.True(d.Identical);
        Assert.Equal("no file changed", d.Summary);
    }

    [Fact]
    public void A_much_smaller_package_is_warned_about()
    {
        var last = Last(new PackageFile("big.bin", 10 * 1024 * 1024, "X"));
        Assert.Contains(PackageDiffs.Compare(last, new[] { new PackageFile("big.bin", 3 * 1024 * 1024, "Y") }).Warnings(), w => w.Contains("check that nothing is missing"));
        // Small packages swing a lot in ratio without meaning anything.
        Assert.Empty(PackageDiffs.Compare(Last(new PackageFile("a", 1000, "X")), new[] { new PackageFile("a", 100, "Y") }).Warnings());
    }

    [Fact]
    public void Losing_many_files_is_warned_about()
    {
        var last = Last(Enumerable.Range(0, 10).Select(i => new PackageFile($"f{i}", 1, "H")).ToArray());
        var now = Enumerable.Range(0, 4).Select(i => new PackageFile($"f{i}", 1, "H")).ToList();
        Assert.Contains(PackageDiffs.Compare(last, now).Warnings(), w => w.Contains("lost 6 of its 10 files"));
    }

    [Fact]
    public void Manifests_survive_a_round_trip()
    {
        PackageDiffs.Save(_manifests, "123", Last(new PackageFile("a.txt", 1, "AA")));
        var back = PackageDiffs.Load(_manifests, "123");
        Assert.NotNull(back);
        Assert.Equal("AA", back!.Files[0].Hash);
        Assert.Null(PackageDiffs.Load(_manifests, "456"));
    }

    [Fact]
    public async Task Second_upload_is_compared_with_the_first_and_an_unchanged_one_can_be_skipped()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd();
        var log = new List<string>();
        var asked = 0;
        Publisher Make()
        {
            var p = new Publisher(_repo, CopyConfig(), steam, "modder", _temp)
            {
                ManifestDir = _manifests,
                UploadUnchanged = (_, d) => { asked++; Assert.True(d.Identical); return false; },
            };
            p.Log += log.Add;
            return p;
        }

        Assert.True((await Make().RunAsync(Mod(), true, CancellationToken.None)).Ok);
        Assert.Equal(0, asked);
        Assert.NotNull(PackageDiffs.Load(_manifests, "2001112223"));

        var batch = await new BatchUpload(Make(), Path.Combine(_root, "uploaded.json")).RunAsync(new[] { Mod() }, true, CancellationToken.None);
        Assert.Equal(1, asked);
        Assert.Equal(BatchStatus.Skipped, batch[0].Status);
        Assert.True(batch[0].Ok);
        Assert.Contains("1 skipped", BatchUpload.Counts(batch));

        File.WriteAllText(Path.Combine(_modDir, "About", "new.xml"), "<x/>");
        var r = await Make().RunAsync(Mod(), true, CancellationToken.None);
        Assert.True(r.Ok);
        Assert.False(r.Skipped);
        Assert.Contains(log, l => l.Contains("1 added") && l.Contains("since the last upload"));
        Assert.Contains(log, l => l == "  + About/new.xml");
    }

    [Fact]
    public async Task Without_a_question_an_unchanged_package_is_uploaded_with_a_warning()
    {
        if (!CanFakeSteamCmd) return;
        var steam = FakeSteamCmd();
        var log = new List<string>();
        var pub = new Publisher(_repo, CopyConfig(), steam, "modder", _temp) { ManifestDir = _manifests };
        pub.Log += log.Add;
        await pub.RunAsync(Mod(), true, CancellationToken.None);
        var r = await pub.RunAsync(Mod(), true, CancellationToken.None);
        Assert.True(r.Ok);
        Assert.False(r.Skipped);
        Assert.Contains(log, l => l.Contains("only the change note"));
    }
}
