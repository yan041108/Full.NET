using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using static Full.NET.Modules.Ai.Features.ManageKnowledgeVersions.AiKnowledgeImportStore;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>当前读取者独立授权；完成状态须复核当前完整性，不消费原会话或触发模型。</summary>
internal sealed class AiKnowledgeIndexStatusService(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, AiKnowledgeIndexActivationStore activation, IOptions<DatabaseOptions> database)
{
    internal static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.IndexRead];

    internal async Task<Result<AiKnowledgeIndexStatusResponse>> ReadAsync(Guid knowledgeBaseId, Guid documentId, Guid versionId, Guid actor, CancellationToken token)
    {
        Check(token); var tenantId = tenant.Id; var isTenant = AiKnowledgeScope.IsTenant(tenant);
        if (actor == Guid.Empty || tenantId == Guid.Empty || isTenant != tenantId.HasValue) return Denied();
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty) return NotFound<AiKnowledgeIndexStatusResponse>();
        var first = await Load().ConfigureAwait(false);
        if (!first.IsSuccess) return Result<AiKnowledgeIndexStatusResponse>.Failure(first.Error!);
        // 初次数据库等待后重新读取权威权限和关系，响应不用等待前的授权快照。
        var final = await Load().ConfigureAwait(false);
        if (!final.IsSuccess) return Result<AiKnowledgeIndexStatusResponse>.Failure(final.Error!);
        var snapshot = final.Value!; var row = snapshot.Row;
        if (row.IsDeleted == 1) return Success(row, row.CleanupPending == 1 || row.HasDerivedData == 1 ? "deleting" : "deleted");
        if (row.TaskId is null) return Success(row, "pending_processing");
        if (row.IsCurrent != 1) return Success(row, "failed");
        if (row.StateKey is "queued" or "running") return Success(row, row.StateKey == "queued" ? "queued" : "processing");
        if (row.StateKey != "completed" || row.ActiveTaskId != row.TaskId || row.ActiveLeaseToken is not { } lease || lease == Guid.Empty) return Success(row, "failed");
        var ready = await activation.IsReadyForReadAsync(row, snapshot.Parameters, token).ConfigureAwait(false);
        Check(token); if (!SameScope()) return Denied();
        // 证明等待后重新读取当前权限与角色/组织关系，成功与失败都不复用等待前的授权。
        var after = await Load().ConfigureAwait(false);
        if (!after.IsSuccess) return Result<AiKnowledgeIndexStatusResponse>.Failure(after.Error!);
        return after.Value!.Row == row ? Success(row, ready ? "ready" : "failed") : Failure<AiKnowledgeIndexStatusResponse>(AiKnowledgeErrorCodes.IndexConflict,
            "The index state changed while it was being read.", ErrorType.Conflict);

        bool SameScope() => tenant.Id == tenantId && AiKnowledgeScope.IsTenant(tenant) == isTenant;
        async Task<Result<Snapshot>> Load()
        {
            Check(token);
            if (!SameScope() || !await permissions.IsAuthorizedAsync(Permissions, actor, token).ConfigureAwait(false)) return Forbidden<Snapshot>();
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            var p = await access.ParametersAsync(actor, token).ConfigureAwait(false);
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            p["KnowledgeBaseId"] = knowledgeBaseId; p["DocumentId"] = documentId; p["VersionId"] = versionId;
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.For(database.Value.Provider, isTenant), p, token).ConfigureAwait(false);
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            return current is null || current.KnowledgeBaseId != knowledgeBaseId || current.DocumentId != documentId || current.VersionId != versionId
                || current.OwnerUserId == Guid.Empty || current.DocumentVersion < 1 || current.KnowledgeBaseVersion < 1
                || current.IsDeleted is < 0 or > 1 || current.CleanupPending is < 0 or > 1 || current.HasDerivedData is < 0 or > 1 || current.IsCurrent is < 0 or > 1
                ? NotFound<Snapshot>() : Result<Snapshot>.Success(new(current, p));
        }
    }

    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Index status cannot join an ambient transaction."); }
    private static Result<AiKnowledgeIndexStatusResponse> Success(AiKnowledgeIndexStatusRecord row, string state) =>
        Result<AiKnowledgeIndexStatusResponse>.Success(new(row.VersionId, row.TaskId, state, state == "failed" ? "ai.knowledge.index_unavailable" : null));
    private static Result<T> Forbidden<T>() => Failure<T>("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
    private static Result<AiKnowledgeIndexStatusResponse> Denied() => Forbidden<AiKnowledgeIndexStatusResponse>();
    private sealed record Snapshot(AiKnowledgeIndexStatusRecord Row, Dictionary<string, object?> Parameters);
}
