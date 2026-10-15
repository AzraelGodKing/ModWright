using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace SteamCmdHelper;

/// <summary>Brings a Workshop preview image under Steam's size limit, keeping a copy of the original.</summary>
[SupportedOSPlatform("windows6.1")]
public static class PreviewImage
{
    /// <summary>Where originals are kept before shrinking, outside the mod folder so they are never uploaded.</summary>
    public static string BackupRoot => Path.Combine(AppSettings.Folder, "preview-backups");

    /// <summary>
    /// Re-encodes <paramref name="path"/> (and scales it down only as far as needed) until it is at most
    /// <paramref name="maxBytes"/>. The original is copied to <paramref name="backupRoot"/> (default
    /// <see cref="BackupRoot"/>) first. Returns a line for the log.
    /// </summary>
    public static string Shrink(string path, string modName, long maxBytes, string? backupRoot = null)
    {
        var original = File.ReadAllBytes(path);
        var backupDir = Path.Combine(backupRoot ?? BackupRoot, string.Concat(modName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));
        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
        File.WriteAllBytes(backup, original);

        using var src = Image.FromStream(new MemoryStream(original));
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var jpeg = ext is ".jpg" or ".jpeg";
        var alpha = !jpeg && Image.IsAlphaPixelFormat(src.PixelFormat);

        var scale = 1.0;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var bytes = Encode(src, scale, jpeg, alpha);
            if (bytes.Length <= maxBytes)
            {
                File.WriteAllBytes(path, bytes);
                var w = (int)Math.Round(src.Width * scale);
                var h = (int)Math.Round(src.Height * scale);
                return $"{Path.GetFileName(path)}: {original.Length / 1024} KB to {bytes.Length / 1024} KB"
                    + (scale < 1 ? $", {src.Width}x{src.Height} to {w}x{h}" : "")
                    + $". The original is kept in {backupDir}.";
            }
            // File size grows roughly with pixel count, so scale both sides by the square root of the overshoot.
            scale *= Math.Clamp(Math.Sqrt((double)maxBytes / bytes.Length) * 0.95, 0.5, 0.95);
        }
        throw new InvalidOperationException($"could not get {Path.GetFileName(path)} under {maxBytes / 1024} KB. The original is kept in {backupDir}.");
    }

    static byte[] Encode(Image src, double scale, bool jpeg, bool alpha)
    {
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));
        using var bmp = new Bitmap(w, h, alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            if (!alpha) g.Clear(Color.White);
            using var wrap = new ImageAttributes();
            wrap.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe along the edges when scaling
            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
        }
        using var ms = new MemoryStream();
        if (jpeg)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
            bmp.Save(ms, codec, p);
        }
        else bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
