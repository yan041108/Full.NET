namespace Full.NET.Modules.Identity.Persistence;

/// <summary>OpenAccess 接入方应用持久化投影。</summary>
internal sealed class OpenAccessClientRecord
{
    public Guid Id { get; set; }

    public Guid ApiKeyId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Remark { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>OpenAccess 接入方应用详情联表行。</summary>
internal sealed class OpenAccessClientDetailRow
{
    public Guid Id { get; set; }

    public Guid ApiKeyId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Remark { get; set; }

    public Guid UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string PermissionsJson { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public int Version { get; set; }
}
