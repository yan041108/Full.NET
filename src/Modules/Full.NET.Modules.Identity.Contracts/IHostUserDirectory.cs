namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 供其他模块校验 Host 用户是否存在的只读目录。
/// </summary>
public interface IHostUserDirectory
{
    /// <summary>
    /// 查找活动 Host 用户；不存在或已禁用时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="userId">待查询的 Host 用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>存在且活动时返回最小只读投影，否则返回 <see langword="null"/>。</returns>
    Task<HostUserDirectoryEntry?> FindActiveHostUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

/// <summary>供其他模块批量补齐 Host 用户显示信息的只读目录。</summary>
public interface IHostUserDisplayDirectory
{
    /// <summary>
    /// 批量读取已存在的 Host 用户显示投影；结果包含禁用用户，供历史关系展示。
    /// </summary>
    /// <remarks>
    /// 调用方应一次传入当前页面的用户集合，避免逐行查询；不存在的用户不会进入结果。
    /// </remarks>
    /// <param name="userIds">待补齐显示信息的用户标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以用户标识为键的只读投影字典。</returns>
    Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindHostUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Host 用户目录项（跨模块只读投影）。</summary>
/// <param name="Id">Host 用户标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record HostUserDirectoryEntry(
    Guid Id,
    string Username,
    string DisplayName);
