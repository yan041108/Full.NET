namespace Full.NET.Modules.Identity.Persistence;

internal sealed class RefreshSessionRecord
{
    public Guid SessionId { get; set; }

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? ReplacedById { get; set; }

    public Guid? ActiveTenantId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public int SessionVersion { get; set; }

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

    public DateTimeOffset UserCreatedAtUtc { get; set; }

    public DateTimeOffset? UserUpdatedAtUtc { get; set; }

    public int UserVersion { get; set; }

    public string PreferredLocale { get; set; } = string.Empty;

    public int ProfileVersion { get; set; }
}
