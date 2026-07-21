namespace Full.NET.Modules.Identity.Persistence;

internal sealed record IdentityRoleRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string Code,
    string Name,
    bool IsSystem,
    bool IsActive,
    bool IsSuperAdministrator,
    string DataScopeKind,
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
    bool IsSuperAdministrator,
    string DataScopeKind,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record UpdateIdentitySystemRole(
    Guid Id,
    string Name,
    bool IsSuperAdministrator,
    DateTimeOffset UpdatedAtUtc,
    int Version);

internal sealed record IdentityAuthorizationRow(
    string? PermissionCode,
    bool IsSuperAdministrator);

internal sealed record IdentityRolePermission(
    Guid RoleId,
    string PermissionCode);

internal sealed record IdentityUserRole(
    Guid UserId,
    Guid RoleId);
