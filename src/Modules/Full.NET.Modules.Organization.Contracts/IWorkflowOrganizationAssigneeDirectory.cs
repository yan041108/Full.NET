using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>为 Workflow 提供可信租户组织、岗位成员与负责人解析的最小只读目录。</summary>
public interface IWorkflowOrganizationAssigneeDirectory
{
    /// <summary>读取明确的直属上级，不从主部门负责人或岗位名册推断；消费方另行验证当前成员身份。</summary>
    Task<WorkflowDirectManagerResolution> ResolveDirectManagerAsync(Guid initiatorUserId, CancellationToken cancellationToken = default);
    /// <summary>检查指定用户在当前租户是否仍属于任一活动岗位，不读取完整岗位名册。</summary>
    /// <param name="userId">经过消费方身份边界验证的实际用户标识。</param>
    /// <param name="positionIds">发布策略中的岗位标识，最多五个。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>仍有活动岗位隶属时为真；账号活动状态由Identity校验。</returns>
    Task<bool> IsActiveMemberOfAnyPositionAsync(Guid userId, IReadOnlyCollection<Guid> positionIds, CancellationToken cancellationToken = default);
    /// <summary>检查指定用户在当前租户是否仍属于任一活动机构，不读取完整机构名册。</summary>
    /// <param name="userId">经过消费方身份边界验证的实际用户标识。</param>
    /// <param name="unitIds">发布策略中的机构标识，最多八个。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>仍有活动机构隶属时为真；账号活动状态由Identity校验。</returns>
    Task<bool> IsActiveMemberOfAnyUnitAsync(Guid userId, IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default);
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

    /// <summary>读取显式机构领导权；有效空结果与无效组织来源分开，组织环不能变成无人自动放行。</summary>
    Task<WorkflowOrganizationLeaderResolution> ResolveLeaderSourceAsync(Guid initiatorUserId,
        WorkflowOrganizationLeaderSource source, CancellationToken cancellationToken = default);

    /// <summary>分页选择当前租户的活动岗位，不开放跨租户目录。</summary>
    Task<PagedResult<WorkflowOrganizationPositionDirectoryEntry>> ListActivePositionsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>批量核对至多五个岗位的存在性及所属机构活动状态。</summary>
    Task<IReadOnlyDictionary<Guid, WorkflowOrganizationPositionDirectoryEntry>> FindActivePositionsAsync(
        IReadOnlyCollection<Guid> positionIds, CancellationToken cancellationToken = default);

    /// <summary>按岗位批量读取有界成员，空列表表示活动岗位当前无人，缺键表示无效岗位。</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindActivePositionMemberUserIdsAsync(
        IReadOnlyCollection<Guid> positionIds, int maximumMembersPerPosition, CancellationToken cancellationToken = default);

    /// <summary>按机构批量读取有界成员，保留活动空机构与无效机构的区别。</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindActiveUnitMemberUserIdsAsync(
        IReadOnlyCollection<Guid> unitIds, int maximumMembersPerUnit, CancellationToken cancellationToken = default);

}

/// <summary>Workflow 设计器与发布校验使用的最小机构单元投影。</summary>
/// <param name="Id">稳定机构单元标识。</param>
/// <param name="Code">稳定机构编码。</param>
/// <param name="Name">机构显示名称。</param>
public sealed record WorkflowOrganizationUnitDirectoryEntry(
    Guid Id,
    string Code,
    string Name);

/// <summary>Workflow 岗位选择器使用的最小活动岗位信息。</summary>
public sealed record WorkflowOrganizationPositionDirectoryEntry(Guid Id, string Code, string Name);

/// <summary>主管解析的组织依据；层级一为发起人主部门，根节点可以提前结束。</summary>
public sealed record WorkflowOrganizationLeaderDirectoryEntry(Guid UnitId, Guid UserId, int Level);
