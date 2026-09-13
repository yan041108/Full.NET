namespace Full.NET.Modules.Ai.Persistence;

/// <summary>委托行映射；不存储访问令牌。</summary>
internal sealed class AiAgentDelegationRecord
{
    public Guid Id { get; set; }

    public string ScopeKey { get; set; } = string.Empty;

    public Guid? TenantId { get; set; }

    public Guid GrantorUserId { get; set; }

    public Guid GranteeUserId { get; set; }

    public string? ToolName { get; set; }

    public string? PermissionCode { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public long Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
