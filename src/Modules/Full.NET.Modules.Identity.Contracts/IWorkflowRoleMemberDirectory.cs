using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>为 Workflow 提供可信作用域内活动角色与成员批量解析的最小只读目录。</summary>
public interface IWorkflowRoleMemberDirectory
{
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

    /// <summary>按角色批量解析当前可信作用域内的活动成员用户标识。</summary>
    /// <param name="roleIds">待解析的角色标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以角色标识为键、去重且稳定排序后的活动用户标识列表为值。</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindActiveMemberUserIdsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
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
