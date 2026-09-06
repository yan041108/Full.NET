using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ocr.Features.ManageIdCardTasks;

internal sealed class OcrIdCardTaskQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<OcrIdCardTaskResponse>> GetByIdAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var row = await FindRecordAsync(taskId, cancellationToken).ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<OcrIdCardTaskResponse>.Success(OcrIdCardTaskMapper.Map(row, OcrIdCardTaskMapper.ShouldMask(row)));
    }

    public async Task<Result<PagedResult<OcrIdCardTaskResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                OcrIdCardTaskSql.Count,
                OcrSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? OcrIdCardTaskSql.ListMySql
            : OcrIdCardTaskSql.ListSqlServer;
        var rows = await queryExecutor.QueryAsync<OcrIdCardTaskRecord>(
                statement,
                OcrSqlParameters.Create([("Offset", offset), ("PageSize", pageSize)]),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<OcrIdCardTaskResponse>>.Success(
            new PagedResult<OcrIdCardTaskResponse>(
                rows.Select(row => OcrIdCardTaskMapper.Map(row, OcrIdCardTaskMapper.ShouldMask(row))).ToArray(),
                page,
                pageSize,
                total));
    }

    internal async Task<OcrIdCardTaskRecord?> FindRecordAsync(
        Guid taskId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<OcrIdCardTaskRecord>(
                OcrIdCardTaskSql.FindById,
                OcrSqlParameters.Create([("TaskId", taskId)]),
                cancellationToken)
            .ConfigureAwait(false);

    private static Result<OcrIdCardTaskResponse> NotFound() =>
        Result<OcrIdCardTaskResponse>.Failure(new Error(
            OcrErrorCodes.IdCardTaskNotFound,
            "The OCR ID card task was not found.",
            ErrorType.NotFound));
}
