namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OpenAccess 接入方应用管理 API 契约。</summary>
public static class IdentityOpenAccessClientPermissions
{
    /// <summary>分页查询 OpenAccess 接入方应用。</summary>
    public const string Read = "identity.open_access_clients.read";

    /// <summary>创建 OpenAccess 接入方应用。</summary>
    public const string Create = "identity.open_access_clients.create";

    /// <summary>更新 OpenAccess 接入方应用元数据与权限。</summary>
    public const string Update = "identity.open_access_clients.update";

    /// <summary>停用 OpenAccess 接入方应用。</summary>
    public const string Disable = "identity.open_access_clients.disable";

    /// <summary>轮换 OpenAccess 接入方应用密钥。</summary>
    public const string Rotate = "identity.open_access_clients.rotate";
}

/// <summary>创建 OpenAccess 接入方应用请求。</summary>
/// <param name="UserId">绑定的 Host 用户标识；该用户必须为活动账号。</param>
/// <param name="Name">接入方应用名称。</param>
/// <param name="Description">接入方应用描述；可为空。</param>
/// <param name="Remark">管理员备注；可为空。</param>
/// <param name="Permissions">该应用被授予的稳定权限码集合；必须为 Host 作用域内已发布权限。</param>
/// <param name="ExpiresAtUtc">可选的过期时间；<see langword="null"/> 表示长期有效（仍可被主动停用或轮换）。</param>
public sealed record CreateOpenAccessClientRequest(
    Guid UserId,
    string Name,
    string? Description,
    string? Remark,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>更新 OpenAccess 接入方应用请求。</summary>
/// <param name="Name">接入方应用名称。</param>
/// <param name="Description">接入方应用描述；可为空。</param>
/// <param name="Remark">管理员备注；可为空。</param>
/// <param name="Permissions">该应用被授予的稳定权限码集合。</param>
/// <param name="ExpiresAtUtc">可选的过期时间；<see langword="null"/> 表示长期有效。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdateOpenAccessClientRequest(
    string Name,
    string? Description,
    string? Remark,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc,
    int Version);

/// <summary>OpenAccess 接入方应用响应（不含明文密钥）。</summary>
/// <param name="Id">接入方应用稳定标识。</param>
/// <param name="ApiKeyId">当前绑定的 API Key 标识。</param>
/// <param name="UserId">绑定的 Host 用户标识。</param>
/// <param name="Username">绑定的 Host 用户登录名。</param>
/// <param name="Name">接入方应用名称。</param>
/// <param name="Description">接入方应用描述。</param>
/// <param name="Remark">管理员备注。</param>
/// <param name="AccessKeyId">公开 Access Key Id；等于 API Key 的 KeyPrefix，用于 HMAC 签名认证。</param>
/// <param name="Permissions">该应用被授予的稳定权限码集合。</param>
/// <param name="ExpiresAtUtc">过期时间；<see langword="null"/> 表示长期有效。</param>
/// <param name="IsActive">是否处于活动状态；停用后密钥不再通过认证。</param>
/// <param name="LastUsedAtUtc">最近一次通过认证的时间（UTC）；从未使用时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record OpenAccessClientResponse(
    Guid Id,
    Guid ApiKeyId,
    Guid UserId,
    string Username,
    string Name,
    string? Description,
    string? Remark,
    string AccessKeyId,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc,
    bool IsActive,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset CreatedAtUtc,
    int Version);

/// <summary>创建或轮换 OpenAccess 接入方应用成功响应；明文密钥只返回一次。</summary>
/// <param name="Client">不含明文密钥的应用投影。</param>
/// <param name="Secret">一次性返回的明文密钥；调用方必须立即写入安全 Secret Store，禁止落盘或写日志。</param>
public sealed record CreateOpenAccessClientResponse(
    OpenAccessClientResponse Client,
    string Secret);
