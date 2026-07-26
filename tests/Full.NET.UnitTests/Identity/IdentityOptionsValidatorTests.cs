using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOptionsValidatorTests
{
    [TestMethod]
    public void Defaults_and_required_runtime_configuration_match_the_approved_security_baseline()
    {
        var options = new IdentityOptions();

        Assert.AreEqual(10, options.AccessTokenMinutes);
        Assert.AreEqual(7, options.RefreshTokenDays);
        Assert.AreEqual(5, options.LockoutThreshold);
        Assert.AreEqual(15, options.LockoutMinutes);
        Assert.IsTrue(options.RequireSecureCookies);
        Assert.IsFalse(options.EnableRemoteSuperAdministratorManagement);
        Assert.IsFalse(options.EnableTotpStrongReauthentication);

        var validator = CreateValidator(Environments.Production);
        var invalidLoginLimit = validator.Validate(null, new IdentityOptions
        {
            EnableTokenEndpoints = false,
            LoginRateLimitPermitLimitPerMinute = 0,
        });
        var invalidSessionLimit = validator.Validate(null, new IdentityOptions
        {
            EnableTokenEndpoints = false,
            SessionMutationRateLimitPermitLimitPerMinute = 0,
        });
        var missingSigningKeys = validator.Validate(null, new IdentityOptions
        {
            EnableTokenEndpoints = false,
            SigningKeys = null!,
        });
        var missingAllowedOrigins = validator.Validate(null, new IdentityOptions
        {
            EnableTokenEndpoints = false,
            AllowedOrigins = null!,
        });
        var missingBootstrap = validator.Validate(null, new IdentityOptions
        {
            EnableTokenEndpoints = false,
            Bootstrap = null!,
        });

        Assert.IsTrue(invalidLoginLimit.Failed);
        StringAssert.Contains(
            string.Join(";", invalidLoginLimit.Failures),
            "LoginRateLimitPermitLimitPerMinute");
        Assert.IsTrue(invalidSessionLimit.Failed);
        StringAssert.Contains(
            string.Join(";", invalidSessionLimit.Failures),
            "SessionMutationRateLimitPermitLimitPerMinute");
        Assert.IsTrue(missingSigningKeys.Failed);
        StringAssert.Contains(
            string.Join(";", missingSigningKeys.Failures),
            "SigningKeys");
        Assert.IsTrue(missingAllowedOrigins.Failed);
        StringAssert.Contains(
            string.Join(";", missingAllowedOrigins.Failures),
            "AllowedOrigins");
        Assert.IsTrue(missingBootstrap.Failed);
        StringAssert.Contains(
            string.Join(";", missingBootstrap.Failures),
            "Bootstrap");
    }

    [TestMethod]
    public void Missing_or_null_signing_key_is_rejected_outside_explicit_ephemeral_mode()
    {
        var validator = CreateValidator(Environments.Production);
        var result = validator
            .Validate(null, new IdentityOptions());
        var nullSigningKey = validator.Validate(null, new IdentityOptions
        {
            ActiveKeyId = "active",
            SigningKeys = new Dictionary<string, IdentitySigningKeyOptions>
            {
                ["active"] = new()
                {
                    PublicKeyPem = "public-key",
                    PrivateKeyPem = "private-key",
                },
                ["retired"] = null!,
            },
        });

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(string.Join(";", result.Failures), "signing key");
        Assert.IsTrue(nullSigningKey.Failed);
        StringAssert.Contains(
            string.Join(";", nullSigningKey.Failures),
            "SigningKeys");
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
