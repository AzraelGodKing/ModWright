namespace SteamCmdHelper.DropPod;

/// <summary>
/// Writes RGBA pixels as a DDS texture with mipmaps: BC1 (DXT1) when every pixel is opaque, BC3 (DXT5) otherwise, the
/// block formats RimWorld 1.5 and later load straight onto the graphics card. A plain, fast encoder (endpoints from
/// each block's colour range, nearest-colour indices), written here so nothing has to be downloaded or bundled.
/// </summary>
public static class DdsEncoder
{
    /// <summary>True when a texture of this size can be block-compressed (both sides a multiple of 4).</summary>
    public static bool CanEncode(int width, int height) => width > 0 && height > 0 && width % 4 == 0 && height % 4 == 0;

    /// <param name="rgba">Width x height pixels, 4 bytes each (red, green, blue, alpha), top row first.</param>
    /// <param name="flip">Store the rows bottom first, as Unity (and so RimWorld) expects DDS files.</param>
    public static byte[] Encode(byte[] rgba, int width, int height, bool flip = true)
    {
        if (!CanEncode(width, height)) throw new ArgumentException($"A {width}x{height} texture can't be block-compressed; both sides must be multiples of 4.");
        if (rgba.Length < width * height * 4) throw new ArgumentException("Not enough pixel data.");
        var pixels = flip ? Flip(rgba, width, height) : rgba;
        var alpha = false;
        for (var i = 3; i < width * height * 4; i += 4) if (pixels[i] != 255) { alpha = true; break; }

        var levels = new List<(byte[] Px, int W, int H)> { (pixels, width, height) };
        while (levels[^1].W > 1 || levels[^1].H > 1) levels.Add(Half(levels[^1].Px, levels[^1].W, levels[^1].H));

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteHeader(w, width, height, levels.Count, alpha);
        foreach (var (px, lw, lh) in levels)
            for (var by = 0; by < (lh + 3) / 4; by++)
                for (var bx = 0; bx < (lw + 3) / 4; bx++)
                {
                    var block = Block(px, lw, lh, bx * 4, by * 4);
                    if (alpha) WriteAlphaBlock(w, block);
                    WriteColorBlock(w, block);
                }
        w.Flush();
        return ms.ToArray();
    }

    static byte[] Flip(byte[] rgba, int width, int height)
    {
        var row = width * 4;
        var o = new byte[width * height * 4];
        for (var y = 0; y < height; y++) Buffer.BlockCopy(rgba, y * row, o, (height - 1 - y) * row, row);
        return o;
    }

    /// <summary>The next mipmap: each pixel the average of a 2x2 square (colour weighted by alpha, so clear pixels don't darken edges).</summary>
    static (byte[], int, int) Half(byte[] px, int w, int h)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var o = new byte[nw * nh * 4];
        for (var y = 0; y < nh; y++)
            for (var x = 0; x < nw; x++)
            {
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var dy = 0; dy < 2; dy++)
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var sx = Math.Min(w - 1, x * 2 + dx);
                        var sy = Math.Min(h - 1, y * 2 + dy);
                        var i = (sy * w + sx) * 4;
                        var pa = px[i + 3];
                        r += px[i] * pa; g += px[i + 1] * pa; b += px[i + 2] * pa; a += pa; n++;
                    }
                var oi = (y * nw + x) * 4;
                if (a > 0) { o[oi] = (byte)(r / a); o[oi + 1] = (byte)(g / a); o[oi + 2] = (byte)(b / a); }
                o[oi + 3] = (byte)(a / n);
            }
        return (o, nw, nh);
    }

    /// <summary>The 4x4 block at (x, y), repeating the edge pixels for mipmaps smaller than a block.</summary>
    static byte[] Block(byte[] px, int w, int h, int x, int y)
    {
        var b = new byte[64];
        for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
            {
                var s = (Math.Min(h - 1, y + j) * w + Math.Min(w - 1, x + i)) * 4;
                Buffer.BlockCopy(px, s, b, (j * 4 + i) * 4, 4);
            }
        return b;
    }

    static ushort To565(int r, int g, int b) => (ushort)((r * 31 + 127) / 255 << 11 | (g * 63 + 127) / 255 << 5 | (b * 31 + 127) / 255);

    static (int R, int G, int B) From565(ushort c)
    {
        int r = c >> 11 & 31, g = c >> 5 & 63, b = c & 31;
        return (r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2);
    }

    static void WriteColorBlock(BinaryWriter w, byte[] block)
    {
        int[] min = { 255, 255, 255 }, max = { 0, 0, 0 };
        for (var p = 0; p < 16; p++)
            for (var c = 0; c < 3; c++)
            {
                min[c] = Math.Min(min[c], block[p * 4 + c]);
                max[c] = Math.Max(max[c], block[p * 4 + c]);
            }
        // Pull the ends in a little: the range's extremes are rarely worth an exact match at the cost of the middle.
        for (var c = 0; c < 3; c++)
        {
            var inset = (max[c] - min[c]) / 16;
            min[c] += inset;
            max[c] -= inset;
        }
        var c0 = To565(max[0], max[1], max[2]);
        var c1 = To565(min[0], min[1], min[2]);
        if (c0 < c1) (c0, c1) = (c1, c0);
        uint indices = 0;
        if (c0 != c1)
        {
            var e0 = From565(c0);
            var e1 = From565(c1);
            var palette = new[]
            {
                e0, e1,
                ((2 * e0.R + e1.R) / 3, (2 * e0.G + e1.G) / 3, (2 * e0.B + e1.B) / 3),
                ((e0.R + 2 * e1.R) / 3, (e0.G + 2 * e1.G) / 3, (e0.B + 2 * e1.B) / 3),
            };
            for (var p = 0; p < 16; p++)
            {
                int best = 0, bestD = int.MaxValue;
                for (var k = 0; k < 4; k++)
                {
                    int dr = block[p * 4] - palette[k].Item1, dg = block[p * 4 + 1] - palette[k].Item2, db = block[p * 4 + 2] - palette[k].Item3;
                    var d = dr * dr + dg * dg + db * db;
                    if (d < bestD) { bestD = d; best = k; }
                }
                indices |= (uint)best << (p * 2);
            }
        }
        w.Write(c0);
        w.Write(c1);
        w.Write(indices);
    }

    static void WriteAlphaBlock(BinaryWriter w, byte[] block)
    {
        int a0 = 0, a1 = 255;
        for (var p = 0; p < 16; p++) { a0 = Math.Max(a0, block[p * 4 + 3]); a1 = Math.Min(a1, block[p * 4 + 3]); }
        ulong bits = 0;
        if (a0 != a1)
        {
            var palette = new int[8];
            palette[0] = a0; palette[1] = a1;
            for (var i = 1; i <= 6; i++) palette[i + 1] = ((7 - i) * a0 + i * a1) / 7;
            for (var p = 0; p < 16; p++)
            {
                int best = 0, bestD = int.MaxValue;
                for (var k = 0; k < 8; k++)
                {
                    var d = Math.Abs(block[p * 4 + 3] - palette[k]);
                    if (d < bestD) { bestD = d; best = k; }
                }
                bits |= (ulong)best << (p * 3);
            }
        }
        w.Write((byte)a0);
        w.Write((byte)a1);
        for (var i = 0; i < 6; i++) w.Write((byte)(bits >> (i * 8)));
    }

    static void WriteHeader(BinaryWriter w, int width, int height, int mips, bool alpha)
    {
        w.Write(0x20534444u); // "DDS "
        w.Write(124u);
        w.Write(0x1u | 0x2u | 0x4u | 0x1000u | 0x20000u | 0x80000u); // caps, height, width, pixel format, mipmap count, linear size
        w.Write((uint)height);
        w.Write((uint)width);
        w.Write((uint)((width + 3) / 4 * ((height + 3) / 4) * (alpha ? 16 : 8)));
        w.Write(0u); // depth
        w.Write((uint)mips);
        for (var i = 0; i < 11; i++) w.Write(0u);
        w.Write(32u);
        w.Write(0x4u); // the pixel format is a FourCC
        w.Write(alpha ? 0x35545844u : 0x31545844u); // "DXT5" / "DXT1"
        for (var i = 0; i < 5; i++) w.Write(0u);
        w.Write(0x1000u | 0x8u | 0x400000u); // texture, complex, mipmaps
        for (var i = 0; i < 4; i++) w.Write(0u);
    }
}
