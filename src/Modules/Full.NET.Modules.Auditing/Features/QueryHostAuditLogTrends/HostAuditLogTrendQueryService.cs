using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostAuditLogTrends;

/// <summary>访问/操作/异常日志的时间范围趋势聚合只读查询。</summary>
internal sealed class HostAuditLogTrendQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    AuditingTrendTimeRangePolicy trendTimeRangePolicy)
{
    /// <summary>聚合访问日志趋势。</summary>
    public Task<Result<AuditLogTrendResponse>> QueryAccessTrendAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? bucketMinutes,
        CancellationToken cancellationToken = default) =>
        QueryTrendAsync(
            AuditLogTrendKind.Access,
            fromUtc,
            toUtc,
            bucketMinutes,
            cancellationToken);

    /// <summary>聚合操作日志趋势。</summary>
    public Task<Result<AuditLogTrendResponse>> QueryOperationTrendAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? bucketMinutes,
        CancellationToken cancellationToken = default) =>
        QueryTrendAsync(
            AuditLogTrendKind.Operation,
            fromUtc,
            toUtc,
            bucketMinutes,
            cancellationToken);

    /// <summary>聚合异常日志趋势。</summary>
    public Task<Result<AuditLogTrendResponse>> QueryExceptionTrendAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? bucketMinutes,
        CancellationToken cancellationToken = default) =>
        QueryTrendAsync(
            AuditLogTrendKind.Exception,
            fromUtc,
            toUtc,
            bucketMinutes,
            cancellationToken);

    private async Task<Result<AuditLogTrendResponse>> QueryTrendAsync(
        AuditLogTrendKind kind,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? bucketMinutes,
        CancellationToken cancellationToken)
    {
        var planResult = trendTimeRangePolicy.Plan(fromUtc, toUtc, bucketMinutes);
        if (!planResult.IsSuccess)
        {
            return Result<AuditLogTrendResponse>.Failure(planResult.Error!);
        }

        var plan = planResult.Value;
        var statement = ResolveStatement(kind);
        var rows = await queryExecutor.QueryAsync<AuditLogTrendBucketRecord>(
                statement,
                AuditingSqlParameters.Create(
                    ("FromUtc", plan.FromUtc),
                    ("ToUtc", plan.ToUtc),
                    ("BucketMinutes", plan.BucketSizeMinutes)),
                cancellationToken)
            .ConfigureAwait(false);

        var buckets = rows
            .Select(row => new AuditLogTrendBucketResponse(
                row.BucketStartUtc,
                row.EventCount,
                row.ErrorCount))
            .ToArray();
        var totalCount = buckets.Sum(bucket => bucket.EventCount);
        return Result<AuditLogTrendResponse>.Success(
            new AuditLogTrendResponse(
                plan.FromUtc,
                plan.ToUtc,
                plan.BucketSizeMinutes,
                buckets,
                totalCount,
                buckets.Length >= plan.ExpectedBucketCount));
    }

    private SqlStatement ResolveStatement(AuditLogTrendKind kind) =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => kind switch
            {
                AuditLogTrendKind.Access => AuditLogTrendSql.AccessTrendSqlServer,
                AuditLogTrendKind.Operation => AuditLogTrendSql.OperationTrendSqlServer,
                AuditLogTrendKind.Exception => AuditLogTrendSql.ExceptionTrendSqlServer,
                _ => throw new InvalidOperationException("Unsupported audit log trend kind."),
            },
            DatabaseProvider.MySql => kind switch
            {
                AuditLogTrendKind.Access => AuditLogTrendSql.AccessTrendMySql,
                AuditLogTrendKind.Operation => AuditLogTrendSql.OperationTrendMySql,
                AuditLogTrendKind.Exception => AuditLogTrendSql.ExceptionTrendMySql,
                _ => throw new InvalidOperationException("Unsupported audit log trend kind."),
            },
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };

    private enum AuditLogTrendKind
    {
        Access,
        Operation,
        Exception,
    }
}
