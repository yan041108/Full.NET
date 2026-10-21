using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>原交互会话恢复精确失败任务；不读取文件、不派发模型、不清除收费记录。</summary>
internal sealed class AiKnowledgeIndexRetryService(IQueryExecutor query, ICommandExecutor command, IDataTransactionState transactions,
    ICurrentTenant tenant, IClock clock, AiKnowledgeIndexTaskStore tasks, IBackgroundSessionAuthorization authorization,
    Microsoft.Extensions.Options.IOptions<AiKnowledgePdfRuntimeOptions>? pdf = null,
    Microsoft.Extensions.Options.IOptions<AiKnowledgeDocxRuntimeOptions>? docx = null)
{
    internal static readonly string[] Permissions = [.. AiKnowledgeIndexEnqueueService.Permissions, AiKnowledgeDocumentPermissions.IndexRetry];

    /// <summary>仅恢复失败或耗尽次数且租约到期的任务；其他状态及不同原会话返回冲突。</summary>
    /// <remarks>Identity 在本地写入外复核；202 只证明接受了恢复，不承诺响应时仍排队或已经就绪。</remarks>
    internal async Task<Result<AiKnowledgeIndexEnqueueResponse>> RetryAsync(Guid knowledgeBaseId, Guid documentId, Guid versionId,
        Guid taskId, SessionBindingSnapshot original, CancellationToken token)
    {
        Check(token); ArgumentNullException.ThrowIfNull(original);
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty || taskId == Guid.Empty)
            return Failure(AiKnowledgeErrorCodes.InputInvalid, "Knowledge, document, version and task identifiers are required.", ErrorType.Validation);
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["VersionId"] = versionId, ["OwnerUserId"] = original.UserId };
        var source = await ReadSourceAsync(parameters, original, token).ConfigureAwait(false);
        if (source is null || source.KnowledgeBaseId != knowledgeBaseId || source.DocumentId != documentId || source.VersionId != versionId
            || source.OwnerUserId != original.UserId || source.DocumentVersion <= 0 || source.KnowledgeBaseVersion <= 0
            || source.ModelConfigId == Guid.Empty || source.ModelConfigVersion <= 0) return AiKnowledgeImportStore.NotFound<AiKnowledgeIndexEnqueueResponse>();
        if (!Supports(source.ContentType))
            return Failure(AiKnowledgeErrorCodes.IndexFormatUnsupported, "The source format is not enabled for indexing.", ErrorType.BusinessRule);
        var binding = new AiKnowledgeIndexTaskBinding(knowledgeBaseId, documentId, versionId, source.DocumentVersion, source.KnowledgeBaseVersion, original.UserId);
        var writeParameters = tasks.TaskParameters(binding, taskId, token);
        var frozen = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(AiKnowledgeScope.IsTenant(tenant)
            ? AiKnowledgeIndexSessionSql.FindBindingTenant : AiKnowledgeIndexSessionSql.FindBindingHost, writeParameters, token).ConfigureAwait(false);
        Check(token); if (!ValidIdentity(original)) return Forbidden();
        if (frozen is null || frozen.Id == Guid.Empty || frozen.IndexTaskId != taskId
            || new SessionBindingSnapshot(frozen.ActorUserId, frozen.TenantId, frozen.SessionId, frozen.SecurityStamp,
                frozen.ActorScope, frozen.EffectiveScope, frozen.SessionKind) != original) return Conflict();
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        // 查询和授权等待后更新时钟；单次条件写入同时核对最新来源、批准、原会话行及恢复状态。
        if (!Supports(source.ContentType)) return Conflict();
        writeParameters["Now"] = clock.UtcNow; writeParameters["SessionRecordId"] = frozen.Id;
        writeParameters["ModelConfigId"] = source.ModelConfigId; writeParameters["ModelConfigVersion"] = source.ModelConfigVersion;
        writeParameters["PdfEnabled"] = pdf?.Value.Enabled == true ? 1 : 0;
        writeParameters["DocxEnabled"] = docx?.Value.Enabled == true ? 1 : 0;
        var affected = await command.ExecuteAsync(AiKnowledgeScope.IsTenant(tenant) ? AiKnowledgeIndexRetrySql.Tenant : AiKnowledgeIndexRetrySql.Host,
            writeParameters, token).ConfigureAwait(false);
        Check(token); if (!ValidIdentity(original)) return Forbidden();
        if (affected != 1) return Conflict();
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        // 授权等待后重新读取来源；不能把写入之前的快照作为响应时的权威证明。
        if (await ReadSourceAsync(parameters, original, token).ConfigureAwait(false) != source || !Supports(source.ContentType)) return Conflict();
        return Result<AiKnowledgeIndexEnqueueResponse>.Success(new(taskId, versionId));
    }

    private bool Supports(string contentType) => (pdf?.Value ?? new AiKnowledgePdfRuntimeOptions()).Supports(contentType)
        || (docx?.Value.Enabled == true && contentType == Domain.AiKnowledgeFilePolicy.DocxContentType);

    private async Task<AiKnowledgeIndexEnqueueRecord?> ReadSourceAsync(Dictionary<string, object?> parameters, SessionBindingSnapshot original, CancellationToken token)
    {
        Check(token); if (!ValidIdentity(original)) return null;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexEnqueueRecord>(AiKnowledgeScope.IsTenant(tenant)
            ? AiKnowledgeIndexEnqueueSql.SourceTenant : AiKnowledgeIndexEnqueueSql.SourceHost, parameters, token).ConfigureAwait(false);
        Check(token); return ValidIdentity(original) ? row : null;
    }

    private async Task<bool> AuthorizedAsync(SessionBindingSnapshot original, CancellationToken token)
    {
        {
            Check(token); if (!ValidIdentity(original)) return false;
            var actor = await authorization.AuthorizeAllAsync(original, Permissions, token).ConfigureAwait(false);
            Check(token);
            if (!ValidIdentity(original) || actor is null || actor.UserId != original.UserId || actor.TenantId != original.TenantId || actor.SessionId != original.SessionId) return false;
        }
        return true;
    }

    private bool ValidIdentity(SessionBindingSnapshot session) => session.UserId != Guid.Empty && session.SessionId != Guid.Empty
        && session.TenantId == tenant.Id && session.TenantId != Guid.Empty && (AiKnowledgeScope.IsTenant(tenant) == session.TenantId.HasValue)
        && session.SessionKind is SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication
        && Text(session.SecurityStamp, 128) && Text(session.ActorScope, 64) && Text(session.EffectiveScope, 64);
    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge index retry cannot join an ambient transaction.");
    }
    private static Result<AiKnowledgeIndexEnqueueResponse> Conflict() => Failure(AiKnowledgeErrorCodes.IndexConflict, "The task cannot be retried with the current source and original session.", ErrorType.Conflict);
    private static Result<AiKnowledgeIndexEnqueueResponse> Forbidden() => Failure("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
    private static Result<AiKnowledgeIndexEnqueueResponse> Failure(string code, string message, ErrorType type) => AiKnowledgeImportStore.Failure<AiKnowledgeIndexEnqueueResponse>(code, message, type);
}
