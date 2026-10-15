using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>显式创建独立索引代次；不接管旧会话、不改写旧费用、不在事务中调用其他模块。</summary>
internal sealed class AiKnowledgeIndexRebuildService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IBackgroundSessionAuthorization authorization,
    IOptions<AiKnowledgeIndexRebuildOptions> options, IOptions<DatabaseOptions> database,
    IOptions<AiKnowledgePdfRuntimeOptions> pdf, IOptions<AiKnowledgeDocxRuntimeOptions> docx)
{
    internal static readonly string[] Permissions = [.. AiKnowledgeIndexEnqueueService.Permissions,
        AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRebuild];

    /// <summary>202 只确认幂等绑定；后续 Worker 仍须复核原身份、来源、批准和预算。</summary>
    internal async Task<Result<AiKnowledgeIndexEnqueueResponse>> RebuildAsync(Guid knowledgeBaseId, Guid documentId, Guid versionId,
        AiKnowledgeIndexRebuildRequest request, SessionBindingSnapshot original, CancellationToken token)
    {
        Check(token); ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(request);
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty || request.ClientRequestId == Guid.Empty
            || request.ExpectedTaskId == Guid.Empty || request.ExpectedDocumentVersion <= 0 || request.ExpectedKnowledgeBaseVersion <= 0
            || !request.ConfirmAdditionalCost)
            return Failure(AiKnowledgeErrorCodes.InputInvalid, "A confirmed rebuild request and current reconciliation snapshot are required.", ErrorType.Validation);
        if (!options.Value.Enabled) return Failure(AiKnowledgeErrorCodes.IndexRebuildDisabled, "Index rebuild is not enabled.", ErrorType.BusinessRule);
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        var p = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["VersionId"] = versionId, ["OwnerUserId"] = original.UserId,
            ["DocumentVersion"] = request.ExpectedDocumentVersion, ["KnowledgeBaseVersion"] = request.ExpectedKnowledgeBaseVersion,
            ["ExpectedTaskId"] = request.ExpectedTaskId, ["RebuildRequestId"] = request.ClientRequestId,
            ["SessionId"] = original.SessionId, ["SessionKind"] = original.SessionKind, ["SecurityStamp"] = original.SecurityStamp,
            ["ActorScope"] = original.ActorScope, ["EffectiveScope"] = original.EffectiveScope
        };
        var source = await ReadSourceAsync(p, token).ConfigureAwait(false);
        if (!ValidIdentity(original)) return Forbidden();
        if (source is null || source.KnowledgeBaseId != knowledgeBaseId || source.DocumentId != documentId
            || source.VersionId != versionId || source.OwnerUserId != original.UserId || source.ModelConfigId == Guid.Empty || source.ModelConfigVersion <= 0)
            return AiKnowledgeImportStore.NotFound<AiKnowledgeIndexEnqueueResponse>();
        if (source.DocumentVersion != request.ExpectedDocumentVersion || source.KnowledgeBaseVersion != request.ExpectedKnowledgeBaseVersion) return Conflict();
        if (!Supports(source.ContentType)) return Failure(AiKnowledgeErrorCodes.IndexFormatUnsupported, "The source format is not enabled for indexing.", ErrorType.BusinessRule);
        p["ModelConfigId"] = source.ModelConfigId; p["ModelConfigVersion"] = source.ModelConfigVersion;
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        Guid? accepted;
        try
        {
            accepted = await transaction.ExecuteAsync<Guid?>(async cancellation =>
            {
                cancellation.ThrowIfCancellationRequested(); if (!ValidIdentity(original) || !options.Value.Enabled || !Supports(source.ContentType)) return null;
                // 两种数据库都先锁定同一文档权威行，串行化代次分配与激活竞争；授权已在事务外完成。
                var locked = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexEnqueueRecord>(
                    AiKnowledgeIndexRebuildSql.LockSource(database.Value.Provider, IsTenant), p, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); if (locked != source || !ValidIdentity(original)) return null;
                var existing = await FindAcceptedAsync(p, original, cancellation).ConfigureAwait(false);
                if (existing.Exists) return existing.Id;
                p["TaskId"] = ids.NewId(); p["SessionRecordId"] = ids.NewId(); p["Now"] = clock.UtcNow;
                if (await command.ExecuteAsync(IsTenant ? AiKnowledgeIndexRebuildSql.CreateTenant : AiKnowledgeIndexRebuildSql.CreateHost, p, cancellation).ConfigureAwait(false) != 1) return null;
                // 首笔写入以后必须以异常回滚，禁止提交没有原会话的孤立任务。
                if (await command.ExecuteAsync(IsTenant ? AiKnowledgeIndexSessionSql.InsertTenant : AiKnowledgeIndexSessionSql.InsertHost, p, cancellation).ConfigureAwait(false) != 1)
                    throw new RebuildRejectedException();
                cancellation.ThrowIfCancellationRequested(); return (Guid)p["TaskId"]!;
            }, token).ConfigureAwait(false);
        }
        catch (RebuildRejectedException) { return Conflict(); }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 唯一冲突必须先由事务边界回滚，再按完整请求及冻结身份读取；不能认领另一个请求。
            accepted = (await FindAcceptedAsync(p, original, token).ConfigureAwait(false)).Id;
        }
        Check(token); if (!ValidIdentity(original)) return Forbidden();
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        // 最后一次跨模块授权等待之后再读取来源；已经保存的幂等绑定仍保留，不伪造成功或补发。
        if (accepted is null || accepted == Guid.Empty || await ReadSourceAsync(p, token).ConfigureAwait(false) != source
            || !options.Value.Enabled || !Supports(source.ContentType)) return Conflict();
        if (!ValidIdentity(original)) return Forbidden();
        return Result<AiKnowledgeIndexEnqueueResponse>.Success(new(accepted.Value, versionId));
    }

    private async Task<(bool Exists, Guid? Id)> FindAcceptedAsync(Dictionary<string, object?> p, SessionBindingSnapshot original, CancellationToken token)
    {
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTaskRecord>(IsTenant ? AiKnowledgeIndexRebuildSql.RequestTenant : AiKnowledgeIndexRebuildSql.RequestHost, p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (row is null) return (false, null);
        if (row.Id == Guid.Empty || row.DocumentId != (Guid)p["DocumentId"]! || row.VersionId != (Guid)p["VersionId"]!
            || row.DocumentVersion != (int)p["DocumentVersion"]! || row.KnowledgeBaseVersion != (int)p["KnowledgeBaseVersion"]!
            || row.ModelConfigId != (Guid)p["ModelConfigId"]! || row.ModelConfigVersion != (int)p["ModelConfigVersion"]!) return (true, null);
        p["TaskId"] = row.Id;
        var frozen = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(IsTenant ? AiKnowledgeIndexRebuildSql.SessionTenant : AiKnowledgeIndexRebuildSql.SessionHost, p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var matches = frozen is not null && frozen.Id != Guid.Empty && frozen.IndexTaskId == row.Id && ValidIdentity(original)
            && new SessionBindingSnapshot(frozen.ActorUserId, frozen.TenantId, frozen.SessionId, frozen.SecurityStamp,
                frozen.ActorScope, frozen.EffectiveScope, frozen.SessionKind) == original;
        return (true, matches ? row.Id : null);
    }
    private async Task<AiKnowledgeIndexEnqueueRecord?> ReadSourceAsync(Dictionary<string, object?> p, CancellationToken token)
    {
        Check(token);
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexEnqueueRecord>(IsTenant ? AiKnowledgeIndexEnqueueSql.ReconciliationSourceTenant : AiKnowledgeIndexEnqueueSql.ReconciliationSourceHost, p, token).ConfigureAwait(false);
        Check(token); return row;
    }
    private async Task<bool> AuthorizedAsync(SessionBindingSnapshot original, CancellationToken token)
    {
        foreach (var permission in Permissions)
        {
            Check(token); if (!ValidIdentity(original)) return false;
            var actor = await authorization.AuthorizeAsync(original, permission, token).ConfigureAwait(false);
            Check(token);
            if (!ValidIdentity(original) || actor is null || actor.UserId != original.UserId || actor.TenantId != original.TenantId || actor.SessionId != original.SessionId) return false;
        }
        return true;
    }
    private bool IsTenant => AiKnowledgeScope.IsTenant(tenant);
    private bool ValidIdentity(SessionBindingSnapshot session) => session.UserId != Guid.Empty && session.SessionId != Guid.Empty
        && session.TenantId == tenant.Id && session.TenantId != Guid.Empty && IsTenant == session.TenantId.HasValue
        && session.SessionKind is SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication
        && Text(session.SecurityStamp, 128) && Text(session.ActorScope, 64) && Text(session.EffectiveScope, 64);
    private static bool Text(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
    private bool Supports(string contentType) => pdf.Value.Supports(contentType) || docx.Value.Enabled && contentType == Domain.AiKnowledgeFilePolicy.DocxContentType;
    private void Check(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge index rebuild cannot join an ambient transaction."); }
    private static Result<AiKnowledgeIndexEnqueueResponse> Conflict() => Failure(AiKnowledgeErrorCodes.IndexConflict, "The rebuild request, source, task or original session is no longer available.", ErrorType.Conflict);
    private static Result<AiKnowledgeIndexEnqueueResponse> Forbidden() => Failure("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
    private static Result<AiKnowledgeIndexEnqueueResponse> Failure(string code, string message, ErrorType type) => AiKnowledgeImportStore.Failure<AiKnowledgeIndexEnqueueResponse>(code, message, type);
    private sealed class RebuildRejectedException : Exception;
}
