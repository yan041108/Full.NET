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
    int Version,
    string MenuType,
    string? Redirect,
    string? LinkUrl,
    bool IsHidden,
    bool IsKeepAlive,
    bool IsAffix,
    bool IsEmbedded,
    string? Remark);

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
    int Version,
    string MenuType,
    string? Redirect,
    string? LinkUrl,
    bool IsHidden,
    bool IsKeepAlive,
    bool IsAffix,
    bool IsEmbedded,
    string? Remark);

internal class HostMenuListRow
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

    public string MenuType { get; set; } = string.Empty;

    public string? Redirect { get; set; }

    public string? LinkUrl { get; set; }

    public bool IsHidden { get; set; }

    public bool IsKeepAlive { get; set; }

    public bool IsAffix { get; set; }

    public bool IsEmbedded { get; set; }

    public string? Remark { get; set; }
}
