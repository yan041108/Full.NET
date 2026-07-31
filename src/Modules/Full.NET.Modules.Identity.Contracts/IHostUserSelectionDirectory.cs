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
    /// <summary>分页读取活动 Host 用户的最小候选投影。</summary>
    Task<PagedResult<HostUserDirectoryEntry>> ListActiveHostUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
