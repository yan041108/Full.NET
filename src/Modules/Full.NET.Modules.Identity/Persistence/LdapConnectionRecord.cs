namespace Full.NET.Modules.Identity.Persistence;

/// <summary>映射 <c>fn_identity_ldap_connection</c> 行。</summary>
internal sealed class LdapConnectionRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool UseTls { get; set; }

    public string BaseDn { get; set; } = string.Empty;

    public string BindDn { get; set; } = string.Empty;

    public string BindPasswordProtected { get; set; } = string.Empty;

    public string UserSearchFilter { get; set; } = string.Empty;

    public string UserAccountAttribute { get; set; } = string.Empty;

    public string? EmployeeIdAttribute { get; set; }

    public string? DepartmentCodeAttribute { get; set; }

    public string SyncSearchBaseDn { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
