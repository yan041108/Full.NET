using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>从已认证 HTTP 请求提取冻结会话绑定；不接受请求体覆盖主体字段。</summary>
internal static class AgentRunHttpBinding
{
    public static bool TryCreate(HttpContext httpContext, Guid? tenantId, out SessionBindingSnapshot binding)
    {
        binding = default!;
        var principal = httpContext.User;
        if (principal.Identity?.IsAuthenticated != true
            || principal.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.ApiKeyId)
            || !Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId), out var sessionId))
        {
            return false;
        }

        var securityStamp = principal.FindFirstValue(FullNetIdentityClaimTypes.SecurityStamp);
        var actorScope = principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope);
        var effectiveScope = principal.FindFirstValue(FullNetIdentityClaimTypes.Scope);
        if (string.IsNullOrWhiteSpace(securityStamp) || string.IsNullOrWhiteSpace(actorScope) || string.IsNullOrWhiteSpace(effectiveScope))
        {
            return false;
        }

        binding = new(userId, tenantId, sessionId, securityStamp, actorScope, effectiveScope);
        return true;
    }
}
