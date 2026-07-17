namespace Full.NET.Modules.Identity.Persistence;

internal sealed record IdentityRoleRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string Code,
    string Name,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record InsertIdentityRole(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string Code,
    string Name,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record UpdateIdentitySystemRole(
    Guid Id,
    string Name,
    DateTimeOffset UpdatedAtUtc,
    int Version);

internal sealed record IdentityRolePermission(
    Guid RoleId,
    string PermissionCode);

internal sealed record IdentityUserRole(
    Guid UserId,
    Guid RoleId);
