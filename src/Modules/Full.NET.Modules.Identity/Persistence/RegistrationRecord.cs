namespace Full.NET.Modules.Identity.Persistence;

/// <summary>映射 <c>fn_identity_registration_policy</c> 单例行。</summary>
internal sealed record RegistrationPolicyRecord(
    Guid Id,
    bool IsPublicRegistrationEnabled,
    DateTimeOffset UpdatedAtUtc,
    int Version);

/// <summary>映射 <c>fn_identity_user_registration_way</c> 行。</summary>
internal sealed class RegistrationWayRecord
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public Guid RoleId { get; set; }

    public Guid OrganizationUnitId { get; set; }

    public Guid? PositionId { get; set; }

    public int SortOrder { get; set; }

    public string? Remark { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
