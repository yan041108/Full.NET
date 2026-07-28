using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostExceptionLogs;

/// <summary>Host 异常日志分页列表与详情只读查询。</summary>
internal sealed class HostExceptionLogQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    AuditingContainsTimeRangePolicy containsTimeRangePolicy)
{
    private const string SafeExceptionMessage = "Unhandled application exception.";

    public async Task<Result<PagedResult<ExceptionLogResponse>>> ListAsync(
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? exceptionTypeContains,
        string? pathContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = BuildFilter(fromUtc, toUtc, exceptionTypeContains, pathContains);
        var timeRangeError = containsTimeRangePolicy.Validate(
            filter.FromUtc,
            filter.ToUtc,
            filter.ExceptionTypeContains is not null
            || filter.PathContains is not null);
        if (timeRangeError is not null)
        {
            return Result<PagedResult<ExceptionLogResponse>>.Failure(timeRangeError);
        }

        var pageStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ExceptionLogSql.CreatePageFilteredSqlServer(
                filter.FromUtc is not null,
                filter.ToUtc is not null,
                filter.ExceptionTypeContains is not null,
                filter.PathContains is not null),
            DatabaseProvider.MySql => ExceptionLogSql.PageFilteredMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                pageStatement,
                new
                {
                    filter.FromUtc,
                    filter.ToUtc,
                    filter.ExceptionTypeContains,
                    filter.PathContains,
                    Offset = offset,
                    PageSize = pageSize,
                },
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>()
                        .ConfigureAwait(false);
                    var rows = await reader.ReadAsync<ExceptionLogRecord>()
                        .ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<ExceptionLogResponse>>.Success(
            new PagedResult<ExceptionLogResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<ExceptionLogResponse>> GetByIdAsync(
        Guid exceptionLogId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<ExceptionLogRecord>(
                ExceptionLogSql.FindById,
                new { ExceptionLogId = exceptionLogId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<ExceptionLogResponse>.Failure(new Error(
                AuditingErrorCodes.ExceptionLogNotFound,
                "The exception log entry was not found.",
                ErrorType.NotFound));
        }

        return Result<ExceptionLogResponse>.Success(Map(record));
    }

    private static ExceptionLogFilter BuildFilter(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? exceptionTypeContains,
        string? pathContains)
    {
        string? typeFragment = null;
        if (!string.IsNullOrWhiteSpace(exceptionTypeContains))
        {
            typeFragment = exceptionTypeContains.Trim();
            if (typeFragment.Length > 200)
            {
                typeFragment = typeFragment[..200];
            }
        }

        string? pathFragment = null;
        if (!string.IsNullOrWhiteSpace(pathContains))
        {
            pathFragment = pathContains.Trim();
            if (pathFragment.Length > 200)
            {
                pathFragment = pathFragment[..200];
            }
        }

        return new ExceptionLogFilter(fromUtc, toUtc, typeFragment, pathFragment);
    }

    private static ExceptionLogResponse Map(ExceptionLogRecord record) =>
        new(
            record.Id,
            record.OccurredAtUtc,
            record.ExceptionType,
            SafeExceptionMessage,
            null,
            record.HttpMethod,
            record.RequestPath,
            record.UserId,
            record.TenantId,
            record.TraceId,
            record.ClientIpFingerprint);

    private sealed record ExceptionLogFilter(
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        string? ExceptionTypeContains,
        string? PathContains);

    internal sealed class ExceptionLogRecord
    {
        public Guid Id { get; init; }

        public DateTimeOffset OccurredAtUtc { get; init; }

        public string ExceptionType { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string? StackTrace { get; init; }

        public string? HttpMethod { get; init; }

        public string? RequestPath { get; init; }

        public Guid? UserId { get; init; }

        public Guid? TenantId { get; init; }

        public string? TraceId { get; init; }

        public string? ClientIpFingerprint { get; init; }
    }
}
