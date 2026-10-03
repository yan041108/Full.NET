using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Identity 权威活动普通角色目录，供 AI 所有者选择当前可信作用域的授权候选。</summary>
/// <remarks>候选不是授权决定；不接受请求租户或令牌角色声明，也不包含超级管理员角色。</remarks>
public interface IActiveRoleDirectory
{
    /// <summary>分页读取当前可信 Host 或租户范围内的活动普通角色。</summary>
    /// <param name="page">从 1 开始的页码，偏移量不得超过 100000。</param>
    /// <param name="pageSize">单页数量，范围为 1 到 100。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>按稳定编码与标识排序的最小角色投影和总数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">分页超出支持范围。</exception>
    /// <exception cref="Full.NET.Abstractions.Tenancy.TenantContextMissingException">非 Host 请求缺少可信租户上下文。</exception>
    Task<PagedResult<ActiveRoleDirectoryEntry>> ListActiveRolesAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>一次验证有界角色集合，返回当前可信范围仍活动的普通角色。</summary>
    /// <param name="roleIds">最多 100 个非空唯一角色标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>有效角色的最小投影，不返回其他范围的存在性。</returns>
    Task<IReadOnlyDictionary<Guid, ActiveRoleDirectoryEntry>> FindActiveRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);

    /// <summary>读取指定主体在当前可信范围内的权威活动普通角色关系。</summary>
    /// <param name="userId">消费方从可信认证上下文取得的主体标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动账号与活动租户成员关系共同允许的角色标识；无效主体返回空集合。</returns>
    Task<IReadOnlyList<Guid>> ListActiveRoleIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Identity 拥有的跨模块最小角色投影，不携带成员或权限明细。</summary>
/// <param name="Id">角色稳定标识。</param>
/// <param name="Code">稳定机器编码。</param>
/// <param name="Name">显示名称。</param>
public sealed record ActiveRoleDirectoryEntry(Guid Id, string Code, string Name);
