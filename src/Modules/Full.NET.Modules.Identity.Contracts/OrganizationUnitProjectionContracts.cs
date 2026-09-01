using Full.NET.Abstractions.Results;
using global::MemoryPack;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Identity 消费方机构单元投影集成事件的稳定消息类型。</summary>
public static class IdentityOrganizationUnitProjectionIntegrationEventTypes
{
    /// <summary>租户机构单元状态已提交变更。</summary>
    public const string UnitChanged = "fullnet.organization.unit.changed";
}

/// <summary>机构单元投影对账 Host 权限码。</summary>
public static class IdentityOrganizationUnitProjectionPermissions
{
    /// <summary>执行 dry-run 对账并读取差异报告。</summary>
    public const string ReconcileDryRun =
        "identity.organization_unit_projections.reconcile_dry_run";

    /// <summary>执行 apply 对账并写入 Identity 本地投影。</summary>
    public const string ReconcileApply =
        "identity.organization_unit_projections.reconcile_apply";
}

/// <summary>机构单元投影对账模式稳定机器码。</summary>
public static class IdentityOrganizationUnitProjectionReconciliationModes
{
    /// <summary>只读对账，不写入投影表。</summary>
    public const string DryRun = "dry-run";

    /// <summary>按差异写入缺失或过期投影行。</summary>
    public const string Apply = "apply";
}

/// <summary>表示租户机构单元创建、更新或禁用已与业务状态原子提交。</summary>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="UnitId">机构单元稳定标识。</param>
/// <param name="Name">变更后的机构单元名称。</param>
/// <param name="IsActive">变更后是否处于活动状态。</param>
/// <param name="Version">机构单元单调递增版本号。</param>
/// <param name="ChangedAtUtc">变更提交时间（UTC）。</param>
[MemoryPackable]
public partial record IdentityOrganizationUnitChangedIntegrationEvent(
    Guid TenantId,
    Guid UnitId,
    string Name,
    bool IsActive,
    long Version,
    DateTimeOffset ChangedAtUtc);

/// <summary>机构单元投影回填与对账所需的最小只读快照。</summary>
/// <param name="UnitId">机构单元稳定标识。</param>
/// <param name="Name">机构单元名称。</param>
/// <param name="IsActive">是否处于活动状态。</param>
/// <param name="Version">机构单元单调递增版本号。</param>
/// <param name="ChangedAtUtc">最近一次变更提交时间（UTC）。</param>
public sealed record IdentityOrganizationUnitProjectionSnapshot(
    Guid UnitId,
    string Name,
    bool IsActive,
    long Version,
    DateTimeOffset ChangedAtUtc);

/// <summary>按 UnitId 递增的 keyset 分页结果。</summary>
/// <param name="Items">当前页的机构单元快照集合。</param>
/// <param name="NextAfterUnitId">下一页起点游标；不存在更多页时为 <see langword="null"/>。</param>
/// <param name="HasMore">是否仍有后续页。</param>
public sealed record IdentityOrganizationUnitProjectionPage(
    IReadOnlyList<IdentityOrganizationUnitProjectionSnapshot> Items,
    Guid? NextAfterUnitId,
    bool HasMore);

/// <summary>单页机构单元投影对账请求；仅接受 keyset 游标，不接受页码或偏移量。</summary>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="AfterUnitId">当前页起点游标；首页传 <see langword="null"/>。</param>
/// <param name="PageSize">单页行数；服务端会限制在 1-100 的有界范围内。</param>
/// <param name="Mode">对账模式机器码；参见 <see cref="IdentityOrganizationUnitProjectionReconciliationModes"/>。</param>
public sealed record ReconcileOrganizationUnitProjectionRequest(
    Guid TenantId,
    Guid? AfterUnitId,
    int PageSize,
    string Mode);

/// <summary>单页机构单元投影对账结果。</summary>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="Scanned">本页扫描的源端快照行数。</param>
/// <param name="Missing">Identity 侧缺失、需要回填的投影行数。</param>
/// <param name="Stale">Identity 侧版本落后、需要更新的投影行数。</param>
/// <param name="Extra">Identity 侧冗余、需要清理的投影行数。</param>
/// <param name="Applied">apply 模式下实际写入或删除的行数；dry-run 模式下恒为 0。</param>
/// <param name="NextAfterUnitId">下一页起点游标；不存在更多页时为 <see langword="null"/>。</param>
/// <param name="HasMore">是否仍有后续页。</param>
/// <param name="IsComplete">整个对账流程是否已到达末页并结束。</param>
public sealed record ReconcileOrganizationUnitProjectionResponse(
    Guid TenantId,
    int Scanned,
    int Missing,
    int Stale,
    int Extra,
    int Applied,
    Guid? NextAfterUnitId,
    bool HasMore,
    bool IsComplete);

/// <summary>Organization 向 Identity 投影提供的批量只读目录端口。</summary>
public interface IIdentityOrganizationUnitProjectionSource
{
    /// <summary>
    /// 按租户 keyset 列出机构单元投影快照。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="afterUnitId">当前页起点游标；首页传 <see langword="null"/>。</param>
    /// <param name="pageSize">单页行数；调用方应使用受控上限。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>按 UnitId 递增的 keyset 分页结果。</returns>
    Task<Result<IdentityOrganizationUnitProjectionPage>> ListAsync(
        Guid tenantId,
        Guid? afterUnitId,
        int pageSize,
        CancellationToken cancellationToken = default);
}