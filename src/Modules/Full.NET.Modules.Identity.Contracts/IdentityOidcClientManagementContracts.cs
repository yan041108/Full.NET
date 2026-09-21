namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OIDC 客户端管理 API 契约。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class IdentityOidcClientPermissions
{
    /// <summary>分页查询 OIDC 客户端列表与详情。</summary>
    public const string Read = "identity.oidc_clients.read";
    /// <summary>创建 OIDC 客户端；ClientId 与 RedirectUris 发布后不可改名。</summary>
    public const string Create = "identity.oidc_clients.create";
    /// <summary>更新 OIDC 客户端元数据；RedirectUris 与 Scopes 调整需考虑向后兼容。</summary>
    public const string Update = "identity.oidc_clients.update";
    /// <summary>停用 OIDC 客户端；停用后已签发 Token 在过期前可能仍有效。</summary>
    public const string Disable = "identity.oidc_clients.disable";
    /// <summary>轮换 OIDC 客户端密钥；新密钥一次性返回，旧密钥立即失效。</summary>
    public const string Rotate = "identity.oidc_clients.rotate";
}

/// <summary>创建 OIDC 客户端请求。</summary>
/// <remarks>ClientId 一旦发布即不可改名；RedirectUris 与 Scopes 后续可调整但仍受机器码稳定性约束。</remarks>
/// <param name="ClientId">OIDC 客户端稳定标识；发布后不可改名。</param>
/// <param name="DisplayName">客户端展示名称。</param>
/// <param name="RedirectUris">允许回调的 Redirect URI 集合；发布后建议仅追加。</param>
/// <param name="PostLogoutRedirectUris">登出后允许跳转的 URI 集合；可为空。</param>
/// <param name="Scopes">客户端被授予的 Scope 集合。</param>
/// <param name="IsConfidential">是否为机密客户端；机密客户端须保管 Secret。</param>
/// <param name="IsFirstParty">是否为第一方客户端；第一方默认获得更高信任。</param>
/// <param name="ResourceAudience">期望的 Resource Audience；可为空。</param>
public sealed record CreateOidcClientRequest(
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string>? PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool IsConfidential,
    bool IsFirstParty,
    string? ResourceAudience);

/// <summary>更新 OIDC 客户端请求；支持乐观并发。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 RedirectUris 与 Scopes 调整需考虑向后兼容。</remarks>
/// <param name="DisplayName">客户端展示名称。</param>
/// <param name="RedirectUris">允许回调的 Redirect URI 集合。</param>
/// <param name="PostLogoutRedirectUris">登出后允许跳转的 URI 集合；可为空。</param>
/// <param name="Scopes">客户端被授予的 Scope 集合。</param>
/// <param name="IsFirstParty">是否为第一方客户端。</param>
/// <param name="ResourceAudience">期望的 Resource Audience；可为空。</param>
/// <param name="Version">调用方感知的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record UpdateOidcClientRequest(
    string DisplayName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string>? PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool IsFirstParty,
    string? ResourceAudience,
    int Version);

/// <summary>OIDC 客户端投影；不含明文密钥。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ClientId 发布后不可改名。</remarks>
/// <param name="Id">客户端内部稳定标识。</param>
/// <param name="ClientId">OIDC 协议层 ClientId；发布后不可改名。</param>
/// <param name="DisplayName">客户端展示名称。</param>
/// <param name="ClientType">客户端类型机器码；取值见协议约定。</param>
/// <param name="RedirectUris">允许回调的 Redirect URI 集合。</param>
/// <param name="PostLogoutRedirectUris">登出后允许跳转的 URI 集合。</param>
/// <param name="Scopes">客户端被授予的 Scope 集合。</param>
/// <param name="IsFirstParty">是否为第一方客户端。</param>
/// <param name="ResourceAudience">期望的 Resource Audience；可为空。</param>
/// <param name="IsDisabled">是否已停用；停用后认证请求将被拒绝。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="Version">乐观并发版本号；调用方更新时必须回传最新值。</param>
public sealed record OidcClientResponse(
    Guid Id,
    string ClientId,
    string DisplayName,
    string ClientType,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool IsFirstParty,
    string? ResourceAudience,
    bool IsDisabled,
    DateTimeOffset CreatedAtUtc,
    int Version);

/// <summary>创建 OIDC 客户端响应；明文密钥只返回一次。</summary>
/// <remarks>Secret 仅返回一次，调用方必须立即写入安全 Secret Store；禁止落盘或写日志，丢失后只能通过轮换重置。</remarks>
/// <param name="Client">不含明文密钥的客户端投影。</param>
/// <param name="Secret">一次性返回的明文密钥；机密客户端必填，公开客户端为 <see langword="null"/>。</param>
public sealed record CreateOidcClientResponse(
    OidcClientResponse Client,
    string? Secret);

/// <summary>轮换 OIDC 客户端密钥响应；新密钥一次性返回。</summary>
/// <remarks>轮换后旧密钥立即失效；调用方必须立即更新下游配置，未完成更新的客户端将无法认证。</remarks>
/// <param name="Client">不含明文密钥的客户端投影。</param>
/// <param name="Secret">一次性返回的新明文密钥；禁止落盘或写日志。</param>
public sealed record RotateOidcClientSecretResponse(
    OidcClientResponse Client,
    string Secret);