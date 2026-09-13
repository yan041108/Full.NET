using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

/// <summary>所有者来自逐次授权主体，输入参数没有用户或租户字段。</summary>
internal sealed class ListChatSessionsToolHandler(AiChatSessionQueryService queries, ICurrentTenant tenant) : IAgentToolHandler
{
    public bool ValidateArguments(JsonElement arguments) => ToolArguments.Parse(arguments) is not null;
    public async ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
    {
        var page = ToolArguments.Parse(invocation.Arguments) ?? throw new InvalidOperationException("Invalid tool arguments.");
        if (actor.UserId == Guid.Empty || actor.TenantId != tenant.Id || (actor.TenantId is null && !tenant.IsHost))
            throw new InvalidOperationException("Tool scope mismatch.");
        var result = await queries.ListAsync(actor.UserId, page.Page, page.PageSize, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidOperationException("Tool query failed.");
        return JsonSerializer.SerializeToElement(result.Value, AiToolJsonSerializerContext.Default.PagedResultAiChatSessionListItem);
    }
}
