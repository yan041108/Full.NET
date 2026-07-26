namespace Full.NET.Modules.Identity.Persistence;

/// <summary>API Key 持久化投影；明文密钥只在创建时返回一次。</summary>
internal sealed class ApiKeyRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string KeyPrefix { get; set; } = string.Empty;

    public string KeyHash { get; set; } = string.Empty;

    public string PermissionsJson { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? DisabledAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>API Key 列表联表行。</summary>
internal sealed class ApiKeyListRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string KeyPrefix { get; set; } = string.Empty;

    public string PermissionsJson { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>API Key 认证时联表加载的用户与密钥状态。</summary>
internal sealed class ApiKeyAuthenticationRow
{
    public Guid ApiKeyId { get; set; }

    public Guid UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PermissionsJson { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public bool UserIsActive { get; set; }

    public DateTimeOffset? UserLockoutEndUtc { get; set; }
}
