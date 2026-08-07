using Full.NET.Abstractions.Results;
using MessagePack;

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
        "identity.organization_unit_projection.reconcile.dry_run";

    /// <summary>执行 apply 对账并写入 Identity 本地投影。</summary>
    public const string ReconcileApply =
        "identity.organization_unit_projection.reconcile.apply";
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
[MessagePackObject]
public sealed record IdentityOrganizationUnitChangedIntegrationEvent(
    [property: Key(0)] Guid TenantId,
    [property: Key(1)] Guid UnitId,
    [property: Key(2)] string Name,
    [property: Key(3)] bool IsActive,
    [property: Key(4)] long Version,
    [property: Key(5)] DateTimeOffset ChangedAtUtc);

/// <summary>机构单元投影回填与对账所需的最小只读快照。</summary>
public sealed record IdentityOrganizationUnitProjectionSnapshot(
    Guid UnitId,
    string Name,
    bool IsActive,
    long Version,
    DateTimeOffset ChangedAtUtc);

/// <summary>按 UnitId 递增的 keyset 分页结果。</summary>
public sealed record IdentityOrganizationUnitProjectionPage(
    IReadOnlyList<IdentityOrganizationUnitProjectionSnapshot> Items,
    Guid? NextAfterUnitId,
    bool HasMore);

/// <summary>单页机构单元投影对账请求；仅接受 keyset 游标，不接受页码或偏移量。</summary>
public sealed record ReconcileOrganizationUnitProjectionRequest(
    Guid TenantId,
    Guid? AfterUnitId,
    int PageSize,
    string Mode);

/// <summary>单页机构单元投影对账结果。</summary>
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
    /// <summary>按租户 keyset 列出机构单元投影快照。</summary>
    Task<Result<IdentityOrganizationUnitProjectionPage>> ListAsync(
        Guid tenantId,
        Guid? afterUnitId,
        int pageSize,
        CancellationToken cancellationToken = default);
}