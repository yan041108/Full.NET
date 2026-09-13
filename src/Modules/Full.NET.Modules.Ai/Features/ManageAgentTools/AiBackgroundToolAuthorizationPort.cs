using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>Worker 路径按运行绑定授权；无绑定时不冒充交互会话。</summary>
internal sealed class AiBackgroundToolAuthorizationPort(
    IAgentRunExecutionContext executionContext,
    IBackgroundSessionAuthorization authorization) : IToolAuthorizationPort
{
    public async ValueTask<ToolActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken)
    {
        var binding = executionContext.Binding
            ?? throw new InvalidOperationException("Agent run execution binding is required for background tool authorization.");
        var actor = await authorization.AuthorizeAsync(binding, permissionCode, cancellationToken).ConfigureAwait(false);
        return actor is null ? null : new(actor.UserId, actor.TenantId, actor.SessionId);
    }
}
