using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>Host 文档预览转换任务只读查询。</summary>
internal sealed class HostDocumentPreviewTaskQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IHostFileContentReader hostFileContentReader,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostDocumentPreviewTaskResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? documentItemId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentPreviewTaskSql.PageSqlServer,
            DatabaseProvider.MySql => DocumentPreviewTaskSql.PageMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

        var pageResult = await multiResultQueryExecutor
            .QueryMultipleAsync(
                statement,
                DocumentSqlParameters.Create(
                    ("DocumentItemId", documentItemId),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    var rows = await reader.ReadAsync<DocumentPreviewTaskRecord>().ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);

        var items = pageResult.Rows.Select(HostDocumentPreviewTaskMapper.Map).ToArray();
        return Result<PagedResult<HostDocumentPreviewTaskResponse>>.Success(
            new PagedResult<HostDocumentPreviewTaskResponse>(items, page, pageSize, pageResult.Total));
    }

    public async Task<Result<HostDocumentPreviewTaskResponse>> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(
                DocumentPreviewTaskSql.FindById,
                DocumentSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<HostDocumentPreviewTaskResponse>.Failure(TaskNotFoundError())
            : Result<HostDocumentPreviewTaskResponse>.Success(HostDocumentPreviewTaskMapper.Map(record));
    }

    public async Task<Result<HostFileContent>> OpenOutputContentAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(
                DocumentPreviewTaskSql.FindById,
                DocumentSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<HostFileContent>.Failure(TaskNotFoundError());
        }

        if (!string.Equals(record.StatusKey, HostDocumentPreviewTaskStatusKeys.Succeeded, StringComparison.Ordinal)
            || record.OutputFileId is null)
        {
            return Result<HostFileContent>.Failure(
                new Error(
                    DocumentErrorCodes.PreviewTaskNotReady,
                    "Document preview task output is not ready.",
                    ErrorType.BusinessRule));
        }

        return await hostFileContentReader
            .OpenReadyContentAsync(record.OutputFileId.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Error TaskNotFoundError() =>
        new(DocumentErrorCodes.PreviewTaskNotFound, "Document preview task was not found.", ErrorType.NotFound);
}
