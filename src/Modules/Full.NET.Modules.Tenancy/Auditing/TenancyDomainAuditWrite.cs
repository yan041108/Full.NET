namespace Full.NET.Modules.Tenancy.Auditing;

/// <summary>
/// Tenancy 模块 B0 域内审计写入载荷；只承载单条 <c>fn_tenancy_domain_audit</c> 记录的业务字段，
/// Id、TraceId 与 OccurredAtUtc 由 <see cref="TenancyDomainAuditWriter"/> 在写入时补全。
/// </summary>
/// <param name="ActionKey">
/// 稳定的审计 ActionKey，必须与 <c>Full.NET.Modules.Auditing.AuditReliabilityCatalog</c>
/// 中登记的分类一致。
/// </param>
/// <param name="EntityId">被操作实体的主键，例如被禁用的租户 Id。</param>
/// <param name="TenantId">
/// 本次操作归属的租户范围；对 Host 级操作即为被操作租户自身的 Id，
/// 非租户范围操作可为空。
/// </param>
/// <param name="Outcome">操作结果，固定取值 <c>success</c> 或 <c>failure</c>。</param>
/// <param name="ActorUserId">发起操作的用户 Id；匿名或系统触发时可为空。</param>
/// <param name="ActorDisplayName">发起操作的用户展示名快照；用户后续改名不回溯更新。</param>
/// <param name="DiffSummaryJson">变更摘要的 JSON 文本；无摘要时可为空。</param>
internal sealed record TenancyDomainAuditWrite(
    string ActionKey,
    Guid EntityId,
    Guid? TenantId,
    string Outcome,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string? DiffSummaryJson);
