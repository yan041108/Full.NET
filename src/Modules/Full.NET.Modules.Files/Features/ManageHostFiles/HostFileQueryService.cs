using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Features.ManageHostFiles;

/// <summary>Host 文件元数据分页列表、详情与引用声明只读查询。</summary>
internal sealed class HostFileQueryService(
    IQueryExecutor queryExecutor,
    IHostFileContentReader hostFileContentReader,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostFileResponse>>> ListAsync(
        int page,
        int pageSize,
        HostFileListFilter filter,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var parameters = BuildListParameters(filter, offset, pageSize);

        var countStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => HostFileSql.CountActiveHostFiles,
            DatabaseProvider.MySql => HostFileSql.CountActiveHostFilesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => HostFileSql.ListActiveHostFilesSqlServer,
            DatabaseProvider.MySql => HostFileSql.ListActiveHostFilesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<HostFileListRecord>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<HostFileResponse>>.Success(
            new PagedResult<HostFileResponse>(items, page, pageSize, total));
    }

    public async Task<Result<HostFileResponse>> GetByIdAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new Dictionary<string, object?> { ["FileId"] = fileId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<HostFileResponse>.Success(Map(record));
    }

    public async Task<Result<HostFileDetailRecord>> GetDetailByIdAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new Dictionary<string, object?> { ["FileId"] = fileId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<HostFileDetailRecord>.Failure(new Error(
                FilesErrorCodes.FileNotFound,
                "The file was not found.",
                ErrorType.NotFound))
            : Result<HostFileDetailRecord>.Success(record);
    }

    public async Task<Result<PagedResult<HostFileReferenceClaimResponse>>> ListReferencesAsync(
        Guid fileId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var file = await GetDetailByIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!file.IsSuccess)
        {
            return Result<PagedResult<HostFileReferenceClaimResponse>>.Failure(file.Error!);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var parameters = new Dictionary<string, object?>
        {
            ["FileId"] = fileId,
            ["Offset"] = offset,
            ["PageSize"] = pageSize,
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostFileReferenceClaimSql.CountByFileId,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => HostFileReferenceClaimSql.ListByFileIdSqlServer,
            DatabaseProvider.MySql => HostFileReferenceClaimSql.ListByFileIdMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostFileReferenceClaimRecord>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapReference).ToArray();
        return Result<PagedResult<HostFileReferenceClaimResponse>>.Success(
            new PagedResult<HostFileReferenceClaimResponse>(items, page, pageSize, total));
    }

    /// <summary>打开可安全内联预览的文件内容；调用方负责释放返回流。</summary>
    public async Task<Result<HostFileContent>> OpenPreviewAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var detailResult = await GetDetailByIdAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        if (!detailResult.IsSuccess)
        {
            return Result<HostFileContent>.Failure(detailResult.Error!);
        }

        if (!HostFilePreviewSupport.IsSupportedContentType(detailResult.Value!.ContentType))
        {
            return Result<HostFileContent>.Failure(new Error(
                FilesErrorCodes.PreviewNotSupported,
                "Preview is not supported for this content type.",
                ErrorType.BusinessRule));
        }

        return await hostFileContentReader
            .OpenReadyContentAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryAcquireHostFileRowLockAsync(
        Guid fileId,
        CancellationToken cancellationToken = default) =>
        await HostFileRowLocks.TryAcquireAsync(
            queryExecutor,
            databaseOptions.Value.Provider,
            fileId,
            cancellationToken).ConfigureAwait(false);

    internal static HostFileResponse Map(HostFileListRecord record) =>
        new(
            record.Id,
            record.OriginalFileName,
            record.ContentType,
            record.SizeBytes,
            record.ContentHash,
            record.CreatedAtUtc,
            record.CreatedByUserId,
            record.FolderId,
            record.Revision,
            record.UpdatedAtUtc,
            record.UpdatedByUserId);

    internal static HostFileResponse Map(HostFileDetailRecord record) =>
        new(
            record.Id,
            record.OriginalFileName,
            record.ContentType,
            record.SizeBytes,
            record.ContentHash,
            record.CreatedAtUtc,
            record.CreatedByUserId,
            record.FolderId,
            record.Revision,
            record.UpdatedAtUtc,
            record.UpdatedByUserId);

    internal static HostFileReferenceClaimResponse MapReference(
        HostFileReferenceClaimRecord record) =>
        new(
            record.Id,
            record.IdempotencyKey,
            record.ConsumerModule,
            record.ConsumerReferenceId,
            record.State,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.ConfirmedAtUtc,
            record.ReleasedAtUtc);

    internal static HostFileListFilter ParseFolderFilter(string? folderIdValue) =>
        folderIdValue switch
        {
            null => HostFileListFilter.All,
            "" => HostFileListFilter.UncategorizedOnly,
            _ => Guid.TryParse(folderIdValue, out var folderId)
                ? HostFileListFilter.ForFolder(folderId)
                : HostFileListFilter.All,
        };

    private static Dictionary<string, object?> BuildListParameters(
        HostFileListFilter filter,
        long offset,
        int pageSize) =>
        new()
        {
            ["Offset"] = offset,
            ["PageSize"] = pageSize,
            ["ApplyFolderFilter"] = filter.ApplyFolderFilter ? 1 : 0,
            ["RootOnly"] = filter.RootOnly ? 1 : 0,
            ["FolderId"] = filter.FolderId,
            ["FileNameContains"] = filter.FileNameContains,
        };

    private static Result<HostFileResponse> NotFound() =>
        Result<HostFileResponse>.Failure(new Error(
            FilesErrorCodes.FileNotFound,
            "The file was not found.",
            ErrorType.NotFound));
}

/// <summary>Host 文件列表筛选条件。</summary>
internal readonly record struct HostFileListFilter(
    bool ApplyFolderFilter,
    bool RootOnly,
    Guid? FolderId,
    string? FileNameContains)
{
    public static HostFileListFilter All => new(false, false, null, null);

    public static HostFileListFilter UncategorizedOnly => new(true, true, null, null);

    public static HostFileListFilter ForFolder(Guid folderId) =>
        new(true, false, folderId, null);

    public HostFileListFilter WithFileNameContains(string? fileNameContains) =>
        new(ApplyFolderFilter, RootOnly, FolderId, NormalizeContains(fileNameContains));

    private static string? NormalizeContains(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: > 0 and <= 128 } ? trimmed : null;
    }
}
