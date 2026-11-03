using System.Security.Cryptography;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Full.NET.UnitTests.Identity;

/// <summary>规范指纹、私钥匹配和退役签名闭包不能受 PEM 编码或配置残留影响。</summary>
[TestClass]
public sealed class IdentityOidcKeyLifecycleTests
{
    [TestMethod]
    public void Fingerprint_is_stable_across_public_pem_formats_and_rejects_a_mismatched_private_key()
    {
        using var a = RSA.Create(2048); using var b = RSA.Create(2048);
        var pkcs = IdentityOidcKeyMaterial.Hash(new IdentityOidcSigningKeyOptions { PublicKeyPem = a.ExportRSAPublicKeyPem() });
        Assert.AreEqual(pkcs, IdentityOidcKeyMaterial.Hash(new IdentityOidcSigningKeyOptions { PublicKeyPem = a.ExportSubjectPublicKeyInfoPem().Replace("\n", "\\n", StringComparison.Ordinal) }));
        Assert.ThrowsExactly<InvalidOperationException>(() => IdentityOidcKeyMaterial.Hash(new IdentityOidcSigningKeyOptions
        { PublicKeyPem = a.ExportRSAPublicKeyPem(), PrivateKeyPem = b.ExportRSAPrivateKeyPem() }));
    }

    [TestMethod]
    public void Retired_key_is_removed_from_validation_and_server_jwks_even_if_secret_configuration_still_contains_it()
    {
        using var a = RSA.Create(2048); using var b = RSA.Create(2048);
        using var ring = new IdentityOidcSigningKeyRing(Options.Create(new IdentityOidcOptions
        {
            Enable = true, ActiveSigningKeyId = "a", SigningKeys = new()
            {
                ["a"] = new() { PublicKeyPem = a.ExportRSAPublicKeyPem(), PrivateKeyPem = a.ExportRSAPrivateKeyPem() },
                ["b"] = new() { PublicKeyPem = b.ExportRSAPublicKeyPem(), PrivateKeyPem = b.ExportRSAPrivateKeyPem() }
            }
        }), NullLogger<IdentityOidcSigningKeyRing>.Instance);
        Assert.ThrowsExactly<InvalidOperationException>(() => ring.ApplyRetired(["a"]));
        Assert.IsTrue(ring.TryActivate("b").IsSuccess); Assert.IsTrue(ring.ApplyRetired(["a"]));
        Assert.IsFalse(ring.TryActivate("a").IsSuccess);
        var options = new OpenIddictServerOptions(); IdentityOidcSigningCredentialSync.Apply(options, ring);
        CollectionAssert.AreEqual(new[] { "b" }, options.SigningCredentials.Select(value => value.Key.KeyId).ToArray());
        CollectionAssert.AreEqual(new[] { "b" }, ring.ValidationKeys.Select(value => value.KeyId).ToArray());
    }

    [TestMethod]
    public void Key_identifiers_cannot_inject_route_or_manifest_separators()
    {
        Assert.IsTrue(IdentityOidcKeyMaterial.IsValidId("rsa-2026.10_v2"));
        foreach (var id in new[] { "", ".", "..", "../key", "rsa|forged", "rsa:key", "rsa/key", new string('a', 129) })
            Assert.IsFalse(IdentityOidcKeyMaterial.IsValidId(id), id);
    }
}
