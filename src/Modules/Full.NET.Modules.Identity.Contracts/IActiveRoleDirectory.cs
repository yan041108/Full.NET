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
}

/// <summary>Identity 拥有的跨模块最小角色投影，不携带成员或权限明细。</summary>
/// <param name="Id">角色稳定标识。</param>
/// <param name="Code">稳定机器编码。</param>
/// <param name="Name">显示名称。</param>
public sealed record ActiveRoleDirectoryEntry(Guid Id, string Code, string Name);
