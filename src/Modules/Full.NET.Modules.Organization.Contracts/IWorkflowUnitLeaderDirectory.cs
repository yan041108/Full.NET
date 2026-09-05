using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>为 Workflow 提供租户机构单元与负责人批量解析的最小只读目录。</summary>
public interface IWorkflowUnitLeaderDirectory
{
    /// <summary>分页读取当前可信租户内可配置为办理人来源的活动机构单元。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动机构单元候选分页结果。</returns>
    Task<PagedResult<WorkflowOrganizationUnitDirectoryEntry>> ListActiveUnitsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>批量校验机构单元是否仍处于活动状态且属于当前可信租户。</summary>
    /// <param name="unitIds">待校验的机构单元标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>有效机构单元的目录项；无效或跨租户单元不会进入结果。</returns>
    Task<IReadOnlyDictionary<Guid, WorkflowOrganizationUnitDirectoryEntry>> FindActiveUnitsAsync(
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken = default);

    /// <summary>按机构单元批量解析当前租户内的负责人用户标识。</summary>
    /// <param name="unitIds">待解析的机构单元标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以机构单元标识为键、负责人用户标识为值；无法解析的单元不会进入结果。</returns>
    Task<IReadOnlyDictionary<Guid, Guid>> FindActiveUnitLeaderUserIdsAsync(
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken = default);

    /// <summary>解析发起人主部门在当前租户内的负责人用户标识。</summary>
    /// <param name="initiatorUserId">工作流实例发起人标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>负责人用户标识；发起人无主部门或负责人不存在时返回 <see langword="null"/>。</returns>
    Task<Guid?> FindInitiatorPrimaryUnitLeaderUserIdAsync(
        Guid initiatorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>Workflow 设计器与发布校验使用的最小机构单元投影。</summary>
/// <param name="Id">稳定机构单元标识。</param>
/// <param name="Code">稳定机构编码。</param>
/// <param name="Name">机构显示名称。</param>
public sealed record WorkflowOrganizationUnitDirectoryEntry(
    Guid Id,
    string Code,
    string Name);
