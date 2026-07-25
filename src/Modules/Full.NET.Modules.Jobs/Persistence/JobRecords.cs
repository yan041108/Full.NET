namespace Full.NET.Modules.Jobs.Persistence;

internal sealed class JobDefinitionRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string JobKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public int Version { get; set; }
}

internal sealed class JobExecutionRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid JobDefinitionId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string TriggerKind { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public Guid? LeaseId { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string JobKey { get; set; } = string.Empty;
}
