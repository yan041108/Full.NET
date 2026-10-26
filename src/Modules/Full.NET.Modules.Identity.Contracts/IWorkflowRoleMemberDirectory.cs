using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>为 Workflow 提供可信作用域内活动角色与成员批量解析的最小只读目录。</summary>
public interface IWorkflowRoleMemberDirectory
{
    /// <summary>批量核对已选用户和角色的活动交集，避免截断角色名册或逐人查询。</summary>
    /// <param name="roleIds">同一启动计划的角色并集，最多 320 个。</param>
    /// <param name="userIds">已选用户并集，最多 160 个。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>角色及其实际匹配的已选活动成员；始终受当前可信作用域约束。</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindSelectedActiveMembersByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
    /// <summary>分页选择最多五个活动角色的真实活动成员；总数是角色并集，不按全体用户名册截断。</summary>
    /// <param name="roleIds">发布范围中的角色，最多五个。</param>
    /// <param name="page">从一开始的页码。</param>
    /// <param name="pageSize">单页最多一百个用户。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当前可信作用域内去重后的角色成员分页。</returns>
    Task<PagedResult<WorkflowRoleUserDirectoryEntry>> ListActiveMembersOfRolesAsync(IReadOnlyCollection<Guid> roleIds,
        int page, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>按指定用户检查其是否仍属于任一活动角色，不扫描或截断完整角色名册。</summary>
    /// <param name="userId">来自已授权资源的实际用户标识。</param>
    /// <param name="roleIds">发布策略中的角色标识，最多五个。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>指定用户在当前可信作用域中仍有该来源成员关系时为真。</returns>
    Task<bool> IsActiveMemberOfAnyRoleAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);
    /// <summary>分页读取当前可信作用域内可配置为办理人的活动角色。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动角色候选分页结果。</returns>
    Task<PagedResult<WorkflowRoleDirectoryEntry>> ListActiveRolesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>批量校验角色是否仍处于活动状态且属于当前可信作用域。</summary>
    /// <param name="roleIds">待校验的角色标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>有效角色的目录项；无效或跨作用域角色不会进入结果。</returns>
    Task<IReadOnlyDictionary<Guid, WorkflowRoleDirectoryEntry>> FindActiveRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>按角色批量解析有界的活动成员，调用方通过上限加一判断超限，禁止当作完整角色名册。</summary>
    /// <param name="roleIds">待解析的角色标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以角色标识为键的活动成员；活动空角色保留空列表，无效或跨作用域角色缺键。</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindActiveMemberUserIdsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        int maximumMembersPerRole,
        CancellationToken cancellationToken = default);
}

/// <summary>Workflow 设计器与发布校验使用的最小角色投影。</summary>
/// <param name="Id">稳定角色标识。</param>
/// <param name="Code">稳定角色编码。</param>
/// <param name="Name">角色显示名称。</param>
public sealed record WorkflowRoleDirectoryEntry(
    Guid Id,
    string Code,
    string Name);

/// <summary>角色范围选择器使用的活动用户最小投影。</summary>
/// <param name="Id">稳定用户标识。</param>
/// <param name="Username">用户名。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record WorkflowRoleUserDirectoryEntry(Guid Id, string Username, string DisplayName);
