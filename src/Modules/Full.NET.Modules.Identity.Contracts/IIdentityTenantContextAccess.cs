namespace Full.NET.Modules.Identity.Contracts;

/// <summary>统一账号的上下文范围；显式平台切换授权可访问全目录，普通账号仅访问活动成员关系。</summary>
public sealed record IdentityTenantContextAccess(bool AllTenants, IReadOnlyCollection<Guid> TenantIds);

/// <summary>由 Identity 权威读取成员归属与显式授权，不依赖客户端权限声明。</summary>
public interface IIdentityTenantContextAccess
{
    /// <summary>由 Identity 权威读取指定用户的活动成员归属与显式平台切换授权。</summary>
    /// <param name="userId">统一账号标识；调用方须已确认其在当前可信作用域内。</param>
    /// <param name="cancellationToken">用于取消读取的令牌。</param>
    /// <returns>用户的租户上下文范围，不依赖客户端权限声明。</returns>
    Task<IdentityTenantContextAccess> ReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
