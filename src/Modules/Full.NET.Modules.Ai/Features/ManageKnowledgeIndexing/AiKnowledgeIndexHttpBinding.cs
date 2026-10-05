using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>入队只允许认证处理器建立的交互原会话，可信租户来自宿主上下文。</summary>
internal static class AiKnowledgeIndexHttpBinding
{
    internal static bool TryCreate(HttpContext context, Guid? tenantId, out SessionBindingSnapshot binding)
    {
        binding = default!;
        if (tenantId == Guid.Empty || context.User.Identities.Any(identity => identity.AuthenticationType is "FullNET.ApiKey" or "FullNET.Signature")) return false;
        // OIDC 声明一旦出现就必须完整有效，不能降级为请求中另一个 Refresh 会话。
        var applicationSession = context.User.FindFirst(FullNetIdentityClaimTypes.ApplicationSessionId);
        if (applicationSession is not null && (!Guid.TryParse(applicationSession.Value, out var id) || id == Guid.Empty
            || context.User.FindFirst(FullNetIdentityClaimTypes.TokenUse)?.Value != "access")) return false;
        if (!AgentRunHttpBinding.TryCreate(context, tenantId, out var session) || session.UserId == Guid.Empty || session.SessionId == Guid.Empty) return false;
        binding = session; return true;
    }
}
