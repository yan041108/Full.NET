namespace Full.NET.Abstractions.Auditing;

/// <summary>域审计变更差异的可读性状态。</summary>
public enum DomainAuditChangeDiffAvailability
{
    /// <summary>已解析出可展示的字段差异。</summary>
    Available,

    /// <summary>审计记录存在但未写入差异摘要。</summary>
    NoDiffRecorded,

    /// <summary>差异摘要存在但无法按约定结构解析。</summary>
    Unparseable,
}

/// <summary>单个字段的前后值差异；值已在模块读取侧完成脱敏。</summary>
/// <param name="FieldKey">稳定字段键。</param>
/// <param name="BeforeValue">变更前展示值；历史未记录时为 <c>null</c>。</param>
/// <param name="AfterValue">变更后展示值；仅新增字段时可为非空。</param>
public sealed record DomainAuditChangeDiffField(
    string FieldKey,
    string? BeforeValue,
    string? AfterValue);

/// <summary>单个模块域审计记录的变更差异读取结果。</summary>
/// <param name="AuditId">域审计记录主键。</param>
/// <param name="ModuleKey">来源模块键。</param>
/// <param name="ActionKey">域审计动作键。</param>
/// <param name="OccurredAtUtc">审计发生时间（UTC）。</param>
/// <param name="Availability">差异可读性状态。</param>
/// <param name="Fields">已脱敏的字段差异列表。</param>
public sealed record DomainAuditChangeDiffEntry(
    Guid AuditId,
    string ModuleKey,
    string ActionKey,
    DateTimeOffset OccurredAtUtc,
    DomainAuditChangeDiffAvailability Availability,
    IReadOnlyList<DomainAuditChangeDiffField> Fields);
