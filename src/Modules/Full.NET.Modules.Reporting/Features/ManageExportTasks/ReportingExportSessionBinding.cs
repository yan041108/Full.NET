using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>只从认证后的主体和可信租户冻结任务绑定，浏览器不能覆盖用户、会话或安全戳。</summary>
internal static class ReportingExportSessionBinding
{
    internal static bool TryRead(ClaimsPrincipal principal, Guid? tenantId, out SessionBindingSnapshot binding)
    {
        binding = default!;
        if (tenantId is null || tenantId == Guid.Empty || principal.Identity?.IsAuthenticated != true
            || principal.Identities.Any(identity => identity.AuthenticationType is "FullNET.ApiKey" or "FullNET.Signature")
            || principal.HasClaim(c => c.Type == FullNetIdentityClaimTypes.ApiKeyId)
            || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var user) || user == Guid.Empty
            || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.TenantId), out var claimedTenant) || claimedTenant != tenantId)
            return false;
        var kind = SessionBindingKinds.Refresh;
        Guid session;
        var application = principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId);
        if (application is not null && (!Guid.TryParse(application, out var applicationId) || applicationId == Guid.Empty
            || principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse) != "access")) return false;
        if (principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse) == "access"
            && Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId), out session))
            kind = SessionBindingKinds.OidcApplication;
        else if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId), out session)) return false;
        var stamp = principal.FindFirstValue(FullNetIdentityClaimTypes.SecurityStamp);
        var actorScope = principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope);
        var effective = principal.FindFirstValue(FullNetIdentityClaimTypes.Scope);
        if (session == Guid.Empty || string.IsNullOrWhiteSpace(stamp) || stamp.Length > 128
            || string.IsNullOrWhiteSpace(actorScope) || actorScope.Length > 64
            || effective != $"tenant:{tenantId.Value:N}") return false;
        binding = new(user, tenantId, session, stamp, actorScope, effective, kind);
        return true;
    }
}
