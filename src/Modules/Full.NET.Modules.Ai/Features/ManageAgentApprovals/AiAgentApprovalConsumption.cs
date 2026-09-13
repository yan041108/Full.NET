using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Approvals;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>审批绑定查询与消费；不依赖工具目录，避免 AgentToolRegistry 构造环。</summary>
internal sealed class AiAgentApprovalConsumption(
    IQueryExecutor queries,
    ICommandExecutor commands,
    IClock clock)
{
    internal async ValueTask<AgentApprovalBinding?> FindBindingByOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var record = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(
            AiAgentApprovalSql.FindByOperation,
            AiSqlParameters.Create(("OperationId", operationId)),
            cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToBinding(record);
    }

    internal async ValueTask<bool> TryConsumeAsync(Guid approvalId, long expectedVersion, CancellationToken cancellationToken)
    {
        var affected = await commands.ExecuteAsync(
            AiAgentApprovalSql.Consume,
            AiSqlParameters.Create(("Id", approvalId), ("ExpectedVersion", expectedVersion), ("Now", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    internal async ValueTask<bool> TryConsumeForToolExecutionAsync(
        ToolInvocation invocation,
        ToolActor actor,
        CancellationToken cancellationToken)
    {
        var binding = await FindBindingByOperationAsync(invocation.OperationId, cancellationToken).ConfigureAwait(false);
        if (binding is null
            || !AgentApprovalGate.TryValidateForConsume(
                binding,
                invocation.OperationId,
                invocation.RunId,
                invocation.ToolName ?? string.Empty,
                invocation.ToolVersion,
                invocation.Arguments,
                actor.UserId,
                actor.TenantId,
                clock.UtcNow,
                out _))
        {
            return false;
        }

        return await TryConsumeAsync(binding.Id, binding.Version, cancellationToken).ConfigureAwait(false);
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
