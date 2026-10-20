using System.Xml.Linq;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>"New mod...": the folder it makes has to pass the app's own checks straight away.</summary>
public sealed class NewModTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld(c => c.UseGitArchive = false);

    public void Dispose() => _repo.Dispose();

    NewModOptions Options(string name = "Better Storage", bool csharp = false) => new()
    {
        Name = name, Folder = NewMod.FolderFor(name), Author = "Azrael God", Description = "Stores <more> & better.",
        Versions = new[] { "1.5", "1.6" }, CSharp = csharp,
    };

    [Fact]
    public void A_new_RimWorld_mod_is_ready_to_upload()
    {
        var dir = NewMod.Create(_repo.Root, _cfg, Options());
        Assert.Equal(Path.Combine(_repo.Root, "BetterStorage"), dir);
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, _cfg));
        Assert.Equal("Better Storage", mod.Name);
        Assert.Equal("1.0.0", mod.Version);
        Assert.True(mod.Ready, string.Join("; ", mod.Problems));
        Assert.Empty(RimWorldLint.Check(dir).Warnings);
        Assert.True(mod.PreviewOk);

        var about = XDocument.Load(Path.Combine(dir, "About", "About.xml")).Root!;
        Assert.Equal("AzraelGod.BetterStorage", about.Element("packageId")!.Value);
        Assert.Equal("Stores <more> & better.", about.Element("description")!.Value);
        Assert.Equal(new[] { "1.5", "1.6" }, about.Element("supportedVersions")!.Elements("li").Select(e => e.Value));
        Assert.Null(about.Element("modDependencies"));
        Assert.StartsWith("1.0.0", File.ReadAllText(Path.Combine(dir, "About", "changelog.txt")));
    }

    [Fact]
    public void The_CSharp_option_adds_Harmony_and_a_project_that_builds_into_the_newest_version()
    {
        var dir = NewMod.Create(_repo.Root, _cfg, Options(csharp: true));
        var about = XDocument.Load(Path.Combine(dir, "About", "About.xml")).Root!;
        Assert.Equal("brrainz.harmony", about.Element("modDependencies")!.Element("li")!.Element("packageId")!.Value);
        Assert.Contains(NewMod.HarmonyId, about.Element("modDependencies")!.ToString());
        var csproj = File.ReadAllText(Path.Combine(dir, "Source", "BetterStorage", "BetterStorage.csproj"));
        Assert.Contains("<OutputPath>../../1.6/Assemblies/</OutputPath>", csproj);
        Assert.Contains("Krafs.Rimworld.Ref\" Version=\"1.6.*\"", csproj);
        Assert.Contains("new Harmony(\"azraelgod.betterstorage\")", File.ReadAllText(Path.Combine(dir, "Source", "BetterStorage", "ModStartup.cs")));
        Assert.True(File.Exists(Path.Combine(dir, ".gitignore")));
        // Source is left out of the package and its project is not XML RimWorld reads.
        Assert.True(Assert.Single(ModScanner.Scan(_repo.Root, _cfg)).Ready);
    }

    [Fact]
    public void Bad_options_are_refused()
    {
        _repo.AddMod("Taken");
        Assert.NotNull(NewMod.Validate(_repo.Root, Options().Also(o => o.Name = " ")));
        Assert.Contains("already", NewMod.Validate(_repo.Root, Options("Taken"))!);
        Assert.NotNull(NewMod.Validate(_repo.Root, Options().Also(o => o.Versions = new[] { "one" })));
        Assert.NotNull(NewMod.Validate(_repo.Root, Options().Also(o => o.Folder = "a/b")));
        Assert.Throws<InvalidOperationException>(() => NewMod.Create(_repo.Root, _cfg, Options("Taken")));
    }

    [Fact]
    public void The_default_version_is_the_newest_any_mod_in_the_folder_supports()
    {
        Assert.Equal("1.6", NewMod.NewestVersion(_repo.Root));
        _repo.AddMod("Old", supportedVersions: new[] { "1.4" });
        Assert.Equal("1.4", NewMod.NewestVersion(_repo.Root));
        _repo.AddMod("New", supportedVersions: new[] { "1.5", "1.10" });
        Assert.Equal("1.10", NewMod.NewestVersion(_repo.Root));
    }

    [Fact]
    public void PackageId_and_folder_keep_letters_and_digits()
    {
        Assert.Equal("BetterStorage2", NewMod.FolderFor("Better Storage 2!"));
        Assert.Equal("Me.Mod", NewMod.PackageId("", "Mod"));
    }

    [Fact]
    public void The_placeholder_preview_is_a_real_png()
    {
        var png = NewMod.Png(640, 360, 1, 2, 3);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        Assert.Equal(640, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
        Assert.Equal(360, (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
        Assert.True(png.Length < 10_000);
    }

    [Fact]
    public void Other_layouts_get_the_files_their_layout_names()
    {
        var cfg = ProjectConfig.GetPreset("Generic");
        cfg.ModMarker = "mod.json";
        var dir = NewMod.Create(_repo.Root, cfg, new NewModOptions { Name = "Thing", Folder = "Thing" });
        Assert.True(File.Exists(Path.Combine(dir, cfg.SteamDescriptionFile)));
        Assert.True(File.Exists(Path.Combine(dir, cfg.Changelog)));
        Assert.False(Directory.Exists(Path.Combine(dir, "About")));
        Assert.Equal("mod.json", NewMod.MissingMarker(dir, cfg));
    }

    [Fact]
    public void A_Paradox_mod_gets_a_descriptor_the_scan_reads()
    {
        var cfg = ProjectConfig.GetPreset(ProjectConfig.ParadoxName);
        var dir = NewMod.Create(_repo.Root, cfg, new NewModOptions { Name = "Big \"Map\"", Folder = "BigMap" });
        Assert.Null(NewMod.MissingMarker(dir, cfg));
        var mod = Assert.Single(ModScanner.Scan(_repo.Root, cfg));
        Assert.Equal("Big 'Map'", mod.Name);
        Assert.Equal("1.0.0", mod.Version);
    }
}

static class OptionsExtensions
{
    public static NewModOptions Also(this NewModOptions o, Action<NewModOptions> change) { change(o); return o; }
}
