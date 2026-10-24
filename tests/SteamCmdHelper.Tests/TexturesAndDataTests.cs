using SteamCmdHelper.DropPod;
using Xunit;

namespace SteamCmdHelper.Tests;

public sealed class DdsEncoderTests
{
    static byte[] Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a; }
        return px;
    }

    [Fact]
    public void Opaque_textures_become_dxt1_with_every_mipmap()
    {
        var dds = DdsEncoder.Encode(Solid(8, 4, 255, 0, 0, 255), 8, 4);
        Assert.Equal("DDS ", System.Text.Encoding.ASCII.GetString(dds, 0, 4));
        Assert.Equal(4u, BitConverter.ToUInt32(dds, 12)); // height
        Assert.Equal(8u, BitConverter.ToUInt32(dds, 16)); // width
        Assert.Equal(4u, BitConverter.ToUInt32(dds, 28)); // 8x4, 4x2, 2x1, 1x1
        Assert.Equal("DXT1", System.Text.Encoding.ASCII.GetString(dds, 84, 4));
        // 128 header bytes, then 2 + 1 + 1 + 1 blocks of 8 bytes.
        Assert.Equal(128 + 5 * 8, dds.Length);
        // A solid red block: both ends pure red in RGB565.
        Assert.Equal(0xF800, BitConverter.ToUInt16(dds, 128));
    }

    [Fact]
    public void Transparent_textures_become_dxt5()
    {
        var px = Solid(4, 4, 10, 20, 30, 255);
        px[3] = 0;
        var dds = DdsEncoder.Encode(px, 4, 4, flip: false);
        Assert.Equal("DXT5", System.Text.Encoding.ASCII.GetString(dds, 84, 4));
        Assert.Equal(128 + 3 * 16, dds.Length); // 4x4, 2x2, 1x1
        Assert.Equal(255, dds[128]); // alpha ends: 255 and 0
        Assert.Equal(0, dds[129]);
        Assert.Equal(1, dds[130] & 7); // pixel 0 (alpha 0) uses the second end
    }

    [Fact]
    public void Flipping_puts_the_bottom_row_first()
    {
        var px = Solid(4, 8, 0, 0, 255, 255);
        for (var i = 0; i < 4 * 4 * 4; i += 4) { px[i] = 255; px[i + 2] = 0; } // top half red
        var dds = DdsEncoder.Encode(px, 4, 8);
        Assert.Equal(0x001F, BitConverter.ToUInt16(dds, 128)); // first block: the blue bottom half
        Assert.Equal(0xF800, BitConverter.ToUInt16(dds, 136));
    }

    [Theory]
    [InlineData(4, 4, true)]
    [InlineData(64, 128, true)]
    [InlineData(6, 4, false)]
    [InlineData(0, 4, false)]
    public void Only_sides_in_multiples_of_4_can_be_encoded(int w, int h, bool ok) => Assert.Equal(ok, DdsEncoder.CanEncode(w, h));
}

public sealed class TextureOptimizerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "tex_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    static (byte[], int, int) Fake(string path) => path.Contains("odd") ? (new byte[6 * 4 * 4], 6, 4) : (new byte[4 * 4 * 4], 4, 4);

    [Fact]
    public void Converts_pngs_skips_shipped_dds_and_undoes_its_own()
    {
        var mod = Path.Combine(_root, "Mod");
        foreach (var rel in new[] { "Textures/A.png", "1.5/Textures/Things/B.png", "Textures/Shipped.png", "Textures/odd.png", "Source/Textures/Art.png" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(mod, rel))!);
            File.WriteAllText(Path.Combine(mod, rel), "png");
        }
        File.WriteAllText(Path.Combine(mod, "Textures", "Shipped.dds"), "theirs");
        var m = new ModManifest { Folder = mod, Id = "a.mod", Source = ModSource.Workshop };
        var tex = new TextureOptimizer(Path.Combine(_root, "records"));
        Assert.Equal(3, tex.ToDo(m).Count);

        var r = tex.Optimize(m, Fake, CancellationToken.None);
        Assert.Equal(2, r.Converted);
        Assert.Equal(1, r.Skipped);
        Assert.True(File.Exists(Path.Combine(mod, "Textures", "A.dds")));
        Assert.True(File.Exists(Path.Combine(mod, "1.5", "Textures", "Things", "B.dds")));
        Assert.Equal(new[] { "1.5/Textures/Things/B.dds", "Textures/A.dds" }, tex.Made(m));
        Assert.Equal(new[] { Path.Combine(mod, "Textures", "odd.png") }, tex.ToDo(m));

        // A newer PNG (a mod update) needs its DDS made again.
        File.SetLastWriteTimeUtc(Path.Combine(mod, "Textures", "A.png"), DateTime.UtcNow.AddMinutes(5));
        Assert.Contains(Path.Combine(mod, "Textures", "A.png"), tex.ToDo(m));

        Assert.Equal(2, tex.Undo(m));
        Assert.False(File.Exists(Path.Combine(mod, "Textures", "A.dds")));
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(mod, "Textures", "Shipped.dds")));
        Assert.Empty(tex.Made(m));
    }
}

public sealed class PackageSizeTests
{
    static PackageFile F(string path, long bytes) => new(path, bytes, "");

    [Fact]
    public void Finds_what_players_rarely_need_and_where_the_bytes_are()
    {
        var files = new[] { F("Textures/a.png", 900), F("Textures/a.psd", 5000), F("Source/Mod.cs", 10), F("Source/Mod.csproj", 5), F("Assemblies/Mod.pdb", 50), F("About/About.xml", 1), F("Thumbs.db", 2) };
        var rare = PackageCheck.RarelyNeeded(files);
        Assert.Equal(new[] { "*.psd", "*.pdb", "Source", "Thumbs.db" }, rare.Select(r => r.Pattern));
        Assert.Equal((2, 15L), (rare[2].Files, rare[2].Bytes));
        var top = PackageCheck.ByTopFolder(files);
        Assert.Equal(("Textures/", 5900L), top[0]);
    }

    [Fact]
    public void Prune_removes_matching_files_and_folders()
    {
        var stage = Path.Combine(Path.GetTempPath(), "prune_" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var rel in new[] { "Textures/a.png", "Textures/a.psd", "Source/x.cs", "About/About.xml" })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(stage, rel))!);
                File.WriteAllText(Path.Combine(stage, rel), "x");
            }
            Publisher.Prune(stage, new[] { "*.psd", "Source" });
            Assert.True(File.Exists(Path.Combine(stage, "Textures", "a.png")));
            Assert.False(File.Exists(Path.Combine(stage, "Textures", "a.psd")));
            Assert.False(Directory.Exists(Path.Combine(stage, "Source")));
        }
        finally { try { Directory.Delete(stage, true); } catch { } }
    }

    [Fact]
    public void A_built_in_layout_keeps_its_leave_out_list()
    {
        var repo = Path.Combine(Path.GetTempPath(), "leave_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repo);
        try
        {
            var cfg = ProjectConfig.GetPreset("RimWorld");
            cfg.LeaveOut = new[] { "*.psd" };
            cfg.Save(repo);
            var again = ProjectConfig.Load(repo);
            Assert.Equal(new[] { "*.psd" }, again.LeaveOut);
            Assert.Equal("RimWorld", again.PresetName());
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }
}

public sealed class SettingsBackupsTests : IDisposable
{
    readonly string _data = Path.Combine(Path.GetTempPath(), "sb_" + Guid.NewGuid().ToString("N"));
    string Dir => Path.Combine(_data, "backups", "daily");

    public void Dispose()
    {
        try { Directory.Delete(_data, true); } catch { }
    }

    [Fact]
    public void Backs_up_once_a_day_with_the_drop_pod_records_and_keeps_14()
    {
        Assert.Null(SettingsBackups.MakeIfDue(_data, Dir)); // no settings yet
        Directory.CreateDirectory(Path.Combine(_data, "droppod", "modlists"));
        Directory.CreateDirectory(Path.Combine(_data, "droppod", "instances", "X"));
        File.WriteAllText(Path.Combine(_data, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_data, "droppod", "modlists", "Heavy.json"), "{}");
        File.WriteAllText(Path.Combine(_data, "droppod", "notes.json"), "{}");
        File.WriteAllText(Path.Combine(_data, "droppod", "instances", "X", "save.rws"), "big");
        var start = new DateTime(2026, 10, 1, 9, 0, 0);
        var first = SettingsBackups.MakeIfDue(_data, Dir, start)!;
        using (var zip = System.IO.Compression.ZipFile.OpenRead(first))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("settings.json", names);
            Assert.Contains("droppod/modlists/Heavy.json", names);
            Assert.Contains("droppod/notes.json", names);
            Assert.DoesNotContain(names, n => n.Contains("instances"));
        }
        Assert.Null(SettingsBackups.MakeIfDue(_data, Dir, start.AddHours(5)));
        for (var d = 1; d <= 20; d++) Assert.NotNull(SettingsBackups.MakeIfDue(_data, Dir, start.AddDays(d)));
        var list = SettingsBackups.List(Dir);
        Assert.Equal(14, list.Count);
        Assert.Equal(start.AddDays(20), list[0].Made);
    }

    [Fact]
    public void Restoring_puts_the_drop_pod_records_back_and_keeps_the_current_ones()
    {
        Directory.CreateDirectory(Path.Combine(_data, "droppod", "modlists"));
        File.WriteAllText(Path.Combine(_data, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_data, "droppod", "modlists", "Heavy.json"), "old");
        var backup = SettingsBackups.MakeIfDue(_data, Dir, new DateTime(2026, 10, 1))!;
        File.WriteAllText(Path.Combine(_data, "droppod", "modlists", "Heavy.json"), "new");
        var (settings, error) = SettingsTransfer.Read(backup);
        Assert.Null(error);
        var before = SettingsTransfer.Import(backup, settings!, _data, new DateTime(2026, 10, 2));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_data, "droppod", "modlists", "Heavy.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(before, "droppod", "modlists", "Heavy.json")));
    }
}
