namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOidcPkceHelperTests
{
    [TestMethod]
    public void Pkce_challenge_is_s256_of_verifier()
    {
        var (verifier, challenge) = IdentityOidcPkceHelper.CreatePkcePair();
        Assert.AreEqual(IdentityOidcPkceHelper.CreateCodeChallenge(verifier), challenge);
    }

    [TestMethod]
    public void Nonce_validation_rejects_mismatch()
    {
        Assert.IsFalse(IdentityOidcPkceHelper.ValidateNonce("expected", "other"));
        Assert.IsTrue(IdentityOidcPkceHelper.ValidateNonce("same", "same"));
    }
}
