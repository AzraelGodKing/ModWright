using System.Text;

namespace SteamCmdHelper;

/// <summary>
/// The Linear request key baked in at build time. RequestKey.targets generates the other half of this class
/// (obj/RequestKey.g.cs, never committed) when a key is available; without it <see cref="Read"/> returns null.
/// </summary>
static partial class BakedRequestKey
{
    static partial void Fill(ref byte[]? data, ref byte[]? mask);

    /// <summary>"key\nteamId", or null when this build has no key.</summary>
    public static string? Read()
    {
        byte[]? data = null, mask = null;
        Fill(ref data, ref mask);
        if (data == null || mask == null || mask.Length == 0) return null;
        var plain = new byte[data.Length];
        for (var i = 0; i < data.Length; i++) plain[i] = (byte)(data[i] ^ mask[i % mask.Length]);
        return Encoding.UTF8.GetString(plain);
    }
}
