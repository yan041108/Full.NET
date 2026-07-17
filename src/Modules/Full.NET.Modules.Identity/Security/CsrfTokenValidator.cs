using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Security;

internal static class CsrfTokenValidator
{
    public static bool IsValid(string? cookieToken, string? headerToken)
    {
        if (string.IsNullOrEmpty(cookieToken) || string.IsNullOrEmpty(headerToken))
        {
            return false;
        }

        var cookieHash = SHA256.HashData(Encoding.UTF8.GetBytes(cookieToken));
        var headerHash = SHA256.HashData(Encoding.UTF8.GetBytes(headerToken));
        return CryptographicOperations.FixedTimeEquals(cookieHash, headerHash);
    }
}
