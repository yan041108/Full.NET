namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>审计日志趋势查询权限。</summary>
public static class AuditLogTrendPermissions
{
    /// <summary>按时间范围聚合访问/操作/异常日志趋势。</summary>
    public const string Read = "auditing.trends.read";
}

/// <summary>域审计变更差异只读查询权限。</summary>
public static class DomainChangeDiffPermissions
{
    /// <summary>按 TraceId 查询各模块脱敏后的域审计变更差异。</summary>
    public const string Read = "auditing.change_diff.read";
}

/// <summary>单个时间桶内的审计事件计数。</summary>
public sealed record AuditLogTrendBucketResponse(
    DateTimeOffset BucketStartUtc,
    long EventCount,
    long ErrorCount);

/// <summary>审计日志时间范围聚合响应。</summary>
public sealed record AuditLogTrendResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int BucketSizeMinutes,
    IReadOnlyList<AuditLogTrendBucketResponse> Buckets,
    long TotalCount,
    bool BucketLimitReached);

/// <summary>域审计变更差异字段响应。</summary>
public sealed record DomainAuditChangeDiffFieldResponse(
    string FieldKey,
    string? BeforeValue,
    string? AfterValue);

/// <summary>单条域审计变更差异响应。</summary>
public sealed record DomainAuditChangeDiffEntryResponse(
    Guid AuditId,
    string ModuleKey,
    string ActionKey,
    DateTimeOffset OccurredAtUtc,
    string Availability,
    IReadOnlyList<DomainAuditChangeDiffFieldResponse> Fields);

/// <summary>按 TraceId 聚合的域审计变更差异查询响应。</summary>
public sealed record DomainChangeDiffQueryResponse(
    string TraceId,
    IReadOnlyList<DomainAuditChangeDiffEntryResponse> Entries);
