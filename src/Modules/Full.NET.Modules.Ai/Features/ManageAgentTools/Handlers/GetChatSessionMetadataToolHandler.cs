using System.Globalization;
using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

/// <summary>只读当前主体指定会话的元数据；不向并行评审暴露其他会话或历史正文。</summary>
internal sealed class GetChatSessionMetadataToolHandler(AiChatSessionQueryService queries, ICurrentTenant tenant) : IAgentToolHandler
{
    internal const string ToolName = "ai.chat.sessions.metadata";
    public bool ValidateArguments(JsonElement arguments) => arguments.ValueKind == JsonValueKind.Object
        && arguments.EnumerateObject().Count() == 1 && arguments.TryGetProperty("sessionId", out var id)
        && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var value) && value != Guid.Empty;
    public async ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
    {
        if (!ValidateArguments(invocation.Arguments) || actor.UserId == Guid.Empty || actor.TenantId != tenant.Id || (!tenant.IsHost && !tenant.IsAvailable))
            throw new InvalidOperationException("Chat metadata scope is unavailable.");
        var id = invocation.Arguments.GetProperty("sessionId").GetGuid();
        var row = await queries.FindOwnedSessionAsync(AiChatScope.Resolve(tenant), id, actor.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The owned target chat session is unavailable.");
        return JsonSerializer.SerializeToElement(new WorkflowChatSessionMetadata(row.Id, row.Title, row.Version.ToString(CultureInfo.InvariantCulture)),
            AiToolJsonSerializerContext.Default.WorkflowChatSessionMetadata);
    }
}

/// <summary>模型只看到身份、标题及无损版本；不含主体、租户、凭据或消息正文。</summary>
internal sealed record WorkflowChatSessionMetadata(Guid SessionId, string Title, string Version);
