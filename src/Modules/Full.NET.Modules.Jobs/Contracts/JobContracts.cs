using System.Text.Json.Serialization;

namespace Full.NET.Modules.Jobs.Contracts;

public static class HostJobPermissions
{
    public const string DefinitionsRead = "jobs.definitions.read";

    public const string DefinitionsCreate = "jobs.definitions.create";

    public const string DefinitionsUpdate = "jobs.definitions.update";

    public const string DefinitionsDisable = "jobs.definitions.disable";

    public const string DefinitionsDelete = "jobs.definitions.delete";

    public const string DefinitionsTrigger = "jobs.definitions.trigger";

    public const string ExecutionsRead = "jobs.executions.read";

    public const string ExecutionsClear = "jobs.executions.clear";

    public const string HealthRead = "jobs.health.read";

    public const string SchedulesRead = "jobs.schedules.read";

    public const string SchedulesCreate = "jobs.schedules.create";

    public const string SchedulesUpdate = "jobs.schedules.update";

    public const string SchedulesDelete = "jobs.schedules.delete";

    public const string SchedulesPause = "jobs.schedules.pause";

    public const string SchedulesResume = "jobs.schedules.resume";
}

public static class JobsWellKnownKeys
{
    public const string Ping = "jobs.ping";
}

/// <summary>内置任务执行器稳定机器码。</summary>
public static class JobHandlerKinds
{
    public const string Ping = "ping";

    public const string Http = "http";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Ping,
        Http,
    ]);
}

/// <summary>HTTP 任务敏感 Header 的 Settings 密钥引用。</summary>
public sealed record HttpJobSecretHeaderRef(
    [property: JsonPropertyName("configKey")] string ConfigKey);

/// <summary>HTTP 任务执行参数；序列化为 ArgsJson 持久化。</summary>
public sealed record HttpJobArgs(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("headers")]
    IReadOnlyDictionary<string, string>? Headers = null,
    [property: JsonPropertyName("secretHeaders")]
    IReadOnlyDictionary<string, HttpJobSecretHeaderRef>? SecretHeaders = null,
    [property: JsonPropertyName("timeoutSeconds")] int? TimeoutSeconds = null,
    [property: JsonPropertyName("successStatusCodes")]
    IReadOnlyList<int>? SuccessStatusCodes = null);

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
    string HandlerKind,
    HttpJobArgs? Args,
    string DisplayName,
    string? Description,
    string? GroupName,
    bool IsEnabled,
    bool AllowConcurrentExecutions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

public sealed record CreateHostJobDefinitionRequest(
    string JobKey,
    string HandlerKind,
    HttpJobArgs? Args,
    string DisplayName,
    string? Description,
    string? GroupName,
    bool AllowConcurrentExecutions = false);

public sealed record UpdateHostJobDefinitionRequest(
    string DisplayName,
    string? Description,
    string? GroupName,
    string HandlerKind,
    HttpJobArgs? Args,
    bool AllowConcurrentExecutions,
    int Version);

public sealed record DisableHostJobDefinitionRequest(int Version);

public sealed record DeleteHostJobDefinitionRequest(int Version);

/// <summary>作业分组去重选项，对应 Admin.NET ListJobGroup。</summary>
public sealed record HostJobGroupResponse(string GroupName);

public sealed record HostJobScheduleResponse(
    Guid Id,
    Guid JobDefinitionId,
    string JobDefinitionJobKey,
    string JobDefinitionDisplayName,
    string TriggerKind,
    string? CronExpression,
    string TimeZoneId,
    DateTimeOffset? OneTimeAtUtc,
    string MisfirePolicy,
    bool IsEnabled,
    DateTimeOffset? NextExecutionAtUtc,
    DateTimeOffset? LastExecutionAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long NumberOfRuns,
    long NumberOfErrors,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? Args,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

public sealed record CreateHostJobScheduleRequest(
    Guid JobDefinitionId,
    string TriggerKind,
    string? CronExpression,
    string TimeZoneId,
    DateTimeOffset? OneTimeAtUtc,
    string MisfirePolicy,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? Args);

public sealed record UpdateHostJobScheduleRequest(
    string TriggerKind,
    string? CronExpression,
    string TimeZoneId,
    DateTimeOffset? OneTimeAtUtc,
    string MisfirePolicy,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? Args,
    int Version);

public sealed record ChangeHostJobScheduleStateRequest(int Version);

public sealed record HostJobScheduleDefinitionOptionResponse(
    Guid Id,
    string JobKey,
    string HandlerKind,
    string DisplayName);

public sealed record HostJobScheduleCronPreviewResponse(
    string HumanDescription,
    DateTimeOffset NextExecutionAtUtc,
    IReadOnlyList<DateTimeOffset> NextOccurrencesUtc);

public sealed record HostJobHealthResponse(
    IReadOnlyList<string> RegisteredHandlers,
    HostJobHealthBacklogSnapshot Backlog,
    IReadOnlyList<HostJobWorkerInstanceResponse> Workers);

public sealed record HostJobHealthBacklogSnapshot(
    long PendingCount,
    DateTimeOffset? OldestClaimableCreatedAtUtc,
    long DueRetryCount,
    DateTimeOffset? OldestDueRetryAtUtc);

public sealed record HostJobWorkerInstanceResponse(
    Guid InstanceId,
    string HostProfile,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    string? WorkerVersion,
    bool IsStale);

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
