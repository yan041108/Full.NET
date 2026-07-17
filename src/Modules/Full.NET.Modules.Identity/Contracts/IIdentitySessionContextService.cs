using System.Security.Claims;
using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 在受信任的认证会话中变更宿主管理员的有效租户上下文。
/// </summary>
public interface IIdentitySessionContextService
{
    /// <summary>
    /// 使用已认证主体和 Tenancy 验证后的租户摘要更新当前会话。
    /// </summary>
    /// <param name="principal">由认证中间件验证的当前主体。</param>
    /// <param name="tenant">目标租户；空值表示返回 Host。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>包含新 Access Token 的上下文切换结果。</returns>
    Task<Result<TenantContextTokenResponse>> ChangeAsync(
        ClaimsPrincipal principal,
        VerifiedTenantContext? tenant,
        CancellationToken cancellationToken = default);
}
