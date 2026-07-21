namespace Full.NET.Modules.Identity.Persistence;

internal sealed class HostRoleListRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public bool IsActive { get; set; }

    public bool IsSuperAdministrator { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
