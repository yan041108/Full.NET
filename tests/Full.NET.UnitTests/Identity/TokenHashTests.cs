using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class TokenHashTests
{
    [TestMethod]
    public void Refresh_token_contains_32_random_bytes()
    {
        var generator = new CryptographicTokenGenerator();

        var first = generator.Generate(32);
        var second = generator.Generate(32);

        Assert.AreEqual(32, Base64UrlEncoder.DecodeBytes(first).Length);
        Assert.AreEqual(32, Base64UrlEncoder.DecodeBytes(second).Length);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void Hash_is_deterministic_lowercase_sha256_hex()
    {
        const string token = "refresh-token";

        var first = TokenHash.Compute(token);
        var second = TokenHash.Compute(token);

        Assert.AreEqual(64, first.Length);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.ToLowerInvariant(), first);
        Assert.AreEqual(
            "0eb17643d4e9261163783a420859c92c7d212fa9624106a12b510afbec266120",
            first);
    }
}
