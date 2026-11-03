using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OpenIddict.Abstractions;

namespace Full.NET.UnitTests.Identity;

/// <summary>验证最小授权与生产失败关闭，防止重新引入隐式离线续期。</summary>
[TestClass]
public sealed class IdentityOidcGovernanceTests
{
    [TestMethod]
    public void Openid_only_client_has_neither_profile_nor_refresh_grant()
    {
        var descriptor = IdentityOidcClientDescriptorFactory.Build(new(
            "minimal", null, "最小授权", ["https://app.example/callback"], [], ["openid"], false, null));
        Assert.IsFalse(descriptor.Permissions.Contains(OpenIddictConstants.Permissions.Prefixes.Scope + "profile"));
        Assert.IsFalse(descriptor.Permissions.Contains(OpenIddictConstants.Permissions.Prefixes.Scope + "offline_access"));
        Assert.IsFalse(descriptor.Permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.RefreshToken));
    }

    [TestMethod]
    public void Offline_access_explicitly_enables_refresh_grant()
    {
        var descriptor = IdentityOidcClientDescriptorFactory.Build(new(
            "offline", null, "离线授权", ["https://app.example/callback"], [], ["openid", "offline_access"], false, null));
        Assert.IsTrue(descriptor.Permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.RefreshToken));
    }

    [TestMethod]
    public void Production_requires_https_and_persistent_encryption_even_with_signing_material()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var result = new IdentityOidcOptionsValidator(environment).Validate(null, new()
        {
            Enable = true, Issuer = "http://identity.example", ActiveSigningKeyId = "active",
            SigningKeys = new() { ["active"] = new() { PublicKeyPem = "public", PrivateKeyPem = "private" } }
        });
        Assert.IsTrue(result.Failed);
        StringAssert.Contains(string.Join(';', result.Failures), "HTTPS");
        StringAssert.Contains(string.Join(';', result.Failures), "EncryptionKeyBase64");
    }

    [TestMethod]
    public void Database_managed_clients_do_not_require_startup_client_configuration()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var result = new IdentityOidcOptionsValidator(environment).Validate(null, new()
        {
            Enable = true, Issuer = "https://identity.example", ActiveSigningKeyId = "active",
            EncryptionKeyBase64 = Convert.ToBase64String(new byte[32]),
            SigningKeys = new() { ["active"] = new() { PublicKeyPem = rsa.ExportRSAPublicKeyPem(), PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() } }
        });
        Assert.IsTrue(result.Succeeded);
    }
}
