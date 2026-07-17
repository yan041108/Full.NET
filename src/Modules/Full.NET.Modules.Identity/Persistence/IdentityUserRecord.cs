namespace Full.NET.Modules.Identity.Persistence;

internal sealed record IdentityUserRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string Username,
    string NormalizedUsername,
    string DisplayName,
    string PasswordHash,
    bool IsActive,
    int FailedLoginCount,
    DateTimeOffset? LockoutEndUtc,
    string SecurityStamp,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
