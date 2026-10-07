using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>从已认证请求冻结执行会话，拒绝 API Key 或由请求体提供主体绑定。</summary>
internal static class ImportExportHttpSessionBinding
{
    internal static bool TryCreate(HttpContext context, Guid? tenantId, out SessionBindingSnapshot binding)
    {
        binding = default!;
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true || tenantId is null
            || principal.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.ApiKeyId)
            || !Guid.TryParse(principal.FindFirstValue("sub"), out var userId) || userId == Guid.Empty)
            return false;

        var sessionKind = SessionBindingKinds.Refresh;
        Guid sessionId;
        if (string.Equals(principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse), "access", StringComparison.Ordinal)
            && Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId), out sessionId))
            sessionKind = SessionBindingKinds.OidcApplication;
        else if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId), out sessionId))
            return false;

        var stamp = principal.FindFirstValue(FullNetIdentityClaimTypes.SecurityStamp);
        var actorScope = principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope);
        var effectiveScope = principal.FindFirstValue(FullNetIdentityClaimTypes.Scope);
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(stamp)
            || string.IsNullOrWhiteSpace(actorScope) || string.IsNullOrWhiteSpace(effectiveScope))
            return false;
        binding = new(userId, tenantId, sessionId, stamp, actorScope, effectiveScope, sessionKind);
        return true;
    }
}
