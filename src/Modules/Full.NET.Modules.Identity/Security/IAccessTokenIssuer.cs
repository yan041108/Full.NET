using Full.NET.Modules.Identity.Domain;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// Access Token 签发器抽象。根据当前用户、会话、活动租户与权限集合，
/// 结合 RsaSigningKeyRing 输出签名后的 JWT Access Token。
/// </summary>
internal interface IAccessTokenIssuer
{
    /// <summary>
    /// 签发签名后的 Access Token。
    /// </summary>
    /// <param name="user">已验证的用户实体。</param>
    /// <param name="sessionId">Refresh Session 标识，便于按会话粒度吊销。</param>
    /// <param name="activeTenantId">用户当前切换的租户（非空时覆盖用户所属 TenantId）。</param>
    /// <param name="permissions">普通用户写进 Claim 的权限码集合；超管留空以动态投影。</param>
    /// <param name="isSuperAdministrator">是否在令牌中写入 SuperAdministrator=true Claim。</param>
    IssuedAccessToken Issue(
        IdentityUser user,
        Guid sessionId,
        Guid? activeTenantId,
        IReadOnlyCollection<string> permissions,
        bool isSuperAdministrator);
}

/// <summary>
/// 已签发 Access Token 结果；返回给调用方用于构造 TokenResponse。
/// </summary>
/// <param name="AccessToken">Compact Serialization 格式的 JWT 字符串。</param>
/// <param name="ExpiresAtUtc">令牌过期时间，与 JWT exp 声明对齐。</param>
internal sealed record IssuedAccessToken(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);
