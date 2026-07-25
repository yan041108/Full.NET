using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostExceptionLogs;

/// <summary>Host 异常日志分页列表与详情只读查询。</summary>
internal sealed class HostExceptionLogQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
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
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                ExceptionLogSql.CountFilteredSqlServer,
                ExceptionLogSql.ListFilteredSqlServer),
            DatabaseProvider.MySql => (
                ExceptionLogSql.CountFilteredMySql,
                ExceptionLogSql.ListFilteredMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<ExceptionLogRecord>(
                listStatement,
                new
                {
                    filter.FromUtc,
                    filter.ToUtc,
                    filter.ExceptionTypeContains,
                    filter.PathContains,
                    Offset = offset,
                    PageSize = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<ExceptionLogResponse>>.Success(
            new PagedResult<ExceptionLogResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
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
            record.Message,
            record.StackTrace,
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
