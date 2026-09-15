using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOidcPrincipalTests
{
    private static readonly Guid UserId =
        Guid.Parse("01995f11-1200-7000-8000-000000000001");
    private static readonly Guid CenterSessionId =
        Guid.Parse("01995f11-1200-7000-8000-000000000002");
    private static readonly Guid ApplicationSessionId =
        Guid.Parse("01995f11-1200-7000-8000-000000000003");

    [TestMethod]
    public void IdToken_purpose_is_rejected_for_resource_projection()
    {
        var factory = new IdentityOidcPrincipalFactory();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            factory.EnsureResourceApiPurpose(IdentityOidcPrincipalPurpose.IdToken));
    }

    [TestMethod]
    public void External_client_does_not_receive_security_stamp_or_fullnet_permissions()
    {
        var factory = new IdentityOidcPrincipalFactory();
        var claims = factory.CreateClaims(CreateRequest(isExternalClient: true));

        Assert.IsFalse(claims.ContainsKey(FullNetIdentityClaimTypes.SecurityStamp));
        Assert.IsFalse(claims.ContainsKey(FullNetIdentityClaimTypes.SuperAdministrator));
        Assert.IsFalse(claims.ContainsKey(FullNetIdentityClaimTypes.Permission));
    }

    [TestMethod]
    public void OAuth_scopes_are_separate_from_fullnet_scope()
    {
        var factory = new IdentityOidcPrincipalFactory();
        var claims = factory.CreateClaims(CreateRequest(
            isExternalClient: true,
            oauthScopes: ["openid", "profile"],
            effectiveScope: "host"));

        Assert.AreEqual("openid profile", claims["scope"]);
        Assert.AreEqual("host", claims[FullNetIdentityClaimTypes.Scope]);
    }

    private static IdentityOidcPrincipalRequest CreateRequest(
        bool isExternalClient,
        IReadOnlyCollection<string>? oauthScopes = null,
        string effectiveScope = "host") => new(
        IdentityOidcPrincipalPurpose.ResourceApi,
        UserId,
        "Admin",
        "admin",
        CenterSessionId,
        ApplicationSessionId,
        "integration-client",
        "host",
        effectiveScope,
        null,
        oauthScopes ?? [],
        ["tenancy.tenants.read"],
        true,
        "stamp",
        isExternalClient);
}
