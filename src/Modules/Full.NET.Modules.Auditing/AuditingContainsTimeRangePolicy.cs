using Full.NET.Abstractions.Results;
using Full.NET.Modules.Auditing.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing;

/// <summary>
/// 在执行数据库查询前统一验证 contains 时间窗，避免三类审计查询的边界发生漂移。
/// </summary>
internal sealed class AuditingContainsTimeRangePolicy(
    IOptions<AuditingQueryOptions> options)
{
    public Error? Validate(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool hasContains)
    {
        if (!hasContains)
        {
            return null;
        }

        if (fromUtc is null || toUtc is null)
        {
            return new Error(
                AuditingErrorCodes.ContainsTimeRangeRequired,
                "Contains queries require both fromUtc and toUtc.",
                ErrorType.Validation);
        }

        if (fromUtc > toUtc)
        {
            return new Error(
                AuditingErrorCodes.TimeRangeInvalid,
                "fromUtc must not be later than toUtc.",
                ErrorType.Validation);
        }

        if (toUtc.Value - fromUtc.Value
            > TimeSpan.FromDays(options.Value.MaximumContainsWindowDays))
        {
            return new Error(
                AuditingErrorCodes.ContainsTimeRangeExceeded,
                "The contains query time range exceeds the configured maximum.",
                ErrorType.Validation);
        }

        return null;
    }
}
