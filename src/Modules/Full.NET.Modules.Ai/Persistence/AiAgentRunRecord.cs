namespace Full.NET.Modules.Ai.Persistence;

internal sealed class AiAgentRunRecord
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid SessionId { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; }
    public Guid AuthorizationBindingId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public string ActorScope { get; set; } = string.Empty;
    public string EffectiveScope { get; set; } = string.Empty;
    public string StatusKey { get; set; } = string.Empty;
    public string BudgetJson { get; set; } = string.Empty;
    public DateTimeOffset DeadlineAtUtc { get; set; }
    public long Version { get; set; }
    public string? LeaseOwner { get; set; }
    public long LeaseEpoch { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
