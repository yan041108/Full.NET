using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

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
        Assert.IsFalse(options.EnableRemoteSuperAdministratorManagement);
        Assert.IsFalse(options.EnableTotpStrongReauthentication);
    }

    [TestMethod]
    public void Missing_signing_key_is_rejected_outside_explicit_ephemeral_mode()
    {
        var result = CreateValidator(Environments.Production)
            .Validate(null, new IdentityOptions());

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

        var result = CreateValidator(Environments.Development).Validate(null, options);

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Explicit_ephemeral_mode_is_rejected_in_production()
    {
        var options = new IdentityOptions
        {
            AllowDevelopmentEphemeralSigningKey = true,
        };

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(string.Join(";", result.Failures), "Development or Testing");
    }

    [TestMethod]
    public void Remote_super_administrator_management_is_rejected_in_production_without_totp_provider()
    {
        var options = new IdentityOptions
        {
            EnableRemoteSuperAdministratorManagement = true,
            EnableTokenEndpoints = false,
        };

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(
            string.Join(";", result.Failures),
            "cannot be enabled in Production");
    }

    [TestMethod]
    public void Remote_super_administrator_management_is_allowed_in_production_with_totp_provider()
    {
        var options = new IdentityOptions
        {
            EnableRemoteSuperAdministratorManagement = true,
            EnableTotpStrongReauthentication = true,
            EnableTokenEndpoints = false,
        };

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Failures ?? []));
    }

    private static IdentityOptionsValidator CreateValidator(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new IdentityOptionsValidator(environment);
    }
}
