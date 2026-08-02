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
public sealed record CreateHostApiKeyRequest(
    Guid UserId,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>Host API Key 列表项（不含明文密钥）。</summary>
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
public sealed record CreateHostApiKeyResponse(
    HostApiKeyResponse Key,
    string Secret);
