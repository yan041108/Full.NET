using Full.NET.Modules.Identity.Oidc;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>OIDC 应用持久化实体；映射 OpenIddict 的 application 表，存储 ClientId 与密钥等机器码字段。</summary>
/// <remarks>ClientId 与 ClientSecret 一旦发布不可改名；ClientSecret 必须以服务端加密形式存储，禁止通过任何 API 回显。Version 字段用于乐观并发控制。</remarks>
public sealed class IdentityOidcApplication
{
    /// <summary>应用稳定标识。</summary>
    public Guid Id { get; set; }

    /// <summary>OIDC 协议层 ClientId；发布后不可改名。</summary>
    public string? ClientId { get; set; }

    /// <summary>客户端密钥的加密存储；禁止回显或写日志。</summary>
    public string? ClientSecret { get; set; }

    /// <summary>同意类型机器码（如 explicit、implicit、external）。</summary>
    public string? ConsentType { get; set; }

    /// <summary>面向管理端展示的应用名称。</summary>
    public string? DisplayName { get; set; }

    /// <summary>多语言展示名的 JSON 序列化形式。</summary>
    public string? DisplayNamesJson { get; set; }

    /// <summary>应用被授予的权限码 JSON；与稳定权限码目录对齐。</summary>
    public string? PermissionsJson { get; set; }

    /// <summary>登出后允许跳转 URI 的 JSON 集合。</summary>
    public string? PostLogoutRedirectUrisJson { get; set; }

    /// <summary>扩展属性 JSON；用于 OpenIddict 兼容层。</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>允许回调 Redirect URI 的 JSON 集合；发布后建议仅追加。</summary>
    public string? RedirectUrisJson { get; set; }

    /// <summary>认证需求 JSON；如 PKCE、nonce 等。</summary>
    public string? RequirementsJson { get; set; }

    /// <summary>应用类型机器码（如 web、native）。</summary>
    public string? ApplicationType { get; set; }

    /// <summary>应用持有的 JSON Web Key Set；包含敏感签名材料，禁止回显。</summary>
    public string? JsonWebKeySetJson { get; set; }

    /// <summary>应用设置 JSON；用于 OpenIddict 兼容层。</summary>
    public string? SettingsJson { get; set; }

    /// <summary>客户端类型机器码。</summary>
    public string? ClientType { get; set; }

    /// <summary>乐观并发版本号；服务端据此拒绝并发覆盖。</summary>
    public long Version { get; set; }

    /// <summary>记录创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>最近一次更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>OIDC 授权持久化实体；映射 OpenIddict 的 authorization 表，记录一次性授权决定。</summary>
/// <remarks>授权记录具有状态机；Status 字段在 valid、rejected、redeemed 之间迁移，迁移必须原子完成。Version 用于乐观并发控制。</remarks>
public sealed class IdentityOidcAuthorization
{
    /// <summary>授权稳定标识。</summary>
    public Guid Id { get; set; }

    /// <summary>所属应用标识；指向 <see cref="IdentityOidcApplication.Id"/>。</summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>授权创建时间（UTC）。</summary>
    public DateTimeOffset? CreationDateUtc { get; set; }

    /// <summary>扩展属性 JSON。</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>授权范围的 JSON 集合。</summary>
    public string? ScopesJson { get; set; }

    /// <summary>授权状态机器码；迁移必须原子完成。</summary>
    public string? Status { get; set; }

    /// <summary>授权主体标识（用户 Id）。</summary>
    public string? Subject { get; set; }

    /// <summary>授权类型机器码（如 authorization_code、refresh_token）。</summary>
    public string? Type { get; set; }

    /// <summary>乐观并发版本号。</summary>
    public long Version { get; set; }

    /// <summary>记录创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>最近一次更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>OIDC Scope 持久化实体；映射 OpenIddict 的 scope 表，存储可声明的权限范围。</summary>
/// <remarks>Name 字段（即 Scope 名）发布后不可改名或删除，新增只能追加，以保证 Token 验证稳定。Version 用于乐观并发控制。</remarks>
public sealed class IdentityOidcScope
{
    /// <summary>Scope 稳定标识。</summary>
    public Guid Id { get; set; }

    /// <summary>Scope 名称机器码；发布后不可改名或删除。</summary>
    public string? Name { get; set; }

    /// <summary>面向管理端展示的 Scope 描述。</summary>
    public string? Description { get; set; }

    /// <summary>多语言描述的 JSON 序列化形式。</summary>
    public string? DescriptionsJson { get; set; }

    /// <summary>面向管理端展示的 Scope 名称。</summary>
    public string? DisplayName { get; set; }

    /// <summary>多语言展示名的 JSON 序列化形式。</summary>
    public string? DisplayNamesJson { get; set; }

    /// <summary>扩展属性 JSON。</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>关联资源（audience）的 JSON 集合。</summary>
    public string? ResourcesJson { get; set; }

    /// <summary>乐观并发版本号。</summary>
    public long Version { get; set; }

    /// <summary>记录创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>最近一次更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>OIDC Token 持久化实体；映射 OpenIddict 的 token 表，存储已签发或撤销的 Token 元数据。</summary>
/// <remarks>Token 具有生命周期；RedemptionDateUtc 与 Status 一起决定 Token 是否仍可 redemption。SessionId 从 PropertiesJson 解析得到，非独立列。Version 用于乐观并发控制。</remarks>
public sealed class IdentityOidcToken
{
    /// <summary>Token 稳定标识。</summary>
    public Guid Id { get; set; }

    /// <summary>所属应用标识；指向 <see cref="IdentityOidcApplication.Id"/>。</summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>所属授权标识；指向 <see cref="IdentityOidcAuthorization.Id"/>。</summary>
    public Guid? AuthorizationId { get; set; }

    /// <summary>Token 创建时间（UTC）。</summary>
    public DateTimeOffset? CreationDateUtc { get; set; }

    /// <summary>Token 过期时间（UTC）；过期后不可再用于 redemption。</summary>
    public DateTimeOffset? ExpirationDateUtc { get; set; }

    /// <summary>Token 载荷；包含敏感凭据，禁止回显或写日志。</summary>
    public string? Payload { get; set; }

    /// <summary>扩展属性 JSON；SessionId 从中解析。</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>Token redemption 时间（UTC）；已 redemption 的 Token 不可再次使用。</summary>
    public DateTimeOffset? RedemptionDateUtc { get; set; }

    /// <summary>Token 引用标识；用于 reference token 模式。</summary>
    public string? ReferenceId { get; set; }

    /// <summary>Token 状态机器码（如 valid、redeemed、revoked）。</summary>
    public string? Status { get; set; }

    /// <summary>Token 主体标识（用户 Id）。</summary>
    public string? Subject { get; set; }

    /// <summary>Token 类型机器码（如 access_token、refresh_token、id_token）。</summary>
    public string? Type { get; set; }

    /// <summary>乐观并发版本号。</summary>
    public long Version { get; set; }

    /// <summary>记录创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>最近一次更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>从 PropertiesJson 解析得到的会话标识；非持久化列，禁止外部依赖。</summary>
    internal string? SessionId { get; set; }
}

internal sealed record IdentityOidcApplicationRow(
    Guid Id, string? ClientId, string? ClientSecret, string? ConsentType, string? DisplayName,
    string? DisplayNamesJson, string? PermissionsJson, string? PostLogoutRedirectUrisJson,
    string? PropertiesJson, string? RedirectUrisJson, string? RequirementsJson, string? ApplicationType,
    string? JsonWebKeySetJson, string? SettingsJson, string? ClientType, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcAuthorizationRow(
    Guid Id, Guid? ApplicationId, DateTimeOffset? CreationDateUtc, string? PropertiesJson,
    string? ScopesJson, string? Status, string? Subject, string? Type, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcAuthorizationDetailRow(
    Guid Id, Guid? ApplicationId, DateTimeOffset? CreationDateUtc, string? PropertiesJson,
    string? ScopesJson, string? Status, string? Subject, string? Type, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string? ClientId);

internal sealed record IdentityOidcScopeRow(
    Guid Id, string? Name, string? Description, string? DescriptionsJson, string? DisplayName,
    string? DisplayNamesJson, string? PropertiesJson, string? ResourcesJson, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcTokenRow(
    Guid Id, Guid? ApplicationId, Guid? AuthorizationId, DateTimeOffset? CreationDateUtc,
    DateTimeOffset? ExpirationDateUtc, string? Payload, string? PropertiesJson,
    DateTimeOffset? RedemptionDateUtc, string? ReferenceId, string? Status, string? Subject,
    string? Type, long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcCountRow(long Count);

internal static class IdentityOidcRecordMapper
{
    internal static IdentityOidcApplication ToApplication(IdentityOidcApplicationRow row) => new()
    {
        Id = row.Id, ClientId = row.ClientId, ClientSecret = row.ClientSecret, ConsentType = row.ConsentType,
        DisplayName = row.DisplayName, DisplayNamesJson = row.DisplayNamesJson, PermissionsJson = row.PermissionsJson,
        PostLogoutRedirectUrisJson = row.PostLogoutRedirectUrisJson, PropertiesJson = row.PropertiesJson,
        RedirectUrisJson = row.RedirectUrisJson, RequirementsJson = row.RequirementsJson,
        ApplicationType = row.ApplicationType, JsonWebKeySetJson = row.JsonWebKeySetJson,
        SettingsJson = row.SettingsJson, ClientType = row.ClientType, Version = row.Version,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
    };

    internal static IdentityOidcAuthorization ToAuthorization(IdentityOidcAuthorizationRow row) => new()
    {
        Id = row.Id, ApplicationId = row.ApplicationId, CreationDateUtc = row.CreationDateUtc,
        PropertiesJson = row.PropertiesJson, ScopesJson = row.ScopesJson, Status = row.Status,
        Subject = row.Subject, Type = row.Type, Version = row.Version,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
    };

    internal static IdentityOidcScope ToScope(IdentityOidcScopeRow row) => new()
    {
        Id = row.Id, Name = row.Name, Description = row.Description, DescriptionsJson = row.DescriptionsJson,
        DisplayName = row.DisplayName, DisplayNamesJson = row.DisplayNamesJson, PropertiesJson = row.PropertiesJson,
        ResourcesJson = row.ResourcesJson, Version = row.Version,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
    };

    internal static IdentityOidcToken ToToken(IdentityOidcTokenRow row)
    {
        var token = new IdentityOidcToken
        {
            Id = row.Id, ApplicationId = row.ApplicationId, AuthorizationId = row.AuthorizationId,
            CreationDateUtc = row.CreationDateUtc, ExpirationDateUtc = row.ExpirationDateUtc,
            Payload = row.Payload, PropertiesJson = row.PropertiesJson, RedemptionDateUtc = row.RedemptionDateUtc,
            ReferenceId = row.ReferenceId, Status = row.Status, Subject = row.Subject, Type = row.Type,
            Version = row.Version, CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
        };
        token.SessionId = IdentityOidcStoreSupport.ReadSessionId(token.PropertiesJson);
        return token;
    }
}