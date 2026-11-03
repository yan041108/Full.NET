namespace Full.NET.Modules.Identity.Contracts;

/// <summary>统一账号的上下文范围；显式平台切换授权可访问全目录，普通账号仅访问活动成员关系。</summary>
public sealed record IdentityTenantContextAccess(bool AllTenants, IReadOnlyCollection<Guid> TenantIds);

/// <summary>由 Identity 权威读取成员归属与显式授权，不依赖客户端权限声明。</summary>
public interface IIdentityTenantContextAccess
{
    Task<IdentityTenantContextAccess> ReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
