using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Identity;

/// <summary>绑定提取只接受可信租户中的交互会话；后续仍须调用权威授权 Port。</summary>
[TestClass]
public sealed class SessionBindingSnapshotReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Refresh_and_oidc_sessions_keep_original_actor_and_effective_scope(bool oidc)
    {
        var tenant = Guid.CreateVersion7(); var principal = Principal(tenant, oidc);
        Assert.IsTrue(SessionBindingSnapshotReader.TryReadTenant(principal, tenant, out var binding));
        Assert.AreEqual(tenant, binding.TenantId); Assert.AreEqual("host", binding.ActorScope);
        Assert.AreEqual(oidc ? SessionBindingKinds.OidcApplication : SessionBindingKinds.Refresh, binding.SessionKind);
        Assert.IsFalse(SessionBindingSnapshotReader.TryReadTenant(principal, Guid.CreateVersion7(), out _));
        Assert.IsFalse(SessionBindingSnapshotReader.TryReadTenant(principal, null, out _));
    }

    [TestMethod]
    public void Api_keys_forged_effective_scope_and_missing_session_are_rejected()
    {
        var tenant = Guid.CreateVersion7(); var key = Principal(tenant, false);
        ((ClaimsIdentity)key.Identity!).AddClaim(new(FullNetIdentityClaimTypes.ApiKeyId, Guid.CreateVersion7().ToString()));
        Assert.IsFalse(SessionBindingSnapshotReader.TryReadTenant(key, tenant, out _));
        foreach (var claim in new[] { FullNetIdentityClaimTypes.SessionId, FullNetIdentityClaimTypes.SecurityStamp, FullNetIdentityClaimTypes.Scope })
        {
            var principal = Principal(tenant, false); var identity = (ClaimsIdentity)principal.Identity!; identity.RemoveClaim(identity.FindFirst(claim)!);
            Assert.IsFalse(SessionBindingSnapshotReader.TryReadTenant(principal, tenant, out _));
        }
    }

    private static ClaimsPrincipal Principal(Guid tenant, bool oidc) => new(new ClaimsIdentity(new[]
    {
        new Claim(FullNetIdentityClaimTypes.Subject, Guid.CreateVersion7().ToString()), new(FullNetIdentityClaimTypes.TenantId, tenant.ToString()),
        new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"), new(FullNetIdentityClaimTypes.ActorScope, "host"), new(FullNetIdentityClaimTypes.Scope, $"tenant:{tenant:N}"),
        new(oidc ? FullNetIdentityClaimTypes.ApplicationSessionId : FullNetIdentityClaimTypes.SessionId, Guid.CreateVersion7().ToString()),
        new(FullNetIdentityClaimTypes.TokenUse, oidc ? "access" : "interactive"),
    }, "test"));
}
