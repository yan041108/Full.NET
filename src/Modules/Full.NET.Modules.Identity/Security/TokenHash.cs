using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Identity.Security;

internal static class TokenHash
{
    public static string Compute(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
