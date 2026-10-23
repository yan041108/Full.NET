using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeDocuments;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;
using static Full.NET.Modules.Ai.Features.ManageKnowledgeVersions.AiKnowledgeImportStore;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;

/// <summary>每次来源读取都重新核对文档授权；不缓存正文或返回可绕过授权的存储 URL。</summary>
internal sealed class AiKnowledgeVersionReader(AiKnowledgeImportStore store, IQueryExecutor query,
    AiKnowledgeReadAccess access, AiKnowledgePermissionGuard permissions, IHostFileReferenceReader references, IHostFileContentReader hostFiles,
    ITenantResourceFileStore tenantFiles)
{
    internal async Task<Result<PagedResult<AiKnowledgeVersionResponse>>> ListAsync(Guid knowledgeBaseId, Guid documentId,
        Guid actor, int page, int pageSize, CancellationToken token)
    {
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty) return NotFound<PagedResult<AiKnowledgeVersionResponse>>();
        var parameters = await ReadParametersAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false);
        if (await query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(AiKnowledgeDocumentSql.ReadFor(store.Provider, store.IsTenant),
            parameters, token).ConfigureAwait(false) is null)
            return NotFound<PagedResult<AiKnowledgeVersionResponse>>();
        var offset = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 100 || offset > 100_000)
            return Failure<PagedResult<AiKnowledgeVersionResponse>>(AiKnowledgeErrorCodes.PageInvalid, "Invalid bounded page.", ErrorType.Validation);
        parameters.Add("PageSize", pageSize);
        parameters.Add("Offset", offset);
        var sql = store.Provider == DatabaseProvider.SqlServer
            ? store.IsTenant ? AiKnowledgeVersionSql.ListSqlServerTenant : AiKnowledgeVersionSql.ListSqlServerHost
            : store.IsTenant ? AiKnowledgeVersionSql.ListMySqlTenant : AiKnowledgeVersionSql.ListMySqlHost;
        var count = await query.QuerySingleOrDefaultAsync<long>(AiKnowledgeVersionSql.CountFor(store.Provider, store.IsTenant), parameters, token).ConfigureAwait(false);
        var rows = await query.QueryAsync<AiKnowledgeVersionRecord>(sql, parameters, token).ConfigureAwait(false);
        // 等待分页查询期间可能撤销角色、机构或文档授权，响应前重新取得完整权威关系。
        var current = await ReadParametersAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false);
        if (await query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(AiKnowledgeDocumentSql.ReadFor(store.Provider, store.IsTenant), current, token).ConfigureAwait(false) is null)
            return NotFound<PagedResult<AiKnowledgeVersionResponse>>();
        if (!await permissions.IsAuthorizedAsync([AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.VersionsRead], actor, token).ConfigureAwait(false))
            return Failure<PagedResult<AiKnowledgeVersionResponse>>("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
        // 索引状态有独立精确权限；查询等待期间撤权后也必须退回原有的中性元数据状态。
        var canReadIndex = await permissions.IsAuthorizedAsync([AiKnowledgeDocumentPermissions.IndexRead], actor, token).ConfigureAwait(false);
        return Result<PagedResult<AiKnowledgeVersionResponse>>.Success(new(rows.Select(row => Map(row, canReadIndex)).ToArray(), page, pageSize, count));
    }

    internal async Task<Result<AiKnowledgeSourceContent>> OpenAsync(Guid knowledgeBaseId, Guid documentId, Guid versionId,
        Guid actor, CancellationToken token)
    {
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty || versionId == Guid.Empty) return NotFound<AiKnowledgeSourceContent>();
        var parameters = await ReadParametersAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false);
        parameters.Add("VersionId", versionId);
        var statement = AiKnowledgeVersionSql.ReadFor(store.Provider, store.IsTenant);
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeVersionRecord>(statement, parameters, token).ConfigureAwait(false);
        if (row is null) return NotFound<AiKnowledgeSourceContent>();
        if (store.IsTenant)
        {
            var opened = await tenantFiles.OpenReadyContentAsync("ai", row.Id, row.FileId, token).ConfigureAwait(false);
            if (!opened.IsSuccess) return Unavailable();
            // 存储打开后再次复核权威授权，取消或授权失败时也必须释放流。
            try
            {
                if (!await RevalidateAsync(knowledgeBaseId, documentId, versionId, actor, statement, token).ConfigureAwait(false))
                { await opened.Value!.Content.DisposeAsync().ConfigureAwait(false); return NotFound<AiKnowledgeSourceContent>(); }
                return Result<AiKnowledgeSourceContent>.Success(new(opened.Value!.Content, row.OriginalFileName));
            }
            catch { await opened.Value!.Content.DisposeAsync().ConfigureAwait(false); throw; }
        }
        var file = await references.GetReadyReferenceAsync(row.FileId, token).ConfigureAwait(false);
        if (file is null || file.SizeBytes != row.SizeBytes || !string.Equals(file.ContentHash, row.ContentHash, StringComparison.OrdinalIgnoreCase)) return Unavailable();
        var content = await hostFiles.OpenReadyContentAsync(row.FileId, token).ConfigureAwait(false);
        if (!content.IsSuccess) return Unavailable();
        try
        {
            if (!await RevalidateAsync(knowledgeBaseId, documentId, versionId, actor, statement, token).ConfigureAwait(false))
            { await content.Value!.Content.DisposeAsync().ConfigureAwait(false); return NotFound<AiKnowledgeSourceContent>(); }
            return Result<AiKnowledgeSourceContent>.Success(new(content.Value!.Content, row.OriginalFileName));
        }
        catch { await content.Value!.Content.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static AiKnowledgeVersionResponse Map(AiKnowledgeVersionRecord row, bool canReadIndex) => new(row.Id, row.DocumentId,
        row.VersionNumber, row.OriginalFileName, row.ContentType, row.ContentHash, row.SizeBytes, canReadIndex ? row.ProcessingStatus : "pending_processing", row.CreatedAtUtc) { ParsingPolicyKey = row.ParsingPolicyKey };
    private async Task<Dictionary<string, object?>> ReadParametersAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, CancellationToken token)
    {
        var parameters = await access.ParametersAsync(actor, token).ConfigureAwait(false);
        parameters.Add("KnowledgeBaseId", knowledgeBaseId);
        parameters.Add("DocumentId", documentId);
        return parameters;
    }
    private async Task<bool> RevalidateAsync(Guid knowledgeBaseId, Guid documentId, Guid versionId, Guid actor, SqlStatement statement, CancellationToken token)
    {
        if (!await permissions.IsAuthorizedAsync([AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead], actor, token).ConfigureAwait(false)) return false;
        // 存储打开期间可能撤销角色或机构隶属，必须重新取得权威关系，不能复用初次参数。
        var parameters = await ReadParametersAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false);
        parameters.Add("VersionId", versionId);
        return await query.QuerySingleOrDefaultAsync<AiKnowledgeVersionRecord>(statement, parameters, token).ConfigureAwait(false) is not null;
    }
    private static Result<AiKnowledgeSourceContent> Unavailable() => Failure<AiKnowledgeSourceContent>(
        "ai.knowledge.source_unavailable", "The source file is unavailable.", ErrorType.NotFound);
}

/// <summary>请求内流由 HTTP 响应负责释放；文件名已在上传边界校验。</summary>
internal sealed record AiKnowledgeSourceContent(Stream Content, string FileName);
