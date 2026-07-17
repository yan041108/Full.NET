using Full.NET.Localization;

namespace Full.NET.Modules.Identity.Domain;

internal sealed record IdentityUser(
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
    int Version,
    string PreferredLocale = LocaleCatalog.DefaultLocale,
    int ProfileVersion = 1);
