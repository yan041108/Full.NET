using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;

/// <summary>Host 访问日志分页列表与详情只读查询。</summary>
internal sealed class HostAccessLogQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    AuditingContainsTimeRangePolicy containsTimeRangePolicy)
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
        var timeRangeError = containsTimeRangePolicy.Validate(
            filter.FromUtc,
            filter.ToUtc,
            filter.PathContains is not null);
        if (timeRangeError is not null)
        {
            return Result<PagedResult<AccessLogResponse>>.Failure(timeRangeError);
        }

        var pageStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AccessLogSql.CreatePageFilteredSqlServer(
                filter.FromUtc is not null,
                filter.ToUtc is not null,
                filter.HttpMethod is not null,
                filter.StatusCode is not null,
                filter.PathContains is not null),
            DatabaseProvider.MySql => AccessLogSql.PageFilteredMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                pageStatement,
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
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>()
                        .ConfigureAwait(false);
                    var rows = await reader.ReadAsync<AccessLogRecord>()
                        .ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);
        var items = pageResult.Rows.Select(Map).ToArray();
        return Result<PagedResult<AccessLogResponse>>.Success(
            new PagedResult<AccessLogResponse>(
                items,
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<AccessLogCursorPageResponse>> ListCursorAsync(
        int limit,
        string? cursor,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? httpMethod,
        int? statusCode,
        string? pathContains,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var filter = BuildFilter(
            fromUtc,
            toUtc,
            httpMethod,
            statusCode,
            pathContains);
        var timeRangeError = containsTimeRangePolicy.Validate(
            filter.FromUtc,
            filter.ToUtc,
            filter.PathContains is not null);
        if (timeRangeError is not null)
        {
            return Result<AccessLogCursorPageResponse>.Failure(timeRangeError);
        }

        AccessLogCursorBoundary? boundary = null;
        if (cursor is not null)
        {
            if (!AccessLogCursorCodec.TryDecode(cursor, filter, out var decoded))
            {
                return Result<AccessLogCursorPageResponse>.Failure(new Error(
                    AuditingErrorCodes.AccessLogCursorInvalid,
                    "The access log cursor is invalid or does not match the current filter.",
                    ErrorType.Validation));
            }

            boundary = decoded;
        }

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AccessLogSql.CreateCursorListSqlServer(
                boundary is not null,
                filter.FromUtc is not null,
                filter.ToUtc is not null,
                filter.HttpMethod is not null,
                filter.StatusCode is not null,
                filter.PathContains is not null),
            DatabaseProvider.MySql => boundary is null
                ? AccessLogSql.CursorListFirstMySql
                : AccessLogSql.CursorListAfterMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<AccessLogRecord>(
                statement,
                new
                {
                    filter.FromUtc,
                    filter.ToUtc,
                    filter.HttpMethod,
                    filter.StatusCode,
                    filter.PathContains,
                    CursorOccurredAtUtc = boundary?.OccurredAtUtc,
                    CursorId = boundary?.Id,
                    FetchSize = limit + 1,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var hasMore = rows.Count > limit;
        var selectedRows = rows.Take(limit).ToArray();
        var items = selectedRows.Select(Map).ToArray();
        var nextCursor = hasMore
            ? AccessLogCursorCodec.Encode(
                new AccessLogCursorBoundary(
                    selectedRows[^1].OccurredAtUtc,
                    selectedRows[^1].Id),
                filter)
            : null;
        return Result<AccessLogCursorPageResponse>.Success(
            new AccessLogCursorPageResponse(items, nextCursor, hasMore));
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

    private static AccessLogCursorFilter BuildFilter(
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

        return new AccessLogCursorFilter(
            fromUtc,
            toUtc,
            normalizedMethod,
            statusCode,
            pathFragment);
    }

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
