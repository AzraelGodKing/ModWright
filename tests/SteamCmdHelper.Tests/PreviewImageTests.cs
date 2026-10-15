using System.Drawing;
using System.Drawing.Imaging;
using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public sealed class PreviewImageTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "steamcmd_helper_preview_" + Guid.NewGuid().ToString("N"));

    public PreviewImageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>Random pixels barely compress, so this makes a file well over 1 MB.</summary>
    string NoisyImage(string name, ImageFormat format, int width, int height)
    {
        var path = Path.Combine(_root, name);
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return path;
        var rnd = new Random(1);
        using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                bmp.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
        bmp.Save(path, format);
        return path;
    }

    [Theory]
    [InlineData("Preview.png", 1280, 720)]
    [InlineData("Preview.jpg", 2400, 1400)] // JPEG compresses even noise, so it needs more pixels to pass 1 MB
    public void Shrinks_an_oversized_preview_under_the_limit_and_keeps_the_original(string name, int width, int height)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return;
        var path = NoisyImage(name, name.EndsWith(".png") ? ImageFormat.Png : ImageFormat.Jpeg, width, height);
        var before = new FileInfo(path).Length;
        Assert.True(before > ModScanner.MaxPreviewBytes, $"test image is only {before} bytes");

        var backups = Path.Combine(_root, "backups");
        PreviewImage.Shrink(path, "My Mod", ModScanner.MaxPreviewBytes, backups);

        Assert.InRange(new FileInfo(path).Length, 1, ModScanner.MaxPreviewBytes);
        using (var img = Image.FromFile(path)) Assert.True(img.Width > 0 && img.Height > 0);
        var kept = Assert.Single(Directory.GetFiles(Path.Combine(backups, "My Mod")));
        Assert.Equal(before, new FileInfo(kept).Length);
    }
}
