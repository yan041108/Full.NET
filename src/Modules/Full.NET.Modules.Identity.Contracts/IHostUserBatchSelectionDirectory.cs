namespace Full.NET.Modules.Identity.Contracts;

/// <summary>为已完成授权校验的业务模块提供活动 Host 用户批量选择目录。</summary>
public interface IHostUserBatchSelectionDirectory
{
    /// <summary>批量查找仍处于活动状态的指定 Host 用户。</summary>
    /// <param name="userIds">待校验的稳定 Host 用户标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以用户标识为键的活动 Host 用户目录；不存在或停用用户不会进入结果。</returns>
    Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindActiveHostUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
