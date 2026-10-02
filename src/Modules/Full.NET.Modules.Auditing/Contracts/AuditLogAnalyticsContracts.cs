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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="BucketStartUtc">桶起始时间（UTC）。</param>
/// <param name="EventCount">桶内事件总数。</param>
/// <param name="ErrorCount">桶内错误事件数。</param>
public sealed record AuditLogTrendBucketResponse(
    DateTimeOffset BucketStartUtc,
    long EventCount,
    long ErrorCount);

/// <summary>审计日志时间范围聚合响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Buckets 按时间升序，BucketLimitReached 为 true 时调用方应缩窄查询窗口。</remarks>
/// <param name="FromUtc">统计窗口起点（UTC）。</param>
/// <param name="ToUtc">统计窗口终点（UTC）。</param>
/// <param name="BucketSizeMinutes">桶宽（分钟）。</param>
/// <param name="Buckets">按时间升序排列的桶集合。</param>
/// <param name="TotalCount">窗口内事件总数。</param>
/// <param name="BucketLimitReached">是否因桶数上限截断；为 true 时窗口内有未返回桶。</param>
public sealed record AuditLogTrendResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int BucketSizeMinutes,
    IReadOnlyList<AuditLogTrendBucketResponse> Buckets,
    long TotalCount,
    bool BucketLimitReached);

/// <summary>域审计变更差异字段响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。BeforeValue/AfterValue 已脱敏，敏感字段不返回原值。</remarks>
/// <param name="FieldKey">稳定字段键；发布后不可改名。</param>
/// <param name="BeforeValue">变更前值（已脱敏）；无前置值时为 <see langword="null"/>。</param>
/// <param name="AfterValue">变更后值（已脱敏）；无后置值时为 <see langword="null"/>。</param>
public sealed record DomainAuditChangeDiffFieldResponse(
    string FieldKey,
    string? BeforeValue,
    string? AfterValue);

/// <summary>单条域审计变更差异响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Fields 顺序按 Schema 字段序固定，不应在序列化端重排。</remarks>
/// <param name="AuditId">审计记录标识。</param>
/// <param name="ModuleKey">模块键；用于跨模块差异定位。</param>
/// <param name="ActionKey">稳定审计动作键；发布后不可改名。</param>
/// <param name="OccurredAtUtc">动作发生时间（UTC）。</param>
/// <param name="Availability">可用性状态键；说明字段是否脱敏或不可读。</param>
/// <param name="Fields">该条变更的字段差异集合。</param>
public sealed record DomainAuditChangeDiffEntryResponse(
    Guid AuditId,
    string ModuleKey,
    string ActionKey,
    DateTimeOffset OccurredAtUtc,
    string Availability,
    IReadOnlyList<DomainAuditChangeDiffFieldResponse> Fields);

/// <summary>按 TraceId 聚合的域审计变更差异查询响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Entries 顺序按各模块 Schema 固定，不应在序列化端重排。</remarks>
/// <param name="TraceId">链路追踪标识；用于跨模块聚合。</param>
/// <param name="Entries">按 Schema 排序的域审计变更条目集合。</param>
public sealed record DomainChangeDiffQueryResponse(
    string TraceId,
    IReadOnlyList<DomainAuditChangeDiffEntryResponse> Entries);
