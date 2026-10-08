namespace Full.NET.Modules.Identity.Contracts;

/// <summary>向已授权业务提供当前可信租户内活动成员的批量目录，成员关系以租户成员表为准。</summary>
public interface ITenantMemberBatchSelectionDirectory
{
    /// <summary>批量查找活动成员及活动 Host 用户资料；禁止由调用方传入租户标识。</summary>
    /// <param name="userIds">待校验的稳定用户标识集合；重复项只查询一次。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>以用户标识为键的活动成员目录；不存在、停用或其他租户的用户不在结果内。</returns>
    Task<IReadOnlyDictionary<Guid, TenantUserDirectoryEntry>> FindActiveTenantMembersAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
