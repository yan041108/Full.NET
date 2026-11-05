using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>只读查询本人 Agent 运行；跨租户与伪造标识均失败关闭。</summary>
internal sealed class AiAgentRunQueryService(AiAgentRunStore store, ICurrentTenant tenant,
    IQueryExecutor queries, IOptions<DatabaseOptions> database)
{
    /// <summary>仅在当前租户内先按本人筛选再分页；游标绑定主体和筛选，不暴露运行输入及会话快照。</summary>
    internal async Task<Result<CursorPage<AiAgentRunResponse>>> ListMineAsync(Guid actorUserId, int pageSize,
        string? statusKey, string? cursor, CancellationToken cancellationToken = default)
    {
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is not { } tenantId || actorUserId == Guid.Empty)
            return Result<CursorPage<AiAgentRunResponse>>.Failure(new(CommonErrorCodes.PermissionDenied,
                "Tenant context and an authenticated user are required.", ErrorType.Forbidden));
        var status = string.IsNullOrWhiteSpace(statusKey) ? null : statusKey.Trim();
        if (status is not null && status is not ("queued" or "running" or "completed" or "failed" or "cancelled"
            or "expired" or "awaiting_approval" or "authorization_required" or "retry_scheduled" or "reconciliation_required"))
            return Result<CursorPage<AiAgentRunResponse>>.Failure(InvalidQuery());
        var context = $"ai.agent_run.mine.v1|{tenantId:N}|{actorUserId:N}|{status}";
        TimeIdQueryPosition? before = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TimeIdQueryCursor.TryDecode(cursor, context, out var position))
                return Result<CursorPage<AiAgentRunResponse>>.Failure(InvalidQuery());
            before = position;
        }
        pageSize = Math.Clamp(pageSize, 1, 100);
        var sql = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => before is null ? AiAgentRunMineSql.FirstSqlServer : AiAgentRunMineSql.AfterSqlServer,
            DatabaseProvider.MySql => before is null ? AiAgentRunMineSql.FirstMySql : AiAgentRunMineSql.AfterMySql,
            _ => throw new InvalidOperationException("Unsupported database provider.")
        };
        var rows = await queries.QueryAsync<AiAgentRunResponse>(sql, AiSqlParameters.Create(
            ("ScopeKey", tenantId.ToString("N")), ("ActorUserId", actorUserId), ("StatusKey", status), ("Take", pageSize + 1),
            ("BeforeCreatedAtUtc", before?.CreatedAtUtc.UtcDateTime), ("BeforeId", before?.Id)), cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToArray();
        var next = hasMore ? TimeIdQueryCursor.Encode(new(items[^1].CreatedAtUtc, items[^1].Id), context) : null;
        return Result<CursorPage<AiAgentRunResponse>>.Success(new(items, next, hasMore));
    }

    private static Error InvalidQuery() => new(CommonErrorCodes.RequestInvalid,
        "The task query or cursor is invalid.", ErrorType.Validation);

    public async Task<Result<AiAgentRunResponse>> GetOwnedAsync(
        Guid runId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope();
        var record = runId == Guid.Empty || actorUserId == Guid.Empty ? null
            : await store.FindOwnedAsync(runId, scope, actorUserId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result<AiAgentRunResponse>.Failure(new Error(
                AiErrorCodes.AgentRunNotFound,
                "The agent run was not found.",
                ErrorType.NotFound));
        }

        return Result<AiAgentRunResponse>.Success(new(
            record.Id,
            record.StatusKey,
            record.DefinitionKey,
            record.DefinitionVersion,
            record.DeadlineAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc));
    }

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent runs.");
}
