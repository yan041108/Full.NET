using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using OpenIddict.Abstractions;

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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Protocol_projection_preserves_scope_and_client_boundary_across_redemption(bool profile)
    {
        var identity = new System.Security.Claims.ClaimsIdentity("oidc");
        OpenIddict.Abstractions.OpenIddictExtensions.SetClaim(identity, "sub", UserId.ToString());
        OpenIddict.Abstractions.OpenIddictExtensions.SetClaim(identity, FullNetIdentityClaimTypes.CenterSessionId, CenterSessionId.ToString());
        OpenIddict.Abstractions.OpenIddictExtensions.SetClaim(identity, FullNetIdentityClaimTypes.ApplicationSessionId, ApplicationSessionId.ToString());
        OpenIddict.Abstractions.OpenIddictExtensions.SetClaim(identity, "name", "Display name");
        OpenIddict.Abstractions.OpenIddictExtensions.SetClaim(identity, "preferred_username", "username");
        OpenIddict.Abstractions.OpenIddictExtensions.SetClaim(identity, "fullnet_is_first_party", false);
        OpenIddict.Abstractions.OpenIddictExtensions.SetClaim(identity, "fullnet_security_stamp", "secret-stamp");
        identity.SetClaim("auth_time", 1_789_545_600L);
        OpenIddict.Abstractions.OpenIddictExtensions.SetScopes(identity, profile ? ["openid", "profile"] : ["openid"]);
        var handler = new IdentityOidcSignInHandler(new IdentityOidcPrincipalFactory(),
            Microsoft.Extensions.Options.Options.Create(new Modules.Identity.Configuration.IdentityOptions()),
            Microsoft.Extensions.Options.Options.Create(new Modules.Identity.Configuration.IdentityOidcOptions { Issuer = "https://identity.example/" }));
        var context = new OpenIddict.Server.OpenIddictServerEvents.ProcessSignInContext(new OpenIddict.Server.OpenIddictServerTransaction())
        { Principal = new System.Security.Claims.ClaimsPrincipal(identity) };

        // 授权码与刷新兑换会再次走同一处理器；不能在第一次投影后丢失客户端分类。
        for (var pass = 0; pass < 2; pass++)
        {
            await handler.HandleAsync(context);
            foreach (var type in new[] { "name", "preferred_username" })
            {
                var claim = context.Principal!.FindFirst(type)!;
                Assert.AreEqual(profile, OpenIddict.Abstractions.OpenIddictExtensions.GetDestinations(claim).Contains("id_token"));
                Assert.AreEqual(profile, OpenIddict.Abstractions.OpenIddictExtensions.GetDestinations(claim).Contains("access_token"));
            }
            Assert.IsFalse(context.Principal!.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.SecurityStamp));
            CollectionAssert.AreEqual(new[] { "id_token" }, context.Principal.FindFirst("auth_time")!.GetDestinations().ToArray());
            Assert.IsTrue(context.Principal.FindFirst("fullnet_is_first_party")!.GetDestinations().IsEmpty);
        }
    }

    [TestMethod]
    public async Task First_party_permissions_remain_repeated_claims_across_redemption()
    {
        var identity = new System.Security.Claims.ClaimsIdentity("oidc");
        identity.SetClaim("sub", UserId.ToString());
        identity.SetClaim(FullNetIdentityClaimTypes.CenterSessionId, CenterSessionId.ToString());
        identity.SetClaim(FullNetIdentityClaimTypes.ApplicationSessionId, ApplicationSessionId.ToString());
        identity.SetClaim("fullnet_is_first_party", true);
        identity.SetClaim("fullnet_permissions", "orders.read,orders.write");
        identity.SetClaim("fullnet_security_stamp", "stamp");
        identity.SetScopes("openid", "profile");
        var handler = new IdentityOidcSignInHandler(new IdentityOidcPrincipalFactory(),
            Microsoft.Extensions.Options.Options.Create(new Modules.Identity.Configuration.IdentityOptions()),
            Microsoft.Extensions.Options.Options.Create(new Modules.Identity.Configuration.IdentityOidcOptions { Issuer = "https://identity.example/" }));
        var context = new OpenIddict.Server.OpenIddictServerEvents.ProcessSignInContext(new OpenIddict.Server.OpenIddictServerTransaction())
        { Principal = new System.Security.Claims.ClaimsPrincipal(identity) };
        for (var pass = 0; pass < 2; pass++)
        {
            await handler.HandleAsync(context);
            CollectionAssert.AreEqual(new[] { "orders.read", "orders.write" },
                context.Principal!.FindAll(FullNetIdentityClaimTypes.Permission).Select(claim => claim.Value).ToArray());
            Assert.AreEqual("stamp", context.Principal.GetClaim(FullNetIdentityClaimTypes.SecurityStamp));
            Assert.IsTrue(context.Principal.FindFirst("fullnet_permissions")!.GetDestinations().IsEmpty);
        }
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
