using System.Buffers.Binary;

namespace SteamCmdHelper;

/// <summary>An image's real format and size, read from the first bytes of the file (no image library needed).</summary>
public sealed record ImageInfo(string Format, int Width, int Height);

/// <summary>
/// Reads what an image file really is, whatever its extension says, so a preview Steam won't show (a WebP, or a PNG
/// saved as .jpg) is pointed out in the list instead of after Steam turns the upload down.
/// </summary>
public static class ImageHeader
{
    /// <summary>The formats Steam shows as a Workshop preview.</summary>
    public static readonly string[] SteamFormats = { "PNG", "JPEG", "GIF" };

    /// <summary>Previews smaller than this on either side look blurry on the Workshop page.</summary>
    public const int SmallSide = 200;

    /// <summary>The image's format and size, or null when the bytes are not an image this knows (size 0 when it can't be read).</summary>
    public static ImageInfo? Read(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return new ImageInfo("PNG", BinaryPrimitives.ReadInt32BigEndian(b[16..]), BinaryPrimitives.ReadInt32BigEndian(b[20..]));
        if (b.Length >= 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8')
            return new ImageInfo("GIF", BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return Jpeg(b);
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return WebP(b);
        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M')
            return new ImageInfo("BMP", BinaryPrimitives.ReadInt32LittleEndian(b[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b[22..])));
        if (b.Length >= 4 && ((b[0] == 'I' && b[1] == 'I' && b[2] == 42 && b[3] == 0) || (b[0] == 'M' && b[1] == 'M' && b[2] == 0 && b[3] == 42)))
            return new ImageInfo("TIFF", 0, 0);
        if (b.Length >= 12 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p' && b[8] == 'a' && b[9] == 'v' && b[10] == 'i' && b[11] == 'f')
            return new ImageInfo("AVIF", 0, 0);
        return null;
    }

    static ImageInfo Jpeg(ReadOnlySpan<byte> b)
    {
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            var marker = b[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return new ImageInfo("JPEG", BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
            i += 2 + BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
        }
        return new ImageInfo("JPEG", 0, 0);
    }

    static ImageInfo WebP(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 30 && b[12] == 'V' && b[13] == 'P' && b[14] == '8')
        {
            if (b[15] == ' ') return new ImageInfo("WebP", BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b[28..]) & 0x3FFF);
            if (b[15] == 'L')
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
                return new ImageInfo("WebP", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }
            if (b[15] == 'X') return new ImageInfo("WebP", 1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
        }
        return new ImageInfo("WebP", 0, 0);
    }

    /// <summary>The format an extension claims (".jpg" is JPEG), or null for one this does not know.</summary>
    public static string? FormatOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "PNG",
        ".jpg" or ".jpeg" or ".jfif" => "JPEG",
        ".gif" => "GIF",
        ".webp" => "WebP",
        ".bmp" => "BMP",
        ".tif" or ".tiff" => "TIFF",
        ".avif" => "AVIF",
        _ => null,
    };

    /// <summary>
    /// What is worth knowing about a preview image before Steam gets it: a format Steam doesn't show, an extension
    /// that doesn't match what the file really is, or a very small picture. Empty for a file that isn't a known image.
    /// </summary>
    public static List<string> PreviewWarnings(string path)
    {
        var w = new List<string>();
        byte[] head;
        try
        {
            using var s = File.OpenRead(path);
            head = new byte[Math.Min(s.Length, 64 * 1024)];
            s.ReadExactly(head);
        }
        catch (IOException) { return w; }
        catch (UnauthorizedAccessException) { return w; }
        if (Read(head) is not { } img) return w;
        var name = Path.GetFileName(path);
        var claimed = FormatOf(Path.GetExtension(path));
        if (!SteamFormats.Contains(img.Format))
            w.Add($"{name} is really a {img.Format} image, which Steam doesn't show as a preview: save it as PNG or JPG");
        else if (claimed != null && claimed != img.Format)
            w.Add($"{name} is really a {img.Format} image saved with a {Path.GetExtension(path)} name; save it as a real {claimed} or rename it, as Steam can refuse it");
        if (img.Width > 0 && img.Height > 0 && (img.Width < SmallSide || img.Height < SmallSide))
            w.Add($"{name} is only {img.Width} x {img.Height}, so it will look blurry on the Workshop page (512 x 512 or larger looks sharp)");
        return w;
    }
}
