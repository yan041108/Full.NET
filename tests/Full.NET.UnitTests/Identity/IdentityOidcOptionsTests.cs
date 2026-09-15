using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOidcOptionsTests
{
    [TestMethod]
    public void Oidc_protocol_is_disabled_by_default()
    {
        var options = new IdentityOidcOptions();
        var validator = CreateValidator(Environments.Production);

        Assert.IsFalse(options.Enable);
        Assert.IsTrue(validator.Validate(null, options).Succeeded);
    }

    [TestMethod]
    public void Production_enabled_oidc_rejects_missing_issuer_and_persistent_signing_key()
    {
        var validator = CreateValidator(Environments.Production);
        var missingIssuer = validator.Validate(null, new IdentityOidcOptions
        {
            Enable = true,
            ActiveSigningKeyId = "active",
            SigningKeys = new Dictionary<string, IdentityOidcSigningKeyOptions>
            {
                ["active"] = new()
                {
                    PublicKeyPem = "public",
                    PrivateKeyPem = "private",
                },
            },
            Clients =
            [
                new()
                {
                    ClientId = "fixture-a",
                    RedirectUris = ["https://a.example.com/signin-oidc"],
                },
            ],
        });
        var missingSigningKey = validator.Validate(null, new IdentityOidcOptions
        {
            Enable = true,
            Issuer = "https://identity.example.com",
            Clients =
            [
                new()
                {
                    ClientId = "fixture-a",
                    RedirectUris = ["https://a.example.com/signin-oidc"],
                },
            ],
        });

        Assert.IsTrue(missingIssuer.Failed);
        StringAssert.Contains(
            string.Join(";", missingIssuer.Failures),
            "Issuer");
        Assert.IsTrue(missingSigningKey.Failed);
        StringAssert.Contains(
            string.Join(";", missingSigningKey.Failures),
            "signing key");
    }

    [TestMethod]
    [DataRow("https://*.example.com/callback")]
    [DataRow("/relative/callback")]
    [DataRow("javascript:alert(1)")]
    public void Callback_registration_rejects_nonexact_or_wildcard_redirect_uris(string redirectUri)
    {
        Assert.IsFalse(IdentityOidcRedirectUriPolicy.IsExactAbsoluteUri(redirectUri));

        var validator = CreateValidator(Environments.Development);
        var result = validator.Validate(null, new IdentityOidcOptions
        {
            Enable = true,
            Issuer = "https://identity.example.com",
            AllowDevelopmentEphemeralSigningKey = true,
            Clients =
            [
                new()
                {
                    ClientId = "fixture-a",
                    RedirectUris = [redirectUri],
                },
            ],
        });

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(
            string.Join(";", result.Failures),
            "redirect");
    }

    [TestMethod]
    public void Registered_redirect_uri_policy_rejects_arbitrary_callback_origin()
    {
        var registered = new[]
        {
            "https://a.example.com/signin-oidc",
            "https://b.example.com/signin-oidc",
        };

        Assert.IsTrue(IdentityOidcRedirectUriPolicy.IsRegisteredRedirectUri(
            "https://a.example.com/signin-oidc",
            registered));
        Assert.IsFalse(IdentityOidcRedirectUriPolicy.IsRegisteredRedirectUri(
            "https://evil.example.com/signin-oidc",
            registered));
        Assert.IsFalse(IdentityOidcRedirectUriPolicy.IsRegisteredRedirectUri(
            "https://a.example.com.evil/signin-oidc",
            registered));
    }

    private static IdentityOidcOptionsValidator CreateValidator(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new IdentityOidcOptionsValidator(environment);
    }
}