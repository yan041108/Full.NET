using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;

/// <summary>Host 访问日志分页列表与详情只读查询。</summary>
internal sealed class HostAccessLogQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<AccessLogResponse>>> ListAsync(
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? httpMethod,
        int? statusCode,
        string? pathContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = BuildFilter(fromUtc, toUtc, httpMethod, statusCode, pathContains);
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                AccessLogSql.CountFilteredSqlServer,
                AccessLogSql.ListFilteredSqlServer),
            DatabaseProvider.MySql => (
                AccessLogSql.CountFilteredMySql,
                AccessLogSql.ListFilteredMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AccessLogRecord>(
                listStatement,
                new
                {
                    filter.FromUtc,
                    filter.ToUtc,
                    filter.HttpMethod,
                    filter.StatusCode,
                    filter.PathContains,
                    Offset = offset,
                    PageSize = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<AccessLogResponse>>.Success(
            new PagedResult<AccessLogResponse>(items, page, pageSize, total));
    }

    public async Task<Result<AccessLogResponse>> GetByIdAsync(
        Guid accessLogId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<AccessLogRecord>(
                AccessLogSql.FindById,
                new { AccessLogId = accessLogId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<AccessLogResponse>.Failure(new Error(
                AuditingErrorCodes.AccessLogNotFound,
                "The access log entry was not found.",
                ErrorType.NotFound));
        }

        return Result<AccessLogResponse>.Success(Map(record));
    }

    private static AccessLogFilter BuildFilter(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? httpMethod,
        int? statusCode,
        string? pathContains)
    {
        var normalizedMethod = string.IsNullOrWhiteSpace(httpMethod)
            ? null
            : httpMethod.Trim().ToUpperInvariant();
        if (normalizedMethod is { Length: > 16 })
        {
            normalizedMethod = normalizedMethod[..16];
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

        return new AccessLogFilter(
            fromUtc,
            toUtc,
            normalizedMethod,
            statusCode,
            pathFragment);
    }

    private sealed record AccessLogFilter(
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        string? HttpMethod,
        int? StatusCode,
        string? PathContains);

    internal static AccessLogResponse Map(AccessLogRecord record) =>
        new(
            record.Id,
            record.OccurredAtUtc,
            record.HttpMethod,
            record.RequestPath,
            record.StatusCode,
            record.DurationMs,
            record.UserId,
            record.TenantId,
            record.TraceId,
            record.ClientIpFingerprint,
            record.IsAuthenticated);

    internal sealed class AccessLogRecord
    {
        public Guid Id { get; init; }

        public DateTimeOffset OccurredAtUtc { get; init; }

        public string HttpMethod { get; init; } = string.Empty;

        public string RequestPath { get; init; } = string.Empty;

        public int StatusCode { get; init; }

        public int DurationMs { get; init; }

        public Guid? UserId { get; init; }

        public Guid? TenantId { get; init; }

        public string? TraceId { get; init; }

        public string? ClientIpFingerprint { get; init; }

        public bool IsAuthenticated { get; init; }
    }
}
