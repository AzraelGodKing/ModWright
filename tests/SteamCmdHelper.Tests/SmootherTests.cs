using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public sealed class SmootherTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_smoother_" + Guid.NewGuid().ToString("N"));

    public SmootherTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [SupportedOSPlatform("windows6.1")]
    string Picture(string name, int width, int height)
    {
        var path = Path.Combine(_root, name);
        using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void A_big_preview_is_kept_no_bigger_than_the_panels_draw_it()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        using var img = PreviewCache.Read(Picture("Preview.png", 1920, 1080));
        Assert.NotNull(img);
        Assert.Equal(PreviewCache.MaxSide, img!.Width);
        Assert.Equal(450, img.Height); // same shape
    }

    [Fact]
    public void A_small_preview_keeps_its_size()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        using var img = PreviewCache.Read(Picture("Small.png", 320, 180));
        Assert.Equal(new Size(320, 180), img!.Size);
    }

    [Fact]
    public void A_file_that_is_not_an_image_gives_no_preview()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var path = Path.Combine(_root, "Preview.png");
        File.WriteAllText(path, "not a picture");
        Assert.Null(PreviewCache.Read(path));
        Assert.Null(PreviewCache.Read(Path.Combine(_root, "missing.png")));
    }

    [Fact]
    public async Task A_preview_is_read_once_and_then_comes_from_the_cache()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var path = Picture("Preview.png", 640, 360);
        using var cache = new PreviewCache();
        Assert.False(cache.TryGet(path, out _));
        var img = await cache.LoadAsync(path, () => null);
        Assert.True(cache.TryGet(path, out var again));
        Assert.Same(img, again);
    }

    [Fact]
    public async Task The_cache_keeps_only_the_newest_previews_and_never_the_one_on_screen()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        using var cache = new PreviewCache(keep: 2);
        var first = Picture("a.png", 64, 64);
        var shown = (await cache.LoadAsync(first, () => null))!;
        await cache.LoadAsync(Picture("b.png", 64, 64), () => shown);
        await cache.LoadAsync(Picture("c.png", 64, 64), () => shown);
        Assert.False(cache.TryGet(first, out _)); // made room for c
        Assert.Equal(64, shown.Width); // still drawable: it was on screen, so it was not disposed
    }

    [Fact]
    public void The_side_opened_last_survives_a_save_and_load_round_trip()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings { OpenOnDropPod = true });
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!.OpenOnDropPod);
        Assert.False(new AppSettings().OpenOnDropPod); // a fresh install opens on The Launch Pad
    }
}
