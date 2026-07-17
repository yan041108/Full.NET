using Full.NET.Modules.Identity.Configuration;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOptionsValidatorTests
{
    [TestMethod]
    public void Defaults_match_the_approved_security_baseline()
    {
        var options = new IdentityOptions();

        Assert.AreEqual(10, options.AccessTokenMinutes);
        Assert.AreEqual(7, options.RefreshTokenDays);
        Assert.AreEqual(5, options.LockoutThreshold);
        Assert.AreEqual(15, options.LockoutMinutes);
        Assert.IsTrue(options.RequireSecureCookies);
    }

    [TestMethod]
    public void Missing_signing_key_is_rejected_outside_explicit_ephemeral_mode()
    {
        var result = new IdentityOptionsValidator().Validate(null, new IdentityOptions());

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(string.Join(";", result.Failures), "signing key");
    }

    [TestMethod]
    public void Explicit_ephemeral_mode_allows_development_configuration()
    {
        var options = new IdentityOptions
        {
            AllowDevelopmentEphemeralSigningKey = true,
        };

        var result = new IdentityOptionsValidator().Validate(null, options);

        Assert.IsTrue(result.Succeeded);
    }
}
