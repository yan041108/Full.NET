using Full.NET.Localization;

namespace Full.NET.Modules.Identity.Persistence;

internal sealed class IdentityUserRecord
{
    public IdentityUserRecord()
    {
    }

    public IdentityUserRecord(
        Guid id,
        Guid? tenantId,
        string scopeKey,
        string username,
        string normalizedUsername,
        string displayName,
        string passwordHash,
        bool isActive,
        int failedLoginCount,
        DateTimeOffset? lockoutEndUtc,
        string securityStamp,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? updatedAtUtc,
        int version,
        string preferredLocale = LocaleCatalog.DefaultLocale,
        int profileVersion = 1)
    {
        Id = id;
        TenantId = tenantId;
        ScopeKey = scopeKey;
        Username = username;
        NormalizedUsername = normalizedUsername;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        IsActive = isActive;
        FailedLoginCount = failedLoginCount;
        LockoutEndUtc = lockoutEndUtc;
        SecurityStamp = securityStamp;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        Version = version;
        PreferredLocale = preferredLocale;
        ProfileVersion = profileVersion;
    }

    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string ScopeKey { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string NormalizedUsername { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutEndUtc { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }

    public string PreferredLocale { get; set; } = string.Empty;

    public int ProfileVersion { get; set; }
}
