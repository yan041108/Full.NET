namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Host API Key 管理 API 契约。</summary>
public static class IdentityApiKeyManagementPermissions
{
    /// <summary>分页查询 Host API Key。</summary>
    public const string Read = "identity.api_keys.read";

    /// <summary>创建 Host API Key。</summary>
    public const string Create = "identity.api_keys.create";

    /// <summary>禁用 Host API Key。</summary>
    public const string Disable = "identity.api_keys.disable";

    /// <summary>轮换 Host API Key。</summary>
    public const string Rotate = "identity.api_keys.rotate";

    /// <summary>迁移 059 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "identity.api_keys.write";
}

/// <summary>创建 Host API Key 请求。</summary>
/// <param name="UserId">绑定的 Host 用户标识；该用户必须为活动账号。</param>
/// <param name="DisplayName">面向管理员展示的名称。</param>
/// <param name="Permissions">该 Key 被授予的稳定权限码集合；必须为 Host 作用域内已发布权限。</param>
/// <param name="ExpiresAtUtc">可选的过期时间；<see langword="null"/> 表示长期有效（仍可被主动禁用或轮换）。</param>
public sealed record CreateHostApiKeyRequest(
    Guid UserId,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>Host API Key 列表项（不含明文密钥）。</summary>
/// <param name="Id">API Key 稳定标识。</param>
/// <param name="UserId">绑定的 Host 用户标识。</param>
/// <param name="Username">绑定的 Host 用户登录名。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="KeyPrefix">明文密钥前缀；仅用于在界面上帮助管理员识别，不可用于反推完整密钥。</param>
/// <param name="Permissions">该 Key 被授予的稳定权限码集合。</param>
/// <param name="ExpiresAtUtc">过期时间；<see langword="null"/> 表示长期有效。</param>
/// <param name="IsActive">是否处于活动状态；禁用后该 Key 不再通过签名校验。</param>
/// <param name="LastUsedAtUtc">最近一次通过签名校验的时间（UTC）；从未使用时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
public sealed record HostApiKeyResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    string KeyPrefix,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc,
    bool IsActive,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset CreatedAtUtc);

/// <summary>创建 Host API Key 成功响应；明文密钥只返回一次。</summary>
/// <param name="Key">不含明文密钥的列表投影。</param>
/// <param name="Secret">一次性返回的明文密钥；调用方必须立即写入安全 Secret Store，禁止落盘或写日志。</param>
public sealed record CreateHostApiKeyResponse(
    HostApiKeyResponse Key,
    string Secret);
