namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 供其他模块校验 Host 用户是否存在的只读目录。
/// </summary>
public interface IHostUserDirectory
{
    /// <summary>
    /// 查找活动 Host 用户；不存在或已禁用时返回 <see langword="null"/>。
    /// </summary>
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
    Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindHostUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Host 用户目录项（跨模块只读投影）。</summary>
public sealed record HostUserDirectoryEntry(
    Guid Id,
    string Username,
    string DisplayName);
