using System.IO.Compression;
using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.SunHaven;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

public sealed class SunHavenTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "modwright-sh-" + Guid.NewGuid().ToString("N"));
    readonly string _game;
    readonly SunHavenModule _sh = new();

    public SunHavenTests()
    {
        _game = Path.Combine(_root, "lib", "steamapps", "common", "Sun Haven");
        Directory.CreateDirectory(Path.Combine(_game, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(_game, SunHavenModule.Exe), "");
        Plugin("plugins", "Author-ModA", "1.0.0", "Author-ModB-1.2.0", "BepInEx-BepInExPack-5.4.2100");
        Plugin("plugins-disabled", "Author-ModB", "1.2.0");
        File.WriteAllBytes(Path.Combine(_game, "BepInEx", "plugins", "Loose.dll"), new byte[] { 1, 2, 3 });
        Directory.CreateDirectory(Path.Combine(_game, "BepInEx", "plugins", "EmptyFolder"));
    }

    void Plugin(string where, string id, string version, params string[] deps)
    {
        var dir = Path.Combine(_game, "BepInEx", where, id);
        Directory.CreateDirectory(dir);
        var name = id[(id.IndexOf('-') + 1)..];
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""{ "name": "{{name}}", "version_number": "{{version}}", "website_url": "", "description": "d", "dependencies": [{{string.Join(",", deps.Select(d => $"\"{d}\""))}}] }""");
        File.WriteAllBytes(Path.Combine(dir, name + ".dll"), new byte[] { 1 });
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    GameInstall Install() => _sh.Detect(new SteamLibraries(null, new[] { Path.Combine(_root, "lib") }), null, Path.Combine(_root, "data"))!;

    [Fact]
    public void Finds_the_game_without_an_app_manifest()
    {
        var i = Install();
        Assert.NotNull(i);
        Assert.Equal(_game, i.GameFolder);
        Assert.True(SunHavenModule.HasBepInEx(i));
    }

    [Fact]
    public void Reads_plugin_folders_and_loose_dlls_and_the_active_ones()
    {
        var scan = ModLibrary.Scan(_sh, Install());
        Assert.Equal(new[] { "author-moda", "author-modb", "loose" }, scan.Mods.Select(m => m.Key).OrderBy(k => k));
        var a = scan.Mods.Single(m => m.Key == "author-moda");
        Assert.Equal("ModA", a.Name);
        Assert.Equal("1.0.0", a.ModVersion);
        Assert.Equal(new[] { "Author-ModB" }, a.Dependencies.All.Select(d => d.Id)); // BepInEx itself is left out
        Assert.Equal(new[] { "loose", "author-moda" }, scan.Active);
    }

    [Fact]
    public void An_inactive_dependency_is_a_problem_and_activating_moves_folders()
    {
        var install = Install();
        var scan = ModLibrary.Scan(_sh, install);
        var problems = Problems.Find(scan, scan.Active!.ToList(), _sh, null);
        Assert.Contains(problems, p => p.Kind == ProblemKind.MissingDependency && p.Other == "author-modb");

        _sh.WriteActiveMods(install, new[] { "author-moda", "Author-ModB" });
        Assert.True(Directory.Exists(Path.Combine(_game, "BepInEx", "plugins", "Author-ModB")));
        Assert.True(File.Exists(Path.Combine(_game, "BepInEx", "plugins-disabled", "Loose.dll")));
        Assert.Equal(new[] { "author-moda", "author-modb" }, _sh.ReadActiveMods(install));
    }

    [Fact]
    public void Has_no_load_order()
    {
        Assert.False(((IGameModule)_sh).HasLoadOrder);
        Assert.True(((IGameModule)new SteamCmdHelper.DropPod.RimWorld.RimWorldModule()).HasLoadOrder);
        Assert.Same(DropPodGames.All[1], DropPodGames.For(SunHavenModule.SteamAppId));
        Assert.Equal("RimWorld", DropPodGames.For("nope").Name);
    }

    [Fact]
    public void Junk_dll_has_no_plugin_info() => Assert.Null(SunHavenModule.PluginInfo(Path.Combine(_game, "BepInEx", "plugins", "Loose.dll")));
}

public sealed class ThunderstoreModsTests
{
    [Theory]
    [InlineData("Author-Name-1.2.3", "Author-Name")]
    [InlineData("Some_Team-Mod_Name-10.0.1", "Some_Team-Mod_Name")]
    [InlineData("Author-Name", null)]
    [InlineData("", null)]
    public void Reads_dependency_strings(string dep, string? id) => Assert.Equal(id, ThunderstoreMods.DependencyId(dep));

    [Fact]
    public void Parses_the_package_answer()
    {
        var p = ThunderstoreMods.Parse("""
            { "namespace": "Author", "name": "ModA", "package_url": "https://thunderstore.io/c/sun-haven/p/Author/ModA/",
              "latest": { "namespace": "Author", "name": "ModA", "version_number": "1.4.0", "download_url": "https://thunderstore.io/package/download/Author/ModA/1.4.0/",
                          "dependencies": ["BepInEx-BepInExPack-5.4.2100", "Author-Lib-2.0.0"] } }
            """)!;
        Assert.Equal("Author-ModA", p.Id);
        Assert.Equal("1.4.0", p.Version);
        Assert.Equal(new[] { "BepInEx-BepInExPack", "Author-Lib" }, p.Dependencies);
        Assert.Null(ThunderstoreMods.Parse("""{ "namespace": "A", "name": "B" }"""));
    }

    [Fact]
    public void Places_files_like_r2modman()
    {
        var root = Path.Combine(Path.GetTempPath(), "modwright-tsp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var unpacked = Path.Combine(root, "files");
            void Put(string rel, string text = "x") { var f = Path.Combine(unpacked, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, text); }
            Put("manifest.json", """{ "name": "ModA", "version_number": "1.0.0", "dependencies": ["Author-Lib-1.0.0"] }""");
            Put("icon.png");
            Put("BepInEx/plugins/ModA/ModA.dll");
            Put("plugins/Extra.dll");
            Put("config/moda.cfg", "new");
            Put("BepInEx/patchers/P.dll");
            Put("BepInEx/core/BepInEx.dll");
            var bepinex = Path.Combine(root, "BepInEx");
            Directory.CreateDirectory(Path.Combine(bepinex, "config"));
            File.WriteAllText(Path.Combine(bepinex, "config", "moda.cfg"), "mine");
            var staged = Path.Combine(root, "staged");

            var deps = ThunderstoreMods.Place(unpacked, staged, bepinex, "Author-ModA");

            Assert.Equal(new[] { "Author-Lib" }, deps);
            Assert.True(File.Exists(Path.Combine(staged, "ModA", "ModA.dll")));
            Assert.True(File.Exists(Path.Combine(staged, "Extra.dll")));
            Assert.True(File.Exists(Path.Combine(staged, "manifest.json")));
            Assert.Equal("mine", File.ReadAllText(Path.Combine(bepinex, "config", "moda.cfg"))); // the player's config stays
            Assert.True(File.Exists(Path.Combine(bepinex, "patchers", "Author-ModA", "P.dll")));
            Assert.False(File.Exists(Path.Combine(staged, "core", "BepInEx.dll")));
            Assert.False(Directory.Exists(Path.Combine(bepinex, "core")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Only_thunderstore_folders_are_update_candidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "modwright-tsc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = Path.Combine(root, "Author-ModA");
            Directory.CreateDirectory(a);
            File.WriteAllText(Path.Combine(a, "manifest.json"), "{}");
            var mods = new[]
            {
                new ModManifest { Folder = a, Id = "Author-ModA", ModVersion = "1.0.0" },
                new ModManifest { Folder = Path.Combine(root, "Loose.dll"), Id = "Loose", ModVersion = "1.0.0" },
                new ModManifest { Folder = Path.Combine(root, "Author-NoManifest"), Id = "Author-NoManifest", ModVersion = "1.0.0" },
            };
            Assert.Equal(new[] { "Author-ModA" }, ThunderstoreMods.Candidates(mods).Select(m => m.Id));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
