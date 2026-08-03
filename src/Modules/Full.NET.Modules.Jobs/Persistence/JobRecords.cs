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

    public Guid? JobScheduleId { get; set; }

    public DateTimeOffset? ScheduledForUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public string TriggerKind { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public Guid? LeaseId { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string JobKey { get; set; } = string.Empty;
}

internal sealed class JobDefinitionOptionRecord
{
    public Guid Id { get; set; }

    public string JobKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

internal class JobScheduleRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid JobDefinitionId { get; set; }

    public string TriggerKind { get; set; } = string.Empty;

    public string? CronExpression { get; set; }

    public string TimeZoneId { get; set; } = string.Empty;

    public DateTimeOffset? OneTimeAtUtc { get; set; }

    public string MisfirePolicy { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public DateTimeOffset? NextExecutionAtUtc { get; set; }

    public DateTimeOffset? LastExecutionAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public int Version { get; set; }
}

internal sealed class JobScheduleDetailRecord : JobScheduleRecord
{
    public string JobDefinitionJobKey { get; set; } = string.Empty;

    public string JobDefinitionDisplayName { get; set; } = string.Empty;
}
