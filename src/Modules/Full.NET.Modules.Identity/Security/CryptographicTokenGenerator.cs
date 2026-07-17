using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Security;

internal sealed class CryptographicTokenGenerator : IRandomTokenGenerator
{
    public string Generate(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteCount, 16);
        return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(byteCount));
    }
}
