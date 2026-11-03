using System.Security.Claims;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>从服务端已认证主体提取租户会话绑定；提取结果仍必须经过权威会话 Port 复核。</summary>
public static class SessionBindingSnapshotReader
{
    /// <summary>仅接受与可信租户一致的 Refresh/OIDC 交互会话，不接受 API Key 或签名代替会话。</summary>
    /// <param name="principal">服务端认证管线建立的主体，不得使用客户端提交的声明集合。</param>
    /// <param name="tenantId">可信当前租户标识。</param>
    /// <param name="binding">成功时返回有界会话快照；失败时为空占位值。</param>
    /// <returns>声明完整且作用域匹配时为真，不表示已经完成当前权限校验。</returns>
    public static bool TryReadTenant(ClaimsPrincipal principal, Guid? tenantId, out SessionBindingSnapshot binding)
    {
        ArgumentNullException.ThrowIfNull(principal); binding = default!;
        if (tenantId is null || tenantId == Guid.Empty || principal.Identity?.IsAuthenticated != true
            || principal.Identities.Any(identity => identity.AuthenticationType is "FullNET.ApiKey" or "FullNET.Signature")
            || principal.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.ApiKeyId)
            || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var user) || user == Guid.Empty
            || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.TenantId), out var claimedTenant) || claimedTenant != tenantId)
            return false;
        var kind = SessionBindingKinds.Refresh; Guid session;
        var application = principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId);
        if (application is not null && (!Guid.TryParse(application, out var applicationId) || applicationId == Guid.Empty
            || principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse) != "access")) return false;
        if (principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse) == "access"
            && Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId), out session)) kind = SessionBindingKinds.OidcApplication;
        else if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId), out session)) return false;
        var stamp = principal.FindFirstValue(FullNetIdentityClaimTypes.SecurityStamp);
        var actorScope = principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope);
        var effective = principal.FindFirstValue(FullNetIdentityClaimTypes.Scope);
        if (session == Guid.Empty || string.IsNullOrWhiteSpace(stamp) || stamp.Length > 128
            || string.IsNullOrWhiteSpace(actorScope) || actorScope.Length > 64 || effective != $"tenant:{tenantId.Value:N}") return false;
        binding = new(user, tenantId, session, stamp, actorScope, effective, kind); return true;
    }
}
