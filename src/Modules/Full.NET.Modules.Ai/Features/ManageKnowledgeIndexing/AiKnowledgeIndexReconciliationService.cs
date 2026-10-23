using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using static Full.NET.Modules.Ai.Features.ManageKnowledgeVersions.AiKnowledgeImportStore;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>只读对账入口；所有者、当前范围及完整证明共同约束恢复提示。</summary>
internal sealed class AiKnowledgeIndexReconciliationService(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, AiKnowledgeIndexStatusService statuses,
    IClock clock, IOptions<DatabaseOptions> database, IOptions<AiKnowledgePdfRuntimeOptions> pdf, IOptions<AiKnowledgeDocxRuntimeOptions>? docx = null,
    Microsoft.Extensions.Options.IOptions<AiKnowledgeDocumentRuntimeOptions>? documents = null)
{
    internal async Task<Result<AiKnowledgeIndexReconciliationResponse>> ReadAsync(Guid knowledgeBaseId, Guid documentId,
        Guid versionId, Guid actor, CancellationToken token)
    {
        Check(token); var tenantId = tenant.Id; var isTenant = AiKnowledgeScope.IsTenant(tenant);
        if (actor == Guid.Empty || tenantId == Guid.Empty || isTenant != tenantId.HasValue) return Forbidden<AiKnowledgeIndexReconciliationResponse>();
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty) return NotFound<AiKnowledgeIndexReconciliationResponse>();
        var first = await LoadAsync().ConfigureAwait(false);
        if (!first.IsSuccess) return Result<AiKnowledgeIndexReconciliationResponse>.Failure(first.Error!);
        // 复用 TXT/PDF 完整证明；任务状态、活动指针或恢复提示都不能单独提升 ready。
        var status = await statuses.ReadAsync(knowledgeBaseId, documentId, versionId, actor, token).ConfigureAwait(false);
        if (!status.IsSuccess) return Result<AiKnowledgeIndexReconciliationResponse>.Failure(status.Error!);
        var after = await LoadAsync().ConfigureAwait(false);
        if (!after.IsSuccess) return Result<AiKnowledgeIndexReconciliationResponse>.Failure(after.Error!);
        var snapshot = after.Value!; var row = snapshot.Row;
        if (first.Value != snapshot || status.Value!.VersionId != row.VersionId || status.Value.TaskId != row.TaskId)
            return Conflict<AiKnowledgeIndexReconciliationResponse>();
        var source = snapshot.Source; var task = snapshot.Task;
        var bindingCurrent = task is null || source is not null && task.DocumentVersion == source.DocumentVersion
            && task.KnowledgeBaseVersion == source.KnowledgeBaseVersion && task.ModelConfigId == source.ModelConfigId
            && task.ModelConfigVersion == source.ModelConfigVersion;
        var decision = AiKnowledgeIndexReconciliationPolicy.Decide(status.Value.State, task?.StateKey, source is not null,
            bindingCurrent, snapshot.FormatSupported, task?.AttemptCount ?? 0, task?.LeaseExpiresAtUtc, clock.UtcNow, row.IsCurrent == 1);
        // 提示不执行修复，也不复用原任务会话；后续写入必须重新授权并通过预算和幂等门禁。
        return Result<AiKnowledgeIndexReconciliationResponse>.Success(new(row.VersionId, row.TaskId, status.Value.State,
            decision.Reason, decision.Action, row.DocumentVersion, row.KnowledgeBaseVersion));

        bool SameScope() => tenant.Id == tenantId && AiKnowledgeScope.IsTenant(tenant) == isTenant;
        async Task<Result<Snapshot>> LoadAsync()
        {
            Check(token);
            if (!SameScope() || !await permissions.IsAuthorizedAsync(AiKnowledgeIndexStatusService.Permissions, actor, token).ConfigureAwait(false)) return Forbidden<Snapshot>();
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            var p = await access.ParametersAsync(actor, token).ConfigureAwait(false);
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            p["KnowledgeBaseId"] = knowledgeBaseId; p["DocumentId"] = documentId; p["VersionId"] = versionId;
            var before = await ReadMetadataAsync(p).ConfigureAwait(false);
            if (!before.IsSuccess) return before;
            // 权限等待可能跨过任务续租或来源修改；权限复核之后再比较完整元数据，不发布旧建议。
            if (!await permissions.IsAuthorizedAsync(AiKnowledgeIndexStatusService.Permissions, actor, token).ConfigureAwait(false)) return Forbidden<Snapshot>();
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            var final = await ReadMetadataAsync(p).ConfigureAwait(false);
            return !final.IsSuccess || before.Value == final.Value ? final : Conflict<Snapshot>();
        }

        async Task<Result<Snapshot>> ReadMetadataAsync(Dictionary<string, object?> p)
        {
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.For(database.Value.Provider, isTenant), p, token).ConfigureAwait(false);
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            if (current is null || current.KnowledgeBaseId != knowledgeBaseId || current.DocumentId != documentId || current.VersionId != versionId
                || current.OwnerUserId != actor || current.DocumentVersion < 1 || current.KnowledgeBaseVersion < 1
                || current.IsDeleted is < 0 or > 1 || current.CleanupPending is < 0 or > 1 || current.HasDerivedData is < 0 or > 1 || current.IsCurrent is < 0 or > 1)
                return NotFound<Snapshot>();
            var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexEnqueueRecord>(isTenant ? AiKnowledgeIndexEnqueueSql.ReconciliationSourceTenant : AiKnowledgeIndexEnqueueSql.ReconciliationSourceHost, p, token).ConfigureAwait(false);
            Check(token); if (!SameScope()) return Forbidden<Snapshot>();
            if (source is not null && (source.KnowledgeBaseId != knowledgeBaseId || source.DocumentId != documentId || source.VersionId != versionId
                || source.OwnerUserId != actor || source.DocumentVersion != current.DocumentVersion || source.KnowledgeBaseVersion != current.KnowledgeBaseVersion
                || source.ModelConfigId == Guid.Empty || source.ModelConfigVersion < 1 || source.ContentType != current.ContentType)) return Conflict<Snapshot>();
            TaskSnapshot? task = null;
            if (current.TaskId is { } taskId)
            {
                p["TaskId"] = taskId;
                var record = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTaskRecord>(isTenant ? AiKnowledgeIndexTaskSql.FindTenant : AiKnowledgeIndexTaskSql.FindHost, p, token).ConfigureAwait(false);
                Check(token); if (!SameScope()) return Forbidden<Snapshot>();
                if (record is null || record.Id != taskId || record.DocumentId != documentId || record.VersionId != versionId || record.StateKey != current.StateKey
                    || record.DocumentVersion < 1 || record.KnowledgeBaseVersion < 1 || record.ModelConfigId == Guid.Empty || record.ModelConfigVersion < 1
                    || record.AttemptCount is < 0 or > 3 || record.StateKey is not ("queued" or "running" or "completed" or "failed" or "cancelled")) return Conflict<Snapshot>();
                task = new(record.Id, record.DocumentVersion, record.KnowledgeBaseVersion, record.ModelConfigId, record.ModelConfigVersion,
                    record.StateKey, record.AttemptCount, record.FailureCode, record.LeaseToken, record.LeaseExpiresAtUtc, record.CreatedAtUtc, record.UpdatedAtUtc);
            }
            return Result<Snapshot>.Success(new(current, source, task, documents?.Value.Supports(current.ContentType) == true || pdf.Value.Supports(current.ContentType) || (docx?.Value.Enabled == true && current.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType)));
        }
    }

    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Index reconciliation cannot join an ambient transaction."); }
    private static Result<T> Forbidden<T>() => Failure<T>("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
    private static Result<T> Conflict<T>() => Failure<T>(AiKnowledgeErrorCodes.IndexConflict, "The index state changed while it was being read.", ErrorType.Conflict);
    private sealed record Snapshot(AiKnowledgeIndexStatusRecord Row, AiKnowledgeIndexEnqueueRecord? Source, TaskSnapshot? Task, bool FormatSupported);
    private sealed record TaskSnapshot(Guid Id, int DocumentVersion, int KnowledgeBaseVersion, Guid ModelConfigId, int ModelConfigVersion,
        string StateKey, int AttemptCount, string? FailureCode, Guid? LeaseToken, DateTimeOffset? LeaseExpiresAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
}
