using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostOperationLogs;

/// <summary>Host 操作日志分页列表与详情只读查询。</summary>
internal sealed class HostOperationLogQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OperationLogResponse>>> ListAsync(
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? httpMethod,
        bool? succeeded,
        string? pathContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = BuildFilter(fromUtc, toUtc, httpMethod, succeeded, pathContains);
        var pageStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OperationLogSql.CreatePageFilteredSqlServer(
                filter.FromUtc is not null,
                filter.ToUtc is not null,
                filter.HttpMethod is not null,
                filter.Succeeded is not null,
                filter.PathContains is not null),
            DatabaseProvider.MySql => OperationLogSql.PageFilteredMySql,
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
                    filter.Succeeded,
                    filter.PathContains,
                    Offset = offset,
                    PageSize = pageSize,
                },
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>()
                        .ConfigureAwait(false);
                    var rows = await reader.ReadAsync<OperationLogRecord>()
                        .ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<OperationLogResponse>>.Success(
            new PagedResult<OperationLogResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<OperationLogResponse>> GetByIdAsync(
        Guid operationLogId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OperationLogRecord>(
                OperationLogSql.FindById,
                new { OperationLogId = operationLogId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<OperationLogResponse>.Failure(new Error(
                AuditingErrorCodes.OperationLogNotFound,
                "The operation log entry was not found.",
                ErrorType.NotFound));
        }

        return Result<OperationLogResponse>.Success(Map(record));
    }

    private static OperationLogFilter BuildFilter(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? httpMethod,
        bool? succeeded,
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

        return new OperationLogFilter(
            fromUtc,
            toUtc,
            normalizedMethod,
            succeeded,
            pathFragment);
    }

    private static OperationLogResponse Map(OperationLogRecord record) =>
        new(
            record.Id,
            record.OccurredAtUtc,
            record.ActionKey,
            record.HttpMethod,
            record.RequestPath,
            record.StatusCode,
            record.DurationMs,
            record.Succeeded,
            record.UserId,
            record.TenantId,
            record.TraceId,
            record.ClientIpFingerprint,
            record.PermissionCode);

    private sealed record OperationLogFilter(
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        string? HttpMethod,
        bool? Succeeded,
        string? PathContains);

    internal sealed class OperationLogRecord
    {
        public Guid Id { get; init; }

        public DateTimeOffset OccurredAtUtc { get; init; }

        public string ActionKey { get; init; } = string.Empty;

        public string HttpMethod { get; init; } = string.Empty;

        public string RequestPath { get; init; } = string.Empty;

        public int StatusCode { get; init; }

        public int DurationMs { get; init; }

        public bool Succeeded { get; init; }

        public Guid? UserId { get; init; }

        public Guid? TenantId { get; init; }

        public string? TraceId { get; init; }

        public string? ClientIpFingerprint { get; init; }

        public string? PermissionCode { get; init; }
    }
}
