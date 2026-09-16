using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>从已认证 HTTP 请求提取冻结会话绑定；不接受请求体覆盖主体字段。</summary>
internal static class AgentRunHttpBinding
{
    private const string OidcAccessTokenUse = "access";

    public static bool TryCreate(HttpContext httpContext, Guid? tenantId, out SessionBindingSnapshot binding)
    {
        binding = default!;
        var principal = httpContext.User;
        if (principal.Identity?.IsAuthenticated != true
            || principal.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.ApiKeyId)
            || !Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
            || !TryReadSessionId(principal, out var sessionId, out var sessionKind))
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

        binding = new(userId, tenantId, sessionId, securityStamp, actorScope, effectiveScope, sessionKind);
        return true;
    }

    private static bool TryReadSessionId(
        ClaimsPrincipal principal,
        out Guid sessionId,
        out string sessionKind)
    {
        sessionKind = SessionBindingKinds.Refresh;
        if (Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId), out sessionId)
            && string.Equals(
                principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse),
                OidcAccessTokenUse,
                StringComparison.Ordinal))
        {
            sessionKind = SessionBindingKinds.OidcApplication;
            return true;
        }

        return Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId), out sessionId);
    }
}
