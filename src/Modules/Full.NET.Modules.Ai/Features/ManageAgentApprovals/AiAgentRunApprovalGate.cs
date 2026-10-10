using System.Text;
using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>恢复前有界复核完整审批、当前业务目标和精确权限，不消费审批或构造工具目录。</summary>
internal sealed class AiAgentRunApprovalGate(
    IQueryExecutor queries,
    ICurrentTenant tenant,
    IClock clock,
    IToolAuthorizationPort authorization,
    IOptions<DatabaseOptions> databaseOptions)
{
    private const int MaximumCandidates = 32;

    public async ValueTask<bool> HasApprovedUnconsumedForRunAsync(
        Guid runId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (runId == Guid.Empty || actorUserId == Guid.Empty) return false;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AiAgentApprovalSql.ListApprovedUnconsumedForRunSqlServer,
            DatabaseProvider.MySql => AiAgentApprovalSql.ListApprovedUnconsumedForRunMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
        var scope = ResolveScope();
        var candidates = await queries.QueryAsync<AiAgentApprovalRecord>(statement, AiSqlParameters.Create(
            ("RunId", runId), ("ScopeKey", scope), ("ActorUserId", actorUserId),
            ("Now", clock.UtcNow), ("PageSize", MaximumCandidates)), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // 单次恢复最多复核 32 条；无法在该窗口找到可执行审批时失败关闭。
        foreach (var record in candidates.Take(MaximumCandidates))
        {
            if (record.RunId != runId || record.RequestedBy != actorUserId
                || record.ArgumentsProtected is null || Encoding.UTF8.GetByteCount(record.ArgumentsProtected) > 16384) continue;
            JsonDocument arguments;
            try { arguments = JsonDocument.Parse(record.ArgumentsProtected); }
            catch (JsonException) { continue; }
            using (arguments)
            {
                var invocation = new ToolInvocation(record.OperationId, runId, record.ToolName, record.ToolVersion, arguments.RootElement);
                var actor = new ToolActor(actorUserId, tenant.Id, record.SessionId);
                if (!AiAgentApprovalConsumption.Matches(record, invocation, actor, tenant, clock.UtcNow)
                    || !await AiAgentApprovalConsumption.TargetMatchesAsync(queries, record, invocation, actor, tenant, cancellationToken).ConfigureAwait(false)) continue;
                var current = await authorization.AuthorizeAsync(AiChatPermissions.Update, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (current == actor && ResolveScope() == scope
                    && AiAgentApprovalConsumption.Matches(record, invocation, actor, tenant, clock.UtcNow)) return true;
            }
        }
        return false;
    }

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent approvals.");
}
