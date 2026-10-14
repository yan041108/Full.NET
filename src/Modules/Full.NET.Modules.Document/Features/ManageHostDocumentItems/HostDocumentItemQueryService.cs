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
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostDocumentItemResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? tagId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
                    ("Offset", offset),
                    ("PageSize", pageSize),
                    ("TagId", tagId)),
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
        cancellationToken.ThrowIfCancellationRequested();
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

    public async Task<Result<HostFileContent>> OpenCurrentVersionContentAsync(
        Guid itemId,
        DocumentAccessObservation? accessObservation = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            return contentResult;
        }

        return await RecordAccessAndTransferAsync(record, accessObservation, contentResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<HostDocumentVersionResponse>>> ListVersionsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null)
        {
            return Result<IReadOnlyList<HostDocumentVersionResponse>>.Failure(NotFoundError());
        }

        var versions = await queryExecutor
            .QueryAsync<DocumentVersionRecord>(
                DocumentItemSql.ListVersionsByItemId,
                DocumentSqlParameters.Create(("DocumentItemId", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return Result<IReadOnlyList<HostDocumentVersionResponse>>.Success(
            versions.Select(HostDocumentItemResponseMapper.MapVersion).ToArray());
    }

    public async Task<Result<HostFileContent>> OpenVersionPreviewAsync(
        Guid itemId,
        Guid? versionId,
        DocumentAccessObservation? accessObservation = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", itemId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null)
        {
            return Result<HostFileContent>.Failure(NotFoundError());
        }

        var fileIdResult = await ResolvePreviewFileIdAsync(itemId, versionId, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!fileIdResult.IsSuccess)
        {
            return Result<HostFileContent>.Failure(fileIdResult.Error!);
        }

        var contentResult = await hostFileContentReader
            .OpenReadyContentAsync(fileIdResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!contentResult.IsSuccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return contentResult;
        }

        return await RecordAccessAndTransferAsync(item, accessObservation, contentResult, cancellationToken, requirePreviewMime: true).ConfigureAwait(false);
    }

    // 日志或请求取消失败时，尚未交付给 HTTP 层的文件流仍由本服务负责释放。
    private async Task<Result<HostFileContent>> RecordAccessAndTransferAsync(
        DocumentItemDetailRecord item, DocumentAccessObservation? observation,
        Result<HostFileContent> content, CancellationToken token, bool requirePreviewMime = false)
    {
        var transferred = false;
        try
        {
            token.ThrowIfCancellationRequested();
            // MIME 拒绝也在持有范围内解释，不能把已取消请求改为普通“不支持预览”。
            if (requirePreviewMime && !IsPreviewSupportedMime(content.Value!.ContentType))
                return Result<HostFileContent>.Failure(PreviewNotSupportedError());
            await TryRecordAccessAsync(item, observation, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            transferred = true;
            return content;
        }
        finally
        {
            if (!transferred) await content.Value!.Content.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Result<Guid>> ResolvePreviewFileIdAsync(
        Guid itemId,
        Guid? versionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (versionId is null)
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
        cancellationToken.ThrowIfCancellationRequested();
        if (version is null)
        {
            return Result<Guid>.Failure(NotFoundError());
        }

        return Result<Guid>.Success(version.FileId);
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
            cancellationToken);
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
