using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Retention;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing;

/// <summary>导出查询的时间窗、保留策略与行数边界校验。</summary>
internal sealed class AuditingExportTimeRangePolicy(
    IOptions<AuditingQueryOptions> queryOptions,
    IOptions<AuditingRetentionOptions> retentionOptions,
    IClock clock)
{
    /// <summary>校验导出时间范围并返回执行计划。</summary>
    public Result<AuditLogExportPlan> Plan(
        AuditLogExportKind kind,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        if (fromUtc is null || toUtc is null)
        {
            return Result<AuditLogExportPlan>.Failure(new Error(
                AuditingErrorCodes.ExportTimeRangeRequired,
                "Export queries require both fromUtc and toUtc.",
                ErrorType.Validation));
        }

        if (fromUtc > toUtc)
        {
            return Result<AuditLogExportPlan>.Failure(new Error(
                AuditingErrorCodes.TimeRangeInvalid,
                "fromUtc must not be later than toUtc.",
                ErrorType.Validation));
        }

        var windowDays = queryOptions.Value.MaximumExportWindowDays;
        if (toUtc.Value - fromUtc.Value > TimeSpan.FromDays(windowDays))
        {
            return Result<AuditLogExportPlan>.Failure(new Error(
                AuditingErrorCodes.ExportTimeRangeExceeded,
                "The export query time range exceeds the configured maximum.",
                ErrorType.Validation));
        }

        var retentionDays = ResolveRetentionDays(kind);
        var earliestAllowedFromUtc = clock.UtcNow.Subtract(TimeSpan.FromDays(retentionDays));
        if (fromUtc < earliestAllowedFromUtc)
        {
            return Result<AuditLogExportPlan>.Failure(new Error(
                AuditingErrorCodes.ExportRetentionBoundaryExceeded,
                "The export query starts earlier than the configured retention boundary.",
                ErrorType.Validation));
        }

        return Result<AuditLogExportPlan>.Success(
            new AuditLogExportPlan(
                fromUtc.Value,
                toUtc.Value,
                queryOptions.Value.MaximumExportRows,
                retentionDays));
    }

    private int ResolveRetentionDays(AuditLogExportKind kind) =>
        kind switch
        {
            AuditLogExportKind.Access => retentionOptions.Value.AccessRetentionDays,
            AuditLogExportKind.Operation => retentionOptions.Value.OperationRetentionDays,
            AuditLogExportKind.Exception => retentionOptions.Value.ExceptionRetentionDays,
            _ => throw new InvalidOperationException("Unsupported audit log export kind."),
        };
}

/// <summary>审计日志导出种类。</summary>
internal enum AuditLogExportKind
{
    Access,
    Operation,
    Exception,
}

/// <summary>导出执行计划。</summary>
internal readonly record struct AuditLogExportPlan(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MaximumRows,
    int RetentionDays);
