using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
namespace Full.NET.Testing;
/// <summary>仅供测试把已受信会话快照装配为认证主体，保留会话种类与有效租户范围。</summary>
internal static class SessionBindingTestPrincipal
{
    internal static ClaimsPrincipal Create(SessionBindingSnapshot binding)
    {
        var claims = new List<Claim>
        {
            new(FullNetIdentityClaimTypes.Subject, binding.UserId.ToString("D")),
            new(FullNetIdentityClaimTypes.SessionId, binding.SessionId.ToString("D")),
            new(FullNetIdentityClaimTypes.SecurityStamp, binding.SecurityStamp),
            new(FullNetIdentityClaimTypes.ActorScope, binding.ActorScope),
            new(FullNetIdentityClaimTypes.Scope, binding.EffectiveScope),
        };
        if (binding.TenantId is { } tenant) claims.Add(new(FullNetIdentityClaimTypes.TenantId, tenant.ToString("D")));
        if (binding.SessionKind == SessionBindingKinds.OidcApplication)
        {
            claims.Add(new(FullNetIdentityClaimTypes.TokenUse, "access"));
            claims.Add(new(FullNetIdentityClaimTypes.ApplicationSessionId, binding.SessionId.ToString("D")));
        }
        return new(new ClaimsIdentity(claims, "Bearer"));
    }
}
