using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ChangeTenantContext;

/// <summary>
/// 请求将当前已认证主体切换到指定租户上下文；空租户表示返回 Host 视角。
/// </summary>
/// <param name="TenantId">目标租户标识；空值表示退出租户上下文。</param>
/// <param name="Principal">已通过认证的当前主体，供会话上下文服务重新签发令牌。</param>
internal sealed record Command(
    Guid? TenantId,
    ClaimsPrincipal Principal)
    : ITransactionalCommand<TenantContextTokenResponse>;
