using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>当前可信租户的活动机构候选目录，不接受客户端租户覆盖。</summary>
/// <remarks>仅提供候选，不表示机构成员已经取得消费方资源的读取权。</remarks>
public interface ICurrentTenantOrganizationUnitDirectory
{
    /// <summary>批量核对当前可信租户的活动机构，用于消费方授予前校验。</summary>
    /// <param name="unitIds">最多 100 个唯一非空机构标识，空集合返回空目录。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>仅包含当前租户内活动机构的最小投影。</returns>
    /// <exception cref="ArgumentOutOfRangeException">集合超过上限或含空标识、重复标识。</exception>
    /// <exception cref="Full.NET.Abstractions.Tenancy.TenantContextMissingException">Host 或可信租户上下文缺失。</exception>
    Task<IReadOnlyDictionary<Guid, TenantOrganizationUnitDirectoryEntry>> FindActiveUnitsAsync(
        IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default);

    /// <summary>读取当前可信租户内用户的完整活动机构关系；机构与隶属必须同时活动。</summary>
    /// <param name="userId">消费方已授权的当前主体，不能来自客户端覆盖。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>完整、去重的明确机构标识，不包含父子推导或跨租户关系。</returns>
    /// <exception cref="Full.NET.Abstractions.Tenancy.TenantContextMissingException">Host 或可信租户上下文缺失。</exception>
    Task<IReadOnlyList<Guid>> ListActiveUnitIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>分页返回当前可信租户的活动机构，按编码与标识稳定排序。</summary>
    /// <param name="page">从 1 开始的页码，偏移不得超过 100000。</param>
    /// <param name="pageSize">每页 1 到 100 项。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>最小机构投影和同一过滤范围的总数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">分页超出支持范围。</exception>
    /// <exception cref="Full.NET.Abstractions.Tenancy.TenantContextMissingException">Host 或缺少完整可信租户上下文。</exception>
    Task<PagedResult<TenantOrganizationUnitDirectoryEntry>> ListActiveUnitsAsync(int page, int pageSize,
        CancellationToken cancellationToken = default);
}
