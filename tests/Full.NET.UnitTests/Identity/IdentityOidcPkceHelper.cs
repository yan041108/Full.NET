using System.Security.Cryptography;
using System.Text;

namespace Full.NET.UnitTests.Identity;

internal static class IdentityOidcPkceHelper
{
    public static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        return (verifier, challenge);
    }

    public static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    public static bool ValidateNonce(string expectedNonce, string idTokenNonce)
        => string.Equals(expectedNonce, idTokenNonce, StringComparison.Ordinal);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
