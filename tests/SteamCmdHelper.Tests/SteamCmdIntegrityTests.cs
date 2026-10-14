using System.IO.Compression;
using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public class SteamCmdIntegrityTests
{
    static byte[] MakeZip(params (string name, int size)[] entries)
    {
        var rng = new Random(42);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, size) in entries)
            {
                var data = new byte[size];
                rng.NextBytes(data); // random bytes do not compress, so the zip keeps a realistic size
                using var s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    static readonly byte[] Good = MakeZip(("steamcmd.exe", 400_000));

    [Fact]
    public void Intact_download_passes()
    {
        SteamCmdIntegrity.VerifyDownload(Good, Good.Length);
        SteamCmdIntegrity.VerifyDownload(Good, null);
    }

    [Fact]
    public void Length_mismatch_fails()
    {
        var e = Assert.Throws<InvalidDataException>(() => SteamCmdIntegrity.VerifyDownload(Good, Good.Length + 10));
        Assert.Contains("incomplete", e.Message);
    }

    [Fact]
    public void Truncated_zip_fails()
    {
        var cut = Good.Take(Good.Length - 5000).ToArray();
        Assert.Throws<InvalidDataException>(() => SteamCmdIntegrity.VerifyDownload(cut, null));
    }

    [Fact]
    public void Corrupted_entry_fails_its_checksum()
    {
        var bad = (byte[])Good.Clone();
        bad[bad.Length / 2] ^= 0xFF; // inside the stored exe data
        var e = Assert.Throws<InvalidDataException>(() => SteamCmdIntegrity.VerifyDownload(bad, bad.Length));
        Assert.Contains("checksum", e.Message);
    }

    [Fact]
    public void Zip_without_steamcmd_exe_fails()
    {
        var other = MakeZip(("readme.txt", 300_000));
        var e = Assert.Throws<InvalidDataException>(() => SteamCmdIntegrity.VerifyDownload(other, other.Length));
        Assert.Contains("steamcmd.exe", e.Message);
    }

    [Fact]
    public void Html_error_page_fails()
    {
        var html = System.Text.Encoding.UTF8.GetBytes("<html><body>Access denied</body></html>" + new string(' ', 200_000));
        Assert.Throws<InvalidDataException>(() => SteamCmdIntegrity.VerifyDownload(html, html.Length));
    }

    [Fact]
    public void Implausibly_small_download_fails()
    {
        var tiny = MakeZip(("steamcmd.exe", 10));
        Assert.Throws<InvalidDataException>(() => SteamCmdIntegrity.VerifyDownload(tiny, tiny.Length));
    }
}
