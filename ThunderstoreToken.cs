using System.Security.Cryptography;
using System.Text;

namespace SteamCmdHelper;

/// <summary>Keeps the Thunderstore token in the settings encrypted for the Windows user, never in plain text.</summary>
static class ThunderstoreToken
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SteamCmdHelper.Thunderstore");

    public static string Protect(string token) => token.Length == 0 ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The token, or "" when there is none or it was saved by another Windows user or PC.</summary>
    public static string Unprotect(string stored)
    {
        if (stored.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return ""; }
    }
}
