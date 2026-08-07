using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 为消费方回填机构单元投影提供批量只读目录；调用方不得直接读取 Organization 表。
/// </summary>
public interface IOrganizationUnitProjectionCatalog
{
    /// <summary>按租户分页列出机构单元投影快照。</summary>
    Task<Result<PagedResult<OrganizationUnitProjectionSnapshot>>> ListUnitSnapshotsAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>机构单元投影回填所需的最小只读快照。</summary>
public sealed record OrganizationUnitProjectionSnapshot(
    Guid UnitId,
    string Name,
    bool IsActive,
    long Version,
    DateTimeOffset ChangedAtUtc);
