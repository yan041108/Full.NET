namespace Full.NET.Modules.Identity.Persistence;

internal sealed record IdentityNavigationRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    Guid? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record InsertIdentityNavigation(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    Guid? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed class HostMenuListRow
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public string RouteName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string ComponentKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public string RequiredPermission { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
