namespace Full.NET.Modules.Identity.Persistence;

internal sealed class HostUserListRow
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

internal sealed class HostUserPreferredLocaleRow
{
    public Guid Id { get; set; }

    public string? Value { get; set; }
}

internal sealed class HostUserFailedLoginCountRow
{
    public Guid Id { get; set; }

    public int Value { get; set; }
}

internal sealed class HostUserLockoutEndUtcRow
{
    public Guid Id { get; set; }

    public DateTimeOffset? Value { get; set; }
}
