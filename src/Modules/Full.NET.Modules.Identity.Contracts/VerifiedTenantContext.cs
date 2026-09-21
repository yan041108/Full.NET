namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 表示由 Tenancy 模块确认存在且启用的租户上下文。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">租户标识。</param>
/// <param name="Identifier">稳定租户编码。</param>
/// <param name="Name">租户名称。</param>
/// <param name="Domain">租户主域名。</param>
public sealed record VerifiedTenantContext(
    Guid Id,
    string Identifier,
    string Name,
    string Domain);

/// <summary>
/// 表示上下文切换后客户端可安全展示的上下文摘要。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="TenantId">有效租户标识；Host 上下文为空。</param>
/// <param name="Identifier">租户编码；Host 上下文固定为 host。</param>
/// <param name="Name">上下文显示名称。</param>
/// <param name="Scope">服务端签发的有效作用域。</param>
public sealed record TenantContextDescriptor(
    Guid? TenantId,
    string Identifier,
    string Name,
    string Scope);

/// <summary>
/// 表示上下文切换后签发的新 Access Token 与上下文摘要。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 AccessToken 与 RefreshToken 为短期敏感凭据，禁止落盘或写日志。</remarks>
/// <param name="AccessToken">仅供内存保存的短期 Access Token。</param>
/// <param name="TokenType">固定为 Bearer。</param>
/// <param name="ExpiresAtUtc">Access Token 的 UTC 过期时间。</param>
/// <param name="Context">服务端确认的有效上下文。</param>
/// <param name="RefreshToken">OIDC 切租户后可选的新 refresh token；仅 offline_access 会话返回。</param>
public sealed record TenantContextTokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    TenantContextDescriptor Context,
    string? RefreshToken = null);
