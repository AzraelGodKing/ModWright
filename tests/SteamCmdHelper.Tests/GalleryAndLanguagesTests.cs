using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>Gallery images and videos, and descriptions in other languages, sent through the Steam client.</summary>
public sealed class GalleryAndLanguagesTests : IDisposable
{
    readonly TempRepo _repo = new();
    readonly ProjectConfig _cfg = TestConfig.RimWorld();
    string Rec(string name) => Path.Combine(_repo.Root, "_store", name);

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void The_gallery_plan_removes_replaces_and_adds()
    {
        var current = new List<PreviewOnSteam>
        {
            new(0, false, "shot1.png"),
            new(1, false, "old.png"),
            new(2, true, "dQw4w9WgXcQ"),
            new(3, false, "shot2.png"),
            new(4, true, "aaaaaaaaaaa"),
        };
        var wanted = new GalleryRequest
        {
            Images = { new() { Path = "/g/shot1.png", Sent = true }, new() { Path = "/g/shot2.png", Sent = false }, new() { Path = "/g/new.jpg" } },
            Videos = new[] { "dQw4w9WgXcQ", "bbbbbbbbbbb" },
        };

        var (remove, replace, addImages, addVideos) = WorkshopExtras.GalleryPlan(current, wanted);

        Assert.Equal(new uint[] { 4, 1 }, remove); // highest first
        Assert.Equal(new[] { (3u, "/g/shot2.png") }, replace);
        Assert.Equal(new[] { "/g/new.jpg" }, addImages);
        Assert.Equal(new[] { "bbbbbbbbbbb" }, addVideos);
    }

    [Fact]
    public void Video_links_in_any_youtube_form_become_ids()
    {
        var ids = WorkshopExtras.VideoIds("""
            https://www.youtube.com/watch?v=dQw4w9WgXcQ
            https://youtu.be/aaaaaaaaaaa?t=10   # trailer
            # https://youtu.be/ccccccccccc
            youtube.com/shorts/bbbbbbbbbbb
            dQw4w9WgXcQ
            not a link
            """);
        Assert.Equal(new[] { "dQw4w9WgXcQ", "aaaaaaaaaaa", "bbbbbbbbbbb" }, ids);
    }

    [Fact]
    public void Translations_are_found_next_to_the_description_file_and_sent_once()
    {
        var dir = _repo.AddMod("Alpha", workshopId: "111");
        _repo.Write("Alpha/About/steam_description.txt", "English");
        _repo.Write("Alpha/About/steam_description.de.txt", "Title: Bessere Lager\r\nDeutsch");
        _repo.Write("Alpha/About/steam_description.schinese.txt", "中文");
        _repo.Write("Alpha/About/steam_description.klingon.txt", "not a Steam language");

        var req = WorkshopExtras.For("294100", "111", _cfg, dir, Array.Empty<string>(), true, Rec("req.json"), Rec("sent.json"))!;

        Assert.Equal(new[] { "german", "schinese" }, req.Languages!.Select(l => l.Language));
        Assert.Equal(("Bessere Lager", "Deutsch"), (req.Languages![0].Title, req.Languages[0].Description));
        Assert.Null(req.Languages[1].Title);

        WorkshopExtras.RememberSent(req, Rec("sent.json"));
        Assert.Null(WorkshopExtras.For("294100", "111", _cfg, dir, Array.Empty<string>(), true, Rec("req.json"), Rec("sent.json")));

        _repo.Write("Alpha/About/steam_description.de.txt", "Title: Bessere Lager\nNeu");
        var again = WorkshopExtras.For("294100", "111", _cfg, dir, Array.Empty<string>(), true, Rec("req.json"), Rec("sent.json"))!;
        Assert.Equal("german", Assert.Single(again.Languages!).Language);
    }

    [Fact]
    public void Gallery_images_remember_what_was_sent()
    {
        var dir = _repo.AddMod("Alpha", workshopId: "111");
        _repo.Write("Alpha/Gallery/a.png", "image a");
        _repo.Write("Alpha/Gallery/notes.txt", "not an image");
        _repo.Write("Alpha/Gallery/videos.txt", "https://youtu.be/dQw4w9WgXcQ");

        var req = WorkshopExtras.For("294100", "111", _cfg, dir, Array.Empty<string>(), true, Rec("req.json"), Rec("sent.json"))!;
        var img = Assert.Single(req.Gallery!.Images);
        Assert.False(img.Sent);
        Assert.Equal(new[] { "dQw4w9WgXcQ" }, req.Gallery.Videos);

        WorkshopExtras.RememberSent(req, Rec("sent.json"));
        var later = WorkshopExtras.For("294100", "111", _cfg, dir, Array.Empty<string>(), true, Rec("req.json"), Rec("sent.json"))!;
        Assert.True(later.Gallery!.Images.Single().Sent); // still compared with Steam, but not uploaded again

        _repo.Write("Alpha/Gallery/a.png", "image a, edited");
        Assert.False(WorkshopExtras.For("294100", "111", _cfg, dir, Array.Empty<string>(), true, Rec("req.json"), Rec("sent.json"))!.Gallery!.Images.Single().Sent);
        Assert.Null(WorkshopExtras.For("294100", "111", _cfg, dir, Array.Empty<string>(), false, Rec("req.json"), Rec("sent.json"))); // turned off
    }

    [Fact]
    public async Task The_gallery_folder_is_never_packaged()
    {
        _repo.AddMod("Alpha", workshopId: "111");
        _repo.Write("Alpha/Gallery/a.png", "image a");
        var mod = ModScanner.Scan(_repo.Root, _cfg).Single();
        var pub = new Publisher(_repo.Root, _cfg, "steamcmd", "modder", Path.Combine(_repo.Root, "_tmp"));
        PackageReport? report = null;
        pub.Packaged += (_, r) => report = r;
        await pub.RunAsync(mod, false, CancellationToken.None);
        Assert.NotNull(report);
        Assert.DoesNotContain(report!.Files, f => f.Path.StartsWith("Gallery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Files, f => f.Path.Replace('\\', '/') == "About/About.xml");
    }
}
