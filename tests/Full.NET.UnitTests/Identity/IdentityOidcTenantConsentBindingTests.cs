using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.DataProtection;
using OpenIddict.Abstractions;

namespace Full.NET.UnitTests.Identity;

/// <summary>租户同意凭据只能用于原中心会话、租户及完整授权请求。</summary>
[TestClass]
public sealed class IdentityOidcTenantConsentBindingTests
{
    [TestMethod]
    public void Consent_proof_rejects_changed_tenant_session_and_request()
    {
        var binding = new IdentityOidcTenantConsentBinding(new EphemeralDataProtectionProvider());
        var center = Guid.CreateVersion7();
        var tenant = Guid.CreateVersion7();
        var consent = Guid.CreateVersion7();
        var request = new OpenIddictRequest
        {
            ClientId = "client-a", RedirectUri = "https://client.example/callback", ResponseType = "code",
            Scope = "openid profile", State = "state", Nonce = "nonce", CodeChallenge = "challenge",
            CodeChallengeMethod = "S256",
        };
        var proof = binding.Create(request, center, tenant, consent);
        Assert.IsTrue(binding.Matches(proof, request, center, tenant, consent));
        Assert.IsFalse(binding.Matches(proof, request, center, Guid.CreateVersion7(), consent));
        Assert.IsFalse(binding.Matches(proof, request, Guid.CreateVersion7(), tenant, consent));
        Assert.IsFalse(binding.Matches(proof, request, center, tenant, Guid.CreateVersion7()));
        request.ClientId = "client-b";
        Assert.IsFalse(binding.Matches(proof, request, center, tenant, consent));
        request.ClientId = "client-a";
        request.CodeChallenge = "another-challenge";
        Assert.IsFalse(binding.Matches(proof, request, center, tenant, consent));
        Assert.IsFalse(binding.Matches(proof + "invalid", request, center, tenant, consent));
    }
}
