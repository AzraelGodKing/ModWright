using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SteamCmdHelper;

/// <summary>
/// Checks on the SteamCMD download. Valve publishes no checksum for steamcmd.zip, so the download is trusted
/// only when it arrived whole (length matches, every zip entry passes its CRC), holds steamcmd.exe, and that
/// exe carries a valid Authenticode signature from Valve.
/// </summary>
public static class SteamCmdIntegrity
{
    public const string ExeName = "steamcmd.exe";
    const long MinZipBytes = 100 * 1024, MaxZipBytes = 64 * 1024 * 1024;

    /// <summary>Throws <see cref="InvalidDataException"/> unless the downloaded bytes are a complete, intact steamcmd.zip.</summary>
    public static void VerifyDownload(byte[] bytes, long? contentLength)
    {
        if (contentLength is long n && n != bytes.Length)
            throw new InvalidDataException($"The SteamCMD download is incomplete ({bytes.Length} of {n} bytes).");
        if (bytes.Length < MinZipBytes || bytes.Length > MaxZipBytes)
            throw new InvalidDataException($"The SteamCMD download is {bytes.Length} bytes, which is not a plausible size for steamcmd.zip.");

        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            var hasExe = false;
            foreach (var e in zip.Entries)
            {
                if (e.FullName.Equals(ExeName, StringComparison.OrdinalIgnoreCase)) hasExe = true;
                if (Crc32Of(e) != e.Crc32)
                    throw new InvalidDataException($"The SteamCMD download is corrupt ({e.FullName} fails its checksum).");
            }
            if (!hasExe) throw new InvalidDataException($"The SteamCMD download does not contain {ExeName}.");
        }
        catch (InvalidDataException) { throw; }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            throw new InvalidDataException("The SteamCMD download is not a valid zip file (" + e.Message + ").", e);
        }
    }

    static uint Crc32Of(ZipArchiveEntry e)
    {
        using var s = e.Open();
        var crc = 0xFFFFFFFFu;
        var buf = new byte[81920];
        int read;
        while ((read = s.Read(buf, 0, buf.Length)) > 0)
            for (var i = 0; i < read; i++)
                crc = CrcTable[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var c = (uint)i;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>
    /// Throws unless <paramref name="exePath"/> has a valid Authenticode signature whose signer is Valve.
    /// Windows only; elsewhere there is no system trust check to call, so this does nothing.
    /// </summary>
    public static void VerifyValveSignature(string exePath)
    {
        if (!OperatingSystem.IsWindows()) return;

        var status = WinTrust.Verify(exePath);
        if (status != 0)
            throw new InvalidDataException($"{ExeName} does not have a valid digital signature (WinVerifyTrust 0x{status:X8}). It was not installed.");

        string signer;
        try
        {
#pragma warning disable SYSLIB0057 // reads the signer of an already verified file
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(exePath));
#pragma warning restore SYSLIB0057
            signer = cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (Exception e) { throw new InvalidDataException($"Could not read the signer of {ExeName}: {e.Message}", e); }

        if (!signer.Contains("Valve", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{ExeName} is signed by \"{signer}\", not Valve. It was not installed.");
    }

    static class WinTrust
    {
        static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        const uint UiNone = 2, RevokeNone = 0, ChoiceFile = 1, ActionVerify = 1, ActionClose = 2;

        [StructLayout(LayoutKind.Sequential)]
        struct FileInfo
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Data
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref Data data);

        public static int Verify(string path)
        {
            var pathPtr = Marshal.StringToHGlobalUni(path);
            var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            try
            {
                Marshal.StructureToPtr(new FileInfo { cbStruct = (uint)Marshal.SizeOf<FileInfo>(), pcwszFilePath = pathPtr }, filePtr, false);
                var data = new Data
                {
                    cbStruct = (uint)Marshal.SizeOf<Data>(),
                    dwUIChoice = UiNone,
                    fdwRevocationChecks = RevokeNone,
                    dwUnionChoice = ChoiceFile,
                    pFile = filePtr,
                    dwStateAction = ActionVerify
                };
                var action = GenericVerifyV2;
                var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                data.dwStateAction = ActionClose;
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(filePtr);
                Marshal.FreeHGlobal(pathPtr);
            }
        }
    }
}
