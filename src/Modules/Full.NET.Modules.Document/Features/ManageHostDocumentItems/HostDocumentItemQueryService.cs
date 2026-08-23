using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
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
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostDocumentItemResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
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
                new { Offset = offset, PageSize = pageSize },
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentItemDetailRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostDocumentItemResponse>>.Success(
            new PagedResult<HostDocumentItemResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<HostDocumentItemResponse>> GetByIdAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                new { Id = itemId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : Result<HostDocumentItemResponse>.Success(Map(record));
    }

    public async Task<Result<HostFileContent>> OpenCurrentVersionContentAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                new { Id = itemId },
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

        return await hostFileContentReader
            .OpenReadyContentAsync(record.FileId.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<HostDocumentVersionResponse>>> ListVersionsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var item = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                new { Id = itemId },
                cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return Result<IReadOnlyList<HostDocumentVersionResponse>>.Failure(NotFoundError());
        }

        var versions = await queryExecutor
            .QueryAsync<DocumentVersionRecord>(
                DocumentItemSql.ListVersionsByItemId,
                new { DocumentItemId = itemId },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<HostDocumentVersionResponse>>.Success(
            versions.Select(HostDocumentItemResponseMapper.MapVersion).ToArray());
    }

    public async Task<Result<HostFileContent>> OpenVersionPreviewAsync(
        Guid itemId,
        Guid? versionId,
        CancellationToken cancellationToken = default)
    {
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

        return contentResult;
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
                    new { Id = itemId },
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
                new { VersionId = versionId.Value, DocumentItemId = itemId },
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return Result<Guid>.Failure(NotFoundError());
        }

        return Result<Guid>.Success(version.FileId);
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

    private static HostDocumentItemResponse Map(DocumentItemDetailRecord record) =>
        HostDocumentItemResponseMapper.Map(record);

    private static Result<HostDocumentItemResponse> NotFound() =>
        Result<HostDocumentItemResponse>.Failure(NotFoundError());

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.NotFound, "Document item was not found.", ErrorType.NotFound);

    private static Error NoCurrentVersionError() =>
        new(DocumentErrorCodes.NoCurrentVersion, "Document item has no downloadable version.", ErrorType.NotFound);

    private static Error PreviewNotSupportedError() =>
        new(DocumentErrorCodes.PreviewNotSupported, "Document preview is not supported for this content type.", ErrorType.BusinessRule);
}
