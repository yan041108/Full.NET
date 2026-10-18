using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>服务器推导索引绑定并原子冻结原会话；不读取文件、不调用模型、不自动重试。</summary>
internal sealed class AiKnowledgeIndexEnqueueService(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    AiKnowledgeIndexSessionService sessions, IBackgroundSessionAuthorization authorization,
    Microsoft.Extensions.Options.IOptions<AiKnowledgePdfRuntimeOptions>? pdf = null,
    Microsoft.Extensions.Options.IOptions<AiKnowledgeDocxRuntimeOptions>? docx = null)
{
    internal static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue];

    /// <summary>接受最新已完成导入的已启用格式；重放仅复用完全相同的原会话。</summary>
    /// <remarks>所有 Identity Port 均在 AI 事务外；提交后的撤权或来源变化拒绝成功响应，但不删除已原子保存的任务。</remarks>
    internal async Task<Result<AiKnowledgeIndexEnqueueResponse>> EnqueueAsync(Guid knowledgeBaseId, Guid documentId, Guid versionId,
        SessionBindingSnapshot original, CancellationToken token, Guid? grantId = null)
    {
        Check(token); ArgumentNullException.ThrowIfNull(original);
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty)
            return Failure(AiKnowledgeErrorCodes.InputInvalid, "Knowledge, document and version identifiers are required.", ErrorType.Validation);
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["VersionId"] = versionId, ["OwnerUserId"] = original.UserId };
        var source = await ReadAsync(parameters, original, token).ConfigureAwait(false);
        if (!ValidSource(source, knowledgeBaseId, documentId, versionId, original)) return AiKnowledgeImportStore.NotFound<AiKnowledgeIndexEnqueueResponse>();
        if (!Supports(source!.ContentType))
            return Failure(AiKnowledgeErrorCodes.IndexFormatUnsupported, "The source format is not enabled for indexing.", ErrorType.BusinessRule);
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        if (!Supports(source.ContentType)) return Failure(AiKnowledgeErrorCodes.IndexFormatUnsupported, "The source format is not enabled for indexing.", ErrorType.BusinessRule);
        var binding = new AiKnowledgeIndexTaskBinding(knowledgeBaseId, documentId, versionId, source.DocumentVersion, source.KnowledgeBaseVersion, original.UserId);
        var taskId = grantId.HasValue
            ? await sessions.EnqueueGrantedAsync(binding, original, grantId.Value, token).ConfigureAwait(false)
            : await sessions.EnqueueAsync(binding, original, token).ConfigureAwait(false);
        Check(token); if (!ValidIdentity(original)) return Forbidden();
        if (!await AuthorizedAsync(original, token).ConfigureAwait(false)) return Forbidden();
        // 跨模块授权等待后，以 AI 权威行查询结束证明，不能返回等待前的来源快照。
        var current = await ReadAsync(parameters, original, token).ConfigureAwait(false);
        if (current != source || taskId is null || taskId == Guid.Empty || !Supports(source.ContentType))
            return Failure(AiKnowledgeErrorCodes.IndexConflict, "The source or original session binding is no longer available.", ErrorType.Conflict);
        // 不公开内部 StateKey：重复请求可能已经 processing/completed，202 只确认稳定绑定。
        return Result<AiKnowledgeIndexEnqueueResponse>.Success(new(taskId.Value, versionId));
    }

    private async Task<AiKnowledgeIndexEnqueueRecord?> ReadAsync(Dictionary<string, object?> parameters, SessionBindingSnapshot original, CancellationToken token)
    {
        Check(token); if (!ValidIdentity(original)) return null;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexEnqueueRecord>(AiKnowledgeScope.IsTenant(tenant)
            ? AiKnowledgeIndexEnqueueSql.SourceTenant : AiKnowledgeIndexEnqueueSql.SourceHost, parameters, token).ConfigureAwait(false);
        Check(token); return ValidIdentity(original) ? row : null;
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

    private bool ValidIdentity(SessionBindingSnapshot session) => session.UserId != Guid.Empty && session.SessionId != Guid.Empty
        && session.TenantId == tenant.Id && session.TenantId != Guid.Empty && (AiKnowledgeScope.IsTenant(tenant) == session.TenantId.HasValue)
        && session.SessionKind is SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication
        && Text(session.SecurityStamp, 128) && Text(session.ActorScope, 64) && Text(session.EffectiveScope, 64);
    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private bool Supports(string contentType) => (pdf?.Value ?? new AiKnowledgePdfRuntimeOptions()).Supports(contentType)
        || (docx?.Value.Enabled == true && contentType == Domain.AiKnowledgeFilePolicy.DocxContentType);
    private static bool ValidSource(AiKnowledgeIndexEnqueueRecord? row, Guid knowledge, Guid document, Guid version, SessionBindingSnapshot original) => row is not null
        && row.KnowledgeBaseId == knowledge && row.DocumentId == document && row.VersionId == version && row.OwnerUserId == original.UserId
        && row.DocumentVersion > 0 && row.KnowledgeBaseVersion > 0 && row.ModelConfigId != Guid.Empty && row.ModelConfigVersion > 0;
    private void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge index HTTP admission cannot join an ambient transaction.");
    }
    private static Result<AiKnowledgeIndexEnqueueResponse> Forbidden() => Failure("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
    private static Result<AiKnowledgeIndexEnqueueResponse> Failure(string code, string message, ErrorType type) => AiKnowledgeImportStore.Failure<AiKnowledgeIndexEnqueueResponse>(code, message, type);
}
