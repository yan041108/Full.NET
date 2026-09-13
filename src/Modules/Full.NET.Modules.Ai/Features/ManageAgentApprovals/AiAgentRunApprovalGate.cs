using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>恢复门禁只读查询；不依赖工具目录，避免 Agent Run HTTP 路径同步阻塞工具注册。</summary>
internal sealed class AiAgentRunApprovalGate(
    IQueryExecutor queries,
    ICurrentTenant tenant,
    IClock clock)
{
    public async ValueTask<bool> HasApprovedUnconsumedForRunAsync(
        Guid runId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var count = await queries.QuerySingleOrDefaultAsync<int>(
            AiAgentApprovalSql.CountApprovedUnconsumedForRun,
            AiSqlParameters.Create(
                ("RunId", runId),
                ("ScopeKey", ResolveScope()),
                ("ActorUserId", actorUserId),
                ("Now", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);
        return count > 0;
    }

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent approvals.");
}
