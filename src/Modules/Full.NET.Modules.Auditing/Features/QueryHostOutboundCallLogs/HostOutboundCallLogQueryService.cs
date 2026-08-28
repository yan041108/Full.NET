using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostOutboundCallLogs;

/// <summary>Host 出站调用审计分页列表与详情只读查询。</summary>
internal sealed class HostOutboundCallLogQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    AuditingContainsTimeRangePolicy containsTimeRangePolicy)
{
    public async Task<Result<PagedResult<OutboundCallLogResponse>>> ListAsync(
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? providerKey,
        bool? succeeded,
        string? operationContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = BuildFilter(fromUtc, toUtc, providerKey, succeeded, operationContains);
        var timeRangeError = containsTimeRangePolicy.Validate(
            filter.FromUtc,
            filter.ToUtc,
            filter.OperationContains is not null);
        if (timeRangeError is not null)
        {
            return Result<PagedResult<OutboundCallLogResponse>>.Failure(timeRangeError);
        }

        var pageStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OutboundCallLogSql.CreatePageFilteredSqlServer(
                filter.FromUtc is not null,
                filter.ToUtc is not null,
                filter.ProviderKey is not null,
                filter.Succeeded is not null,
                filter.OperationContains is not null),
            DatabaseProvider.MySql => OutboundCallLogSql.PageFilteredMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                pageStatement,
                AuditingSqlParameters.Create(
                    ("FromUtc", filter.FromUtc),
                    ("ToUtc", filter.ToUtc),
                    ("ProviderKey", filter.ProviderKey),
                    ("Succeeded", filter.Succeeded),
                    ("OperationContains", filter.OperationContains),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>()
                        .ConfigureAwait(false);
                    var rows = await reader.ReadAsync<OutboundCallLogRecord>()
                        .ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<OutboundCallLogResponse>>.Success(
            new PagedResult<OutboundCallLogResponse>(
                pageResult.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                pageResult.Total));
    }

    public async Task<Result<OutboundCallLogResponse>> GetByIdAsync(
        Guid outboundCallLogId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OutboundCallLogRecord>(
                OutboundCallLogSql.FindById,
                AuditingSqlParameters.Create(("OutboundCallLogId", outboundCallLogId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<OutboundCallLogResponse>.Failure(new Error(
                AuditingErrorCodes.OutboundCallLogNotFound,
                "The outbound call log entry was not found.",
                ErrorType.NotFound));
        }

        return Result<OutboundCallLogResponse>.Success(Map(record));
    }

    private static OutboundCallLogFilter BuildFilter(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? providerKey,
        bool? succeeded,
        string? operationContains)
    {
        string? normalizedProvider = null;
        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            normalizedProvider = providerKey.Trim().ToLowerInvariant();
            if (normalizedProvider.Length > 64)
            {
                normalizedProvider = normalizedProvider[..64];
            }
        }

        string? operationFragment = null;
        if (!string.IsNullOrWhiteSpace(operationContains))
        {
            operationFragment = operationContains.Trim();
            if (operationFragment.Length > 200)
            {
                operationFragment = operationFragment[..200];
            }
        }

        return new OutboundCallLogFilter(
            fromUtc,
            toUtc,
            normalizedProvider,
            succeeded,
            operationFragment);
    }

    private static OutboundCallLogResponse Map(OutboundCallLogRecord record) =>
        new(
            record.Id,
            record.OccurredAtUtc,
            record.ProviderKey,
            record.OperationKey,
            record.DestinationHostCategory,
            record.StatusCode,
            record.Succeeded,
            record.DurationMs,
            record.RetryCount,
            record.TraceId,
            record.SafeErrorCode,
            record.TenantId,
            record.UserId);

    private sealed record OutboundCallLogFilter(
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        string? ProviderKey,
        bool? Succeeded,
        string? OperationContains);
}
