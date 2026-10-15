using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The Paradox layout: descriptor.mod holds the name, version and the Workshop id (remote_file_id).</summary>
public sealed class ParadoxLayoutTests : IDisposable
{
    readonly TempRepo _repo = new();
    public void Dispose() => _repo.Dispose();

    static ProjectConfig Paradox(string appId = "281990")
    {
        var c = ProjectConfig.GetPreset(ProjectConfig.ParadoxName);
        c.AppId = appId;
        c.UseGitArchive = false;
        return c;
    }

    [Fact]
    public void Descriptor_gives_name_version_and_id()
    {
        _repo.Write("Better Ships/descriptor.mod", "version=\"2.3.1\"\ntags={\n\t\"Gameplay\"\n}\nname=\"Better Ships\"\nsupported_version=\"3.12.*\"\nremote_file_id=\"2911223344\"\n");
        _repo.Write("Better Ships/thumbnail.png", "png");

        var m = Assert.Single(ModScanner.Scan(_repo.Root, Paradox()));

        Assert.Equal("Better Ships", m.Name);
        Assert.Equal("2.3.1", m.Version); // not supported_version
        Assert.Equal("2911223344", m.WorkshopId);
        Assert.True(m.Ready, m.StatusText);
    }

    [Theory]
    [InlineData("name=\"New Mod\"\nversion=\"1.0\"\n")]
    [InlineData("name=\"New Mod\"\nversion=\"1.0\"\nremote_file_id=\"0\"\n")]
    public void Descriptor_without_an_id_is_a_new_item(string descriptor)
    {
        _repo.Write("New/descriptor.mod", descriptor);
        _repo.Write("New/thumbnail.png", "png");

        var m = Assert.Single(ModScanner.Scan(_repo.Root, Paradox()));

        Assert.Null(m.WorkshopId);
        Assert.True(m.Ready, m.StatusText);
    }

    [Fact]
    public void Id_that_is_not_a_number_is_a_problem()
    {
        _repo.Write("Bad/descriptor.mod", "name=\"Bad\"\nremote_file_id=\"abc\"\n");
        var m = Assert.Single(ModScanner.Scan(_repo.Root, Paradox()));
        Assert.Contains(m.Problems, p => p.Contains("remote_file_id in descriptor.mod"));
    }

    [Theory]
    [InlineData("name=\"A\"\nremote_file_id=\"0\"\nversion=\"1\"\n", "name=\"A\"\nremote_file_id=\"123\"\nversion=\"1\"\n")]
    [InlineData("name=\"A\"\n", "name=\"A\"\nremote_file_id=\"123\"\n")]
    [InlineData("name=\"A\"", "name=\"A\"\nremote_file_id=\"123\"\n")]
    [InlineData("name=\"A\"\r\n\tremote_file_id = \"9\"\r\n", "name=\"A\"\r\n\tremote_file_id=\"123\"\r\n")]
    public void New_id_is_written_into_the_descriptor(string before, string after)
    {
        Assert.Equal(after, ModScanner.WriteKey(before, "remote_file_id", "123"));
    }

    [Fact]
    public void Folders_with_descriptor_files_are_detected_as_paradox()
    {
        _repo.Write("Some Mod/descriptor.mod", "name=\"Some Mod\"\n");
        var c = ProjectConfig.Load(_repo.Root);
        Assert.Equal(ProjectConfig.ParadoxName, c.PresetName());
        Assert.True(c.Experimental);
        Assert.False(c.AppIdLocked); // the game decides the app id
    }

    [Fact]
    public void Saved_paradox_layout_keeps_the_typed_app_id_and_stays_locked()
    {
        var c = Paradox("394360");
        c.Save(_repo.Root);
        var back = ProjectConfig.Load(_repo.Root);
        Assert.Equal(ProjectConfig.ParadoxName, back.PresetName());
        Assert.Equal("394360", back.AppId);
        Assert.Equal("remote_file_id", back.WorkshopIdKey);
    }
}
