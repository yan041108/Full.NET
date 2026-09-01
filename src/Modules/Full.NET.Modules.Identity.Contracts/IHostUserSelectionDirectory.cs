using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 为已完成授权校验的业务模块提供可分配 Host 用户候选目录。
/// </summary>
/// <remarks>
/// 调用方必须在自身 Endpoint 上实施精确权限校验；目录自身只负责 Host 边界、活动状态与分页。
/// </remarks>
public interface IHostUserSelectionDirectory
{
    /// <summary>
    /// 分页读取活动 Host 用户的最小候选投影。
    /// </summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">单页返回数量；调用方应使用受控上限，避免把目录查询退化为全表扫描。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>仅包含分配场景所需字段的分页结果。</returns>
    Task<PagedResult<HostUserDirectoryEntry>> ListActiveHostUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
