using Full.NET.Modules.Document.Security;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Features.DocumentAccessLogs;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentItems;

/// <summary>
/// Host 文档项与版本只读查询服务。列表与详情投影均 LEFT JOIN 当前版本行，
/// 缺失版本时由 Mapper 防御性转换 NULL；下载内容请求通过 Files 模块的 <see cref="IHostFileContentReader"/>
/// 间接读取，本服务不直连文件存储，以保持模块边界与 Files 模块对存储后端的统一治理。
/// </summary>
internal sealed class HostDocumentItemQueryService(
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IQueryExecutor queryExecutor,
    IHostFileContentReader hostFileContentReader,
    DocumentAccessLogRecorder accessLogRecorder,
    DocumentItemTagAssignmentService documentItemTagAssignmentService,
    IOptions<DatabaseOptions> databaseOptions,
    IDocumentAccessPolicy accessPolicy)
{
    public async Task<Result<PagedResult<HostDocumentItemResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? tagId = null,
        CancellationToken cancellationToken = default,
        string? keyword = null,
        Guid? categoryId = null,
        HostDocumentStatus? status = null,
        HostDocumentType? documentType = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (keyword?.Length > 256 || status.HasValue && !Enum.IsDefined(status.Value)
            || documentType.HasValue && !Enum.IsDefined(documentType.Value))
            return Result<PagedResult<HostDocumentItemResponse>>.Failure(
                new Error(DocumentErrorCodes.Invalid, "Document filters are invalid.", ErrorType.Validation));
        // LIKE 转义使通配符成为普通检索文字，避免两库对方括号的解释差异。
        var pattern = string.IsNullOrWhiteSpace(keyword) ? null : "%" + keyword.Trim()
            .Replace("~", "~~", StringComparison.Ordinal).Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentItemSql.PageSqlServer,
            DatabaseProvider.MySql => DocumentItemSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                statement,
                DocumentSqlParameters.Create(
                    ("ActorUserId", accessPolicy.ActorUserId),
                    ("Offset", offset),
                    ("PageSize", pageSize),
                    ("TagId", tagId), ("Keyword", pattern), ("CategoryId", categoryId),
                    ("Status", status is null ? null : (int)status),
                    ("DocumentType", documentType is null ? null : (int)documentType)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentItemDetailRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var itemIds = pageResult.Rows.Select(row => row.Id).ToArray();
        var tagsByItem = await documentItemTagAssignmentService
            .ListByDocumentItemIdsAsync(itemIds, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return Result<PagedResult<HostDocumentItemResponse>>.Success(
            new PagedResult<HostDocumentItemResponse>(
                pageResult.Rows
                    .Select(row =>
                    {
                        tagsByItem.TryGetValue(row.Id, out var tags);
                        return Map(row, tags);
                    })
                    .ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<HostDocumentItemResponse>> GetByIdAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        // 元数据也须在授权返回后复核取消，不能把迟到的拒绝或快照交给已结束观察。
        cancellationToken.ThrowIfCancellationRequested();
        var error = await accessPolicy.CheckAsync(itemId, DocumentOperation.Read, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return error is null ? await GetByIdAuthorizedAsync(itemId, cancellationToken).ConfigureAwait(false)
            : Result<HostDocumentItemResponse>.Failure(error);
    }

    private async Task<Result<HostDocumentItemResponse>> GetByIdAuthorizedAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null)
        {
            return NotFound();
        }

        var tagsByItem = await documentItemTagAssignmentService
            .ListByDocumentItemIdsAsync([itemId], cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        tagsByItem.TryGetValue(itemId, out var tags);
        return Result<HostDocumentItemResponse>.Success(Map(record, tags));
    }

    public Task<Result<HostFileContent>> OpenCurrentVersionContentAsync(
        Guid itemId,
        DocumentAccessObservation? accessObservation = null,
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Read,
            () => OpenCurrentVersionContentAuthorizedAsync(itemId, accessObservation, cancellationToken), cancellationToken);

    internal async Task<Result<HostFileContent>> OpenCurrentVersionContentAuthorizedAsync(
        Guid itemId,
        DocumentAccessObservation? accessObservation = null,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<HostFileContent>.Failure(NotFoundError());
        }

        if (record.FileId is null)
        {
            return Result<HostFileContent>.Failure(NoCurrentVersionError());
        }

        var contentResult = await hostFileContentReader
            .OpenReadyContentAsync(record.FileId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!contentResult.IsSuccess)
        {
            return contentResult;
        }

        return await RevalidateContentAsync(itemId, contentResult, accessObservation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PagedResult<HostDocumentVersionResponse>>> ListVersionsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default, int page = 1, int pageSize = 20)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var error = await accessPolicy.CheckAsync(itemId, DocumentOperation.Read, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return error is null ? await ListVersionsAuthorizedAsync(itemId, page, pageSize, cancellationToken).ConfigureAwait(false)
            : Result<PagedResult<HostDocumentVersionResponse>>.Failure(error);
    }

    private async Task<Result<PagedResult<HostDocumentVersionResponse>>> ListVersionsAuthorizedAsync(
        Guid itemId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = DocumentSqlParameters.Create(("DocumentItemId", itemId), ("Id", itemId),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var item = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null)
        {
            return Result<PagedResult<HostDocumentVersionResponse>>.Failure(NotFoundError());
        }

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            DocumentItemSql.CountVersionsByItemId, parameters, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var versions = await queryExecutor
            .QueryAsync<DocumentVersionRecord>(
                databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? DocumentItemSql.ListVersionsPageMySql : DocumentItemSql.ListVersionsPageSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return Result<PagedResult<HostDocumentVersionResponse>>.Success(
            new PagedResult<HostDocumentVersionResponse>(versions.Select(HostDocumentItemResponseMapper.MapVersion).ToArray(), page, pageSize, total));
    }

    public Task<Result<HostFileContent>> OpenVersionPreviewAsync(
        Guid itemId,
        Guid? versionId,
        DocumentAccessObservation? accessObservation = null,
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(itemId, DocumentOperation.Read,
            () => OpenVersionPreviewAuthorizedAsync(itemId, versionId, accessObservation, cancellationToken), cancellationToken);

    private async Task<Result<HostFileContent>> OpenVersionPreviewAuthorizedAsync(
        Guid itemId,
        Guid? versionId,
        DocumentAccessObservation? accessObservation = null,
        CancellationToken cancellationToken = default)
    {
        var item = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return Result<HostFileContent>.Failure(NotFoundError());
        }

        var fileIdResult = await ResolvePreviewFileIdAsync(itemId, versionId, cancellationToken)
            .ConfigureAwait(false);
        if (!fileIdResult.IsSuccess)
        {
            return Result<HostFileContent>.Failure(fileIdResult.Error!);
        }

        var contentResult = await hostFileContentReader
            .OpenReadyContentAsync(fileIdResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!contentResult.IsSuccess)
        {
            return contentResult;
        }

        if (!IsPreviewSupportedMime(contentResult.Value!.ContentType))
        {
            contentResult.Value.Content.Dispose();
            return Result<HostFileContent>.Failure(PreviewNotSupportedError());
        }

        return await RevalidateContentAsync(itemId, contentResult, accessObservation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<Guid>> ResolvePreviewFileIdAsync(
        Guid itemId,
        Guid? versionId,
        CancellationToken cancellationToken)
    {
        if (versionId is null)
        {
            var record = await queryExecutor
                .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                    DocumentItemSql.FindActiveById,
                    DocumentSqlParameters.Create(("Id", itemId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (record is null)
            {
                return Result<Guid>.Failure(NotFoundError());
            }

            if (record.FileId is null)
            {
                return Result<Guid>.Failure(NoCurrentVersionError());
            }

            return Result<Guid>.Success(record.FileId.Value);
        }

        var version = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentVersionRecord>(
                DocumentItemSql.FindVersionById,
                DocumentSqlParameters.Create(("VersionId", versionId.Value), ("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return Result<Guid>.Failure(NotFoundError());
        }

        return Result<Guid>.Success(version.FileId);
    }

    /// <summary>文件读取可能等待外部存储；移交流前重查撤权与删除，拒绝或异常时本层负责释放。</summary>
    private async Task<Result<HostFileContent>> RevalidateContentAsync(Guid itemId,
        Result<HostFileContent> content, DocumentAccessObservation? observation, CancellationToken token)
    {
        var transferred = false;
        try
        {
            if (await accessPolicy.CheckAsync(itemId, DocumentOperation.Read, token).ConfigureAwait(false) is { } denied)
                return Result<HostFileContent>.Failure(denied);
            var current = await queryExecutor.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)), token).ConfigureAwait(false);
            if (current is null) return Result<HostFileContent>.Failure(NotFoundError());
            await TryRecordAccessAsync(current, observation, token).ConfigureAwait(false);
            transferred = true;
            return content;
        }
        finally { if (!transferred) await content.Value!.Content.DisposeAsync().ConfigureAwait(false); }
    }

    private Task TryRecordAccessAsync(
        DocumentItemDetailRecord item,
        DocumentAccessObservation? accessObservation,
        CancellationToken cancellationToken)
    {
        if (accessObservation is null)
        {
            return Task.CompletedTask;
        }

        return accessLogRecorder.RecordAsync(
            item.Id,
            item.Title,
            accessObservation.AccessTypeKey,
            accessObservation.SourceKey,
            accessObservation.ActorUserId,
            accessObservation.ClientIpFingerprint,
            cancellationToken, accessObservation.RequestKey);
    }

    private static bool IsPreviewSupportedMime(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized.StartsWith("text/", StringComparison.Ordinal)
            || normalized.StartsWith("image/", StringComparison.Ordinal)
            || normalized == "application/pdf";
    }

    private static HostDocumentItemResponse Map(
        DocumentItemDetailRecord record,
        IReadOnlyList<HostDocumentTagAssignmentResponse>? tags = null) =>
        HostDocumentItemResponseMapper.Map(record, tags);

    private static Result<HostDocumentItemResponse> NotFound() =>
        Result<HostDocumentItemResponse>.Failure(NotFoundError());

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.NotFound, "Document item was not found.", ErrorType.NotFound);

    private static Error NoCurrentVersionError() =>
        new(DocumentErrorCodes.NoCurrentVersion, "Document item has no downloadable version.", ErrorType.NotFound);

    private static Error PreviewNotSupportedError() =>
        new(DocumentErrorCodes.PreviewNotSupported, "Document preview is not supported for this content type.", ErrorType.BusinessRule);
}
