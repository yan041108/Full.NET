namespace Full.NET.Modules.Jobs.Contracts;

public static class HostJobPermissions
{
    public const string DefinitionsRead = "jobs.definitions.read";

    public const string DefinitionsWrite = "jobs.definitions.write";

    public const string ExecutionsRead = "jobs.executions.read";
}

public static class JobsWellKnownKeys
{
    public const string Ping = "jobs.ping";
}

public static class JobTriggerKinds
{
    public const string Manual = "manual";
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

public sealed record HostJobExecutionResponse(
    Guid Id,
    Guid JobDefinitionId,
    string Status,
    string TriggerKind,
    string? ErrorMessage,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc);
