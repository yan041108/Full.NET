using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>当前可信租户的活动机构候选目录，不接受客户端租户覆盖。</summary>
/// <remarks>仅提供候选，不表示机构成员已经取得消费方资源的读取权。</remarks>
public interface ICurrentTenantOrganizationUnitDirectory
{
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
