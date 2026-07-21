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

/// <summary>Host 用户目录项（跨模块只读投影）。</summary>
public sealed record HostUserDirectoryEntry(
    Guid Id,
    string Username,
    string DisplayName);
