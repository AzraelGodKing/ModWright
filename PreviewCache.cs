using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace SteamCmdHelper;

/// <summary>
/// Preview images for the details panels, read on a worker thread and shrunk to the size a panel can show, so moving
/// through a long mod list never waits on the disk, and hundreds of full-size pictures are never kept in memory.
/// The files are not kept open, so the mods stay editable. Used from the UI thread only.
/// </summary>
[SupportedOSPlatform("windows6.1")]
public sealed class PreviewCache : IDisposable
{
    /// <summary>The biggest a kept copy gets: more than the panels draw, even on a wide 4K window.</summary>
    public const int MaxSide = 800;

    readonly Dictionary<string, (DateTime Stamp, Image? Image)> _images = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Task<Image?>> _reading = new(StringComparer.OrdinalIgnoreCase);
    readonly Queue<string> _order = new();
    readonly int _keep;

    public PreviewCache(int keep = 40) => _keep = keep;

    /// <summary>The kept copy, when the file has not changed since it was read; false when it still has to be read.</summary>
    public bool TryGet(string path, out Image? image)
    {
        image = null;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { return true; }
        if (!_images.TryGetValue(path, out var c) || c.Stamp != stamp) return false;
        image = c.Image;
        return true;
    }

    /// <summary>
    /// Reads <paramref name="path"/> in the background and keeps the result. <paramref name="shown"/> gives the image
    /// on screen when it is read, which is never thrown away to make room.
    /// </summary>
    public async Task<Image?> LoadAsync(string path, Func<Image?> shown)
    {
        if (TryGet(path, out var hit)) return hit;
        DateTime stamp; // taken before reading, so a file changed meanwhile is read again next time
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { stamp = default; }
        if (!_reading.TryGetValue(path, out var task))
        {
            task = Task.Run(() => Read(path));
            _reading[path] = task;
        }
        Image? img;
        try { img = await task; }
        finally { _reading.Remove(path); }
        var onScreen = shown();
        if (_images.TryGetValue(path, out var old))
        {
            if (ReferenceEquals(old.Image, img)) return img; // another caller kept it already
            if (!ReferenceEquals(old.Image, onScreen)) old.Image?.Dispose();
        }
        else _order.Enqueue(path);
        _images[path] = (stamp, img);
        while (_order.Count > _keep && _order.TryDequeue(out var oldest))
        {
            if (string.Equals(oldest, path, StringComparison.OrdinalIgnoreCase)) { _order.Enqueue(oldest); break; }
            if (_images.Remove(oldest, out var gone) && !ReferenceEquals(gone.Image, onScreen)) gone.Image?.Dispose();
        }
        return img;
    }

    /// <summary>The image at <paramref name="path"/>, scaled down to fit <see cref="MaxSide"/>; null when Windows can't read it.</summary>
    public static Image? Read(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var src = Image.FromStream(fs, false, false);
            var scale = Math.Min(1.0, (double)MaxSide / Math.Max(src.Width, src.Height));
            var w = Math.Max(1, (int)Math.Round(src.Width * scale));
            var h = Math.Max(1, (int)Math.Round(src.Height * scale));
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, w, h);
            return bmp;
        }
        catch { return null; } // not an image Windows can read: the panel goes without
    }

    /// <summary>Throws away every kept copy except <paramref name="shown"/>.</summary>
    public void Dispose(Image? shown)
    {
        foreach (var (_, image) in _images.Values)
            if (!ReferenceEquals(image, shown)) image?.Dispose();
        _images.Clear();
        _order.Clear();
    }

    public void Dispose() => Dispose(null);
}
