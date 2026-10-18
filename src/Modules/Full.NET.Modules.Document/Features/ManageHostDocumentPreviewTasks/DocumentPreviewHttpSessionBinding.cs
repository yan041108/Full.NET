using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>只从可信 Host 请求冻结交互会话，拒绝机器凭据和租户范围的声明。</summary>
internal static class DocumentPreviewHttpSessionBinding
{
    internal static bool TryCreate(HttpContext context, ICurrentTenant tenant, out SessionBindingSnapshot binding)
    {
        binding = default!;
        var principal = context.User;
        if (!tenant.IsAvailable || !tenant.IsHost || tenant.Id is not null || principal.Identity?.IsAuthenticated != true
            || principal.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.ApiKeyId)
            || !Guid.TryParse(principal.FindFirstValue("sub"), out var userId) || userId == Guid.Empty)
            return false;
        var kind = SessionBindingKinds.Refresh;
        Guid sessionId;
        if (principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse) == "access"
            && Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId), out sessionId))
            kind = SessionBindingKinds.OidcApplication;
        else if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId), out sessionId)) return false;
        var stamp = principal.FindFirstValue(FullNetIdentityClaimTypes.SecurityStamp);
        var actorScope = principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope);
        var effectiveScope = principal.FindFirstValue(FullNetIdentityClaimTypes.Scope);
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(stamp) || actorScope != "host" || effectiveScope != "host") return false;
        binding = new(userId, null, sessionId, stamp, actorScope, effectiveScope, kind);
        return true;
    }
}
