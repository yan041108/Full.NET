using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Approvals;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>审批绑定查询与消费；不依赖工具目录，避免 AgentToolRegistry 构造环。</summary>
internal sealed class AiAgentApprovalConsumption(
    IQueryExecutor queries,
    ICommandExecutor commands,
    IClock clock,
    ICurrentTenant tenant,
    IToolAuthorizationPort authorization)
{
    internal async ValueTask<AgentApprovalBinding?> FindBindingByOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var record = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(
            AiAgentApprovalSql.FindByOperation,
            AiSqlParameters.Create(("OperationId", operationId)),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return record is null ? null : ToBinding(record);
    }

    internal async ValueTask<bool> TryConsumeAsync(Guid approvalId, long expectedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var affected = await commands.ExecuteAsync(
            AiAgentApprovalSql.Consume,
            AiSqlParameters.Create(("Id", approvalId), ("ExpectedVersion", expectedVersion), ("Now", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);
        // 消费已在调用方事务中执行；取消后抛出以回滚，不能继续写入目标。
        cancellationToken.ThrowIfCancellationRequested();
        return affected == 1;
    }

    internal async ValueTask<bool> TryConsumeForToolExecutionAsync(
        ToolInvocation invocation,
        ToolActor actor,
        CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(invocation.OperationId, cancellationToken).ConfigureAwait(false);
        if (record is null || !Matches(record, invocation, actor, tenant, clock.UtcNow)) return false;
        // 事务内只复核 AI 自有记录，跨模块权限已在入场阶段检查。
        if (!await TargetMatchesAsync(queries, record, invocation, actor, tenant, cancellationToken).ConfigureAwait(false)
            || !Matches(record, invocation, actor, tenant, clock.UtcNow)) return false;
        return await TryConsumeAsync(record.Id, record.Version, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>只读入场复核；授权发生在调用方开启本地事务之前。</summary>
    internal async ValueTask<AgentApprovalExecutionStatus> ValidateForToolExecutionAsync(
        ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(invocation.OperationId, cancellationToken).ConfigureAwait(false);
        if (record is null) return AgentApprovalExecutionStatus.Required;
        if (!Matches(record, invocation, actor, tenant, clock.UtcNow)
            || !await TargetMatchesAsync(queries, record, invocation, actor, tenant, cancellationToken).ConfigureAwait(false))
            return AgentApprovalExecutionStatus.Denied;
        var current = await authorization.AuthorizeAsync(AiChatPermissions.Update, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return current == actor && Matches(record, invocation, actor, tenant, clock.UtcNow)
            ? AgentApprovalExecutionStatus.Approved : AgentApprovalExecutionStatus.Denied;
    }

    private async ValueTask<AiAgentApprovalRecord?> FindRecordAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var record = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
            AiSqlParameters.Create(("OperationId", operationId)), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return record;
    }

    /// <summary>恢复与消费使用相同静态工具策略，不构造依赖审批端口的工具目录。</summary>
    internal static bool Matches(AiAgentApprovalRecord record, ToolInvocation invocation, ToolActor actor,
        ICurrentTenant tenant, DateTimeOffset now) =>
        record.Id != Guid.Empty && record.Version > 0 && record.RunId != Guid.Empty && record.OperationId != Guid.Empty
        && actor.UserId != Guid.Empty && actor.SessionId != Guid.Empty && record.SessionId == actor.SessionId
        && actor.TenantId == tenant.Id && (tenant.Id is not null || tenant.IsHost)
        && record.ScopeKey == (tenant.Id is { } id ? id.ToString("N") : "host")
        && invocation.ToolName == RenameChatSessionToolHandler.ToolName
        && invocation.ToolVersion == RenameChatSessionToolHandler.ToolVersion
        && RenameChatSessionArgumentParser.Parse(invocation.Arguments) is not null
        && AgentApprovalGate.TryValidateForConsume(ToBinding(record), invocation.OperationId, invocation.RunId,
            invocation.ToolName, invocation.ToolVersion, invocation.Arguments, actor.UserId, actor.TenantId, now, out _);

    internal static async ValueTask<bool> TargetMatchesAsync(IQueryExecutor queries, AiAgentApprovalRecord record,
        ToolInvocation invocation, ToolActor actor, ICurrentTenant tenant, CancellationToken cancellationToken)
    {
        var parsed = RenameChatSessionArgumentParser.Parse(invocation.Arguments);
        if (parsed is null || string.IsNullOrWhiteSpace(record.PresentationJson)) return false;
        AiAgentApprovalPresentation? presentation;
        try
        {
            using var document = JsonDocument.Parse(record.PresentationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            // 含重复字段的快照存在歧义，不允许用最后一个值覆盖批准事实。
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) return false;
            presentation = JsonSerializer.Deserialize(document.RootElement, AiJsonSerializerContext.Default.AiAgentApprovalPresentation);
        }
        catch (JsonException) { return false; }
        if (presentation is null || presentation.Target is null || presentation.Change != parsed.Title
            || presentation.TargetVersion is not > 0) return false;
        var target = await queries.QuerySingleOrDefaultAsync<AiChatSessionRecord>(AiChatSql.FindSessionById,
            AiChatSessionQueryService.BuildScopeParameters(AiChatScope.Resolve(tenant), actor.UserId, ("SessionId", parsed.SessionId)),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // 标题恢复原值也不能恢复旧批准；目标版本与业务值同时匹配，写入仍须通过版本 CAS。
        return target is not null && target.Id == parsed.SessionId && target.OwnerUserId == actor.UserId
            && target.TenantId == actor.TenantId && target.Title == presentation.Target
            && target.Version == presentation.TargetVersion;
    }

    internal static AgentApprovalBinding ToBinding(AiAgentApprovalRecord record) => new(
        record.Id,
        record.RunId,
        record.OperationId,
        record.TenantId,
        record.ToolName,
        record.ToolVersion,
        record.ArgumentsHash,
        record.PolicyVersion,
        record.RequestedBy,
        record.DecisionKey,
        record.ExpiresAtUtc,
        record.ConsumedAtUtc,
        record.Version);
}
