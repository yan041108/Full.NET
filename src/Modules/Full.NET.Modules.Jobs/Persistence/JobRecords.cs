using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Modules.Jobs.Persistence;

/// <summary>
/// Host 任务定义持久化记录（对应 Admin.NET SysJobDetail）。
/// 承载 JobKey（唯一处理器键）、DisplayName、Description、GroupName、IsEnabled、创建/更新审计、并发控制 Version 等字段，
/// TenantId 为 NULL 时表示 Host 级定义，后续可扩展 Tenant 级定义隔离。
/// </summary>
internal sealed class JobDefinitionRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string JobKey { get; set; } = string.Empty;

    public string HandlerKind { get; set; } = JobHandlerKinds.Ping;

    public string? ArgsJson { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? GroupName { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>是否允许同一作业定义在集群内重叠执行；对标 Admin.NET SysJobDetail.Concurrent。</summary>
    public bool AllowConcurrentExecutions { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// Host 任务执行记录持久化记录（对应 Admin.NET SysJobTriggerRecord）。
/// 承载状态机（Pending→Running→Succeeded/Failed，可带重试 NextAttemptAtUtc/AttemptCount）、
/// 关联 JobDefinitionId 与可选 JobScheduleId（手动触发时为 NULL）、
/// 分布式 Worker 竞争执行的 LeaseId/LeaseExpiresAtUtc 租约机制、
/// StartedAtUtc 与 FinishedAtUtc 用于计算耗时，ErrorMessage 字段用于保存截断的异常信息。
/// </summary>
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

    public string HandlerKind { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Host 任务计划调度持久化记录（对应 Admin.NET SysJobTrigger）。
/// 支持 TriggerKind=Cron（周期性）与 OneTime（一次性）两类触发器；
/// Cron 携带 CronExpression 与 TimeZoneId，OneTime 携带 OneTimeAtUtc；
/// 运行统计 NumberOfRuns/NumberOfErrors 用于 UI 红色 Tag 告警，
/// StartTime/EndTime 构成可选时间窗口，NextExecutionAtUtc 由 JobScheduleCalculator 在写入前预计算，
/// CompletedAtUtc 非空时该计划已完结（一次性触发后或暂停后标记完成），Version 用于并发控制。
/// </summary>
internal class JobScheduleRecord
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid JobDefinitionId { get; set; }

    /// <summary>来自关联定义；调度物化时用于禁止重叠 gate。</summary>
    public bool AllowConcurrentExecutions { get; set; }

    public string TriggerKind { get; set; } = string.Empty;

    public string? CronExpression { get; set; }

    public string TimeZoneId { get; set; } = string.Empty;

    public DateTimeOffset? OneTimeAtUtc { get; set; }

    public string MisfirePolicy { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public DateTimeOffset? NextExecutionAtUtc { get; set; }

    public DateTimeOffset? LastExecutionAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long NumberOfRuns { get; set; }

    public long NumberOfErrors { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public string? Args { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// 带关联 JobDefinition 冗余字段的计划详情投影，用于列表查询一次 JOIN 返回展示所需的 JobKey 与 DisplayName，
/// 避免 N+1 二次查询定义表。
/// </summary>
internal sealed class JobScheduleDetailRecord : JobScheduleRecord
{
    public string JobDefinitionJobKey { get; set; } = string.Empty;

    public string JobDefinitionDisplayName { get; set; } = string.Empty;
}
