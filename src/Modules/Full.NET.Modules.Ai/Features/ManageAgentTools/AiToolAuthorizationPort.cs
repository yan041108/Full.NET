using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>Identity 拥有身份与权限的权威读取，Ai 不跨模块查询身份表。</summary>
internal sealed class AiToolAuthorizationPort(ICurrentSessionAuthorization authorization) : IToolAuthorizationPort
{
    /// <inheritdoc />
    public async ValueTask<ToolActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken)
    {
        var actor = await authorization.AuthorizeAsync(permissionCode, cancellationToken).ConfigureAwait(false);
        return actor is null ? null : new(actor.UserId, actor.TenantId, actor.SessionId);
    }
}
