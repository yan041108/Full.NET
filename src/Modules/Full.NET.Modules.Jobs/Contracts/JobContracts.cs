namespace Full.NET.Modules.Jobs.Contracts;

public static class HostJobPermissions
{
    public const string DefinitionsRead = "jobs.definitions.read";

    public const string DefinitionsCreate = "jobs.definitions.create";

    public const string DefinitionsUpdate = "jobs.definitions.update";

    public const string DefinitionsDisable = "jobs.definitions.disable";

    public const string DefinitionsTrigger = "jobs.definitions.trigger";

    public const string ExecutionsRead = "jobs.executions.read";

    public const string SchedulesRead = "jobs.schedules.read";

    public const string SchedulesCreate = "jobs.schedules.create";

    public const string SchedulesUpdate = "jobs.schedules.update";

    public const string SchedulesPause = "jobs.schedules.pause";

    public const string SchedulesResume = "jobs.schedules.resume";
}

public static class JobsWellKnownKeys
{
    public const string Ping = "jobs.ping";
}

public static class JobTriggerKinds
{
    public const string Manual = "manual";

    public const string OneTime = "one_time";

    public const string Cron = "cron";
}

public static class JobMisfirePolicies
{
    public const string Skip = "skip";

    public const string FireOnce = "fire_once";
}

public static class JobExecutionStatuses
{
    public const string Pending = "pending";

    public const string Running = "running";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";
}

public sealed record HostJobDefinitionResponse(
    Guid Id,
    string JobKey,
    string DisplayName,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

public sealed record CreateHostJobDefinitionRequest(
    string JobKey,
    string DisplayName,
    string? Description);

public sealed record UpdateHostJobDefinitionRequest(
    string DisplayName,
    string? Description,
    int Version);

public sealed record DisableHostJobDefinitionRequest(int Version);

public sealed record HostJobScheduleResponse(
    Guid Id,
    Guid JobDefinitionId,
    string TriggerKind,
    string? CronExpression,
    string TimeZoneId,
    DateTimeOffset? OneTimeAtUtc,
    string MisfirePolicy,
    bool IsEnabled,
    DateTimeOffset? NextExecutionAtUtc,
    DateTimeOffset? LastExecutionAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

public sealed record CreateHostJobScheduleRequest(
    Guid JobDefinitionId,
    string TriggerKind,
    string? CronExpression,
    string TimeZoneId,
    DateTimeOffset? OneTimeAtUtc,
    string MisfirePolicy);

public sealed record UpdateHostJobScheduleRequest(
    string TriggerKind,
    string? CronExpression,
    string TimeZoneId,
    DateTimeOffset? OneTimeAtUtc,
    string MisfirePolicy,
    int Version);

public sealed record ChangeHostJobScheduleStateRequest(int Version);

public sealed record HostJobExecutionResponse(
    Guid Id,
    Guid JobDefinitionId,
    Guid? JobScheduleId,
    string Status,
    string TriggerKind,
    DateTimeOffset? ScheduledForUtc,
    string? ErrorMessage,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc);
