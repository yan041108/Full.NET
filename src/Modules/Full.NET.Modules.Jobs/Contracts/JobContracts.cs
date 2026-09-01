using System.Text.Json.Serialization;

namespace Full.NET.Modules.Jobs.Contracts;

/// <summary>
/// 作业（Jobs）模块稳定权限码集合；不可本地化，作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class HostJobPermissions
{
    /// <summary>允许读取作业定义列表与详情。</summary>
    public const string DefinitionsRead = "jobs.definitions.read";

    /// <summary>允许创建新的作业定义。</summary>
    public const string DefinitionsCreate = "jobs.definitions.create";

    /// <summary>允许修改既有作业定义的元数据与执行参数。</summary>
    public const string DefinitionsUpdate = "jobs.definitions.update";

    /// <summary>允许停用作业定义，使其不再被任何调度触发。</summary>
    public const string DefinitionsDisable = "jobs.definitions.disable";

    /// <summary>允许删除未被引用的作业定义。</summary>
    public const string DefinitionsDelete = "jobs.definitions.delete";

    /// <summary>允许手动触发一次作业定义立即执行。</summary>
    public const string DefinitionsTrigger = "jobs.definitions.trigger";

    /// <summary>允许读取作业执行记录列表与详情。</summary>
    public const string ExecutionsRead = "jobs.executions.read";

    /// <summary>允许批量清理已完成或失败的作业执行历史。</summary>
    public const string ExecutionsClear = "jobs.executions.clear";

    /// <summary>允许读取作业调度健康检查与积压摘要。</summary>
    public const string HealthRead = "jobs.health.read";

    /// <summary>允许读取作业调度计划列表与详情。</summary>
    public const string SchedulesRead = "jobs.schedules.read";

    /// <summary>允许创建新的作业调度计划。</summary>
    public const string SchedulesCreate = "jobs.schedules.create";

    /// <summary>允许修改既有作业调度计划的触发条件。</summary>
    public const string SchedulesUpdate = "jobs.schedules.update";

    /// <summary>允许删除未在运行中的作业调度计划。</summary>
    public const string SchedulesDelete = "jobs.schedules.delete";

    /// <summary>允许暂停作业调度计划，暂不产生新的触发实例。</summary>
    public const string SchedulesPause = "jobs.schedules.pause";

    /// <summary>允许恢复被暂停的作业调度计划。</summary>
    public const string SchedulesResume = "jobs.schedules.resume";
}

/// <summary>
/// 作业模块已知 JobKey 集合，用于标识内置任务；值为稳定机器码不可重命名。
/// </summary>
public static class JobsWellKnownKeys
{
    /// <summary>保活心跳任务 JobKey；用于验证 Worker 链路与健康探针。</summary>
    public const string Ping = "jobs.ping";
}

/// <summary>内置任务执行器稳定机器码。</summary>
public static class JobHandlerKinds
{
    /// <summary>Ping 执行器：只写一条成功执行记录，用于链路自检。</summary>
    public const string Ping = "ping";

    /// <summary>HTTP 回调执行器：按 HttpJobArgs 发起一次 HTTP 请求并以状态码判定成败。</summary>
    public const string Http = "http";

    /// <summary>当前版本已注册的全部执行器稳定键集合；顺序作为枚举列表的稳定投影。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Ping,
        Http,
    ]);
}

/// <summary>HTTP 任务敏感 Header 的 Settings 密钥引用。</summary>
/// <param name="ConfigKey">Settings 目录中存储实际值的稳定键名；真实值不会出现在 ArgsJson 中。</param>
public sealed record HttpJobSecretHeaderRef(
    [property: JsonPropertyName("configKey")] string ConfigKey);

/// <summary>HTTP 任务执行参数；序列化为 ArgsJson 持久化。</summary>
/// <param name="Url">目标请求 URL。</param>
/// <param name="Method">HTTP 方法，如 GET/POST/PUT/DELETE。</param>
/// <param name="Headers">普通 Header 字典；敏感 Header 应通过 SecretHeaders 注入。</param>
/// <param name="SecretHeaders">敏感 Header 字典；值为 Settings 密钥引用，运行时由 Worker 解析后注入。</param>
/// <param name="TimeoutSeconds">请求超时秒数；null 表示使用 Worker 默认超时。</param>
/// <param name="SuccessStatusCodes">视为执行成功的 HTTP 状态码白名单；null 时默认 200-299 均为成功。</param>
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

/// <summary>
/// 作业触发方式稳定机器码；作为调度计划 TriggerKind 的权威取值范围。
/// </summary>
public static class JobTriggerKinds
{
    /// <summary>手动触发：不绑定调度计划，由用户或外部调用即时触发。</summary>
    public const string Manual = "manual";

    /// <summary>一次性触发：在指定 OneTimeAtUtc 时刻触发一次后即失效。</summary>
    public const string OneTime = "one_time";

    /// <summary>Cron 表达式触发：按 CronExpression 与 TimeZoneId 重复调度。</summary>
    public const string Cron = "cron";
}

/// <summary>
/// 作业错失触发（Misfire）的补偿策略稳定机器码。
/// </summary>
public static class JobMisfirePolicies
{
    /// <summary>跳过策略：错过的触发窗口直接丢弃，不补偿执行。</summary>
    public const string Skip = "skip";

    /// <summary>补偿一次策略：在下一个可执行窗口立即补偿一次错失的触发。</summary>
    public const string FireOnce = "fire_once";
}

/// <summary>
/// 作业执行状态稳定机器码；持久化与协议字段共享同一字符串。
/// </summary>
public static class JobExecutionStatuses
{
    /// <summary>已入队待领取；Worker 尚未开始执行。</summary>
    public const string Pending = "pending";

    /// <summary>执行中；某 Worker 已领取租约并正在运行。</summary>
    public const string Running = "running";

    /// <summary>执行成功；满足 SuccessStatusCodes 或 Handler 无异常退出。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>执行失败；超过最大重试次数仍未成功。</summary>
    public const string Failed = "failed";
}

/// <summary>
/// 作业定义响应契约，用于列表、详情与调度下拉选项。
/// </summary>
/// <param name="Id">作业定义标识。</param>
/// <param name="JobKey">稳定的作业业务键，用于跨模块引用。</param>
/// <param name="HandlerKind">执行器稳定机器码，取值自 JobHandlerKinds。</param>
/// <param name="Args">执行参数；HTTP 任务反序列化为 HttpJobArgs，其它 Handler 可为 null。</param>
/// <param name="DisplayName">面向管理员的展示名称。</param>
/// <param name="Description">作业用途说明，可空。</param>
/// <param name="GroupName">分组名称，用于列表聚合展示；可空表示未分组。</param>
/// <param name="IsEnabled">是否启用；停用后任何调度均不产生新执行。</param>
/// <param name="AllowConcurrentExecutions">是否允许同一作业定义并发多次执行。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最后更新时间（UTC），可空。</param>
/// <param name="Version">乐观并发版本号。</param>
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

/// <summary>
/// 创建作业定义的请求契约。
/// </summary>
/// <param name="JobKey">稳定作业业务键；创建后不可更改，用于跨模块与事件引用。</param>
/// <param name="HandlerKind">执行器稳定机器码，取值自 JobHandlerKinds。</param>
/// <param name="Args">执行参数；HTTP 任务应传入 HttpJobArgs。</param>
/// <param name="DisplayName">面向管理员的展示名称。</param>
/// <param name="Description">作业用途说明，可空。</param>
/// <param name="GroupName">分组名称，可空。</param>
/// <param name="AllowConcurrentExecutions">是否允许同一作业定义并发多次执行；默认 false。</param>
public sealed record CreateHostJobDefinitionRequest(
    string JobKey,
    string HandlerKind,
    HttpJobArgs? Args,
    string DisplayName,
    string? Description,
    string? GroupName,
    bool AllowConcurrentExecutions = false);

/// <summary>
/// 更新作业定义的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">用途说明，可空。</param>
/// <param name="GroupName">分组名称，可空。</param>
/// <param name="HandlerKind">执行器稳定机器码；允许切换以适配 Handler 升级。</param>
/// <param name="Args">执行参数。</param>
/// <param name="AllowConcurrentExecutions">是否允许并发执行。</param>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
public sealed record UpdateHostJobDefinitionRequest(
    string DisplayName,
    string? Description,
    string? GroupName,
    string HandlerKind,
    HttpJobArgs? Args,
    bool AllowConcurrentExecutions,
    int Version);

/// <summary>
/// 停用作业定义的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
public sealed record DisableHostJobDefinitionRequest(int Version);

/// <summary>
/// 删除作业定义的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
public sealed record DeleteHostJobDefinitionRequest(int Version);

/// <summary>作业分组去重选项，对应 Admin.NET ListJobGroup。</summary>
/// <param name="GroupName">分组名称；null 条目表示"未分组"桶。</param>
public sealed record HostJobGroupResponse(string GroupName);

/// <summary>
/// 作业调度计划响应契约；字段顺序为稳定机器码的一部分。
/// </summary>
/// <param name="Id">调度计划标识。</param>
/// <param name="JobDefinitionId">关联的作业定义标识。</param>
/// <param name="JobDefinitionJobKey">关联作业定义的 JobKey，冗余投影便于搜索。</param>
/// <param name="JobDefinitionDisplayName">关联作业定义的展示名称，冗余投影。</param>
/// <param name="TriggerKind">触发方式稳定机器码，取值自 JobTriggerKinds。</param>
/// <param name="CronExpression">Cron 表达式；TriggerKind 为 Cron 时必填，否则为 null。</param>
/// <param name="TimeZoneId">IANA/Windows 标准时区标识；Cron 与 OneTime 均需指定。</param>
/// <param name="OneTimeAtUtc">一次性触发的绝对时间（UTC）；TriggerKind 为 OneTime 时必填，否则为 null。</param>
/// <param name="MisfirePolicy">错失触发补偿策略，取值自 JobMisfirePolicies。</param>
/// <param name="IsEnabled">调度计划是否启用；暂停期间不产生新触发。</param>
/// <param name="NextExecutionAtUtc">下一次预计触发时间（UTC），计算值可空。</param>
/// <param name="LastExecutionAtUtc">上一次触发发生时间（UTC），可空。</param>
/// <param name="CompletedAtUtc">调度计划完成时间，如一次性任务结束或被暂停。</param>
/// <param name="NumberOfRuns">累计成功执行次数。</param>
/// <param name="NumberOfErrors">累计失败执行次数。</param>
/// <param name="StartTime">调度生效起始时间，可空。</param>
/// <param name="EndTime">调度生效结束时间，可空。</param>
/// <param name="Args">本次调度计划覆盖的执行参数 JSON；null 表示继承作业定义默认参数。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最后更新时间（UTC），可空。</param>
/// <param name="Version">乐观并发版本号。</param>
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

/// <summary>
/// 创建作业调度计划的请求契约。
/// </summary>
/// <param name="JobDefinitionId">关联的作业定义标识。</param>
/// <param name="TriggerKind">触发方式稳定机器码，取值自 JobTriggerKinds。</param>
/// <param name="CronExpression">Cron 表达式；TriggerKind 为 Cron 时必填。</param>
/// <param name="TimeZoneId">IANA/Windows 标准时区标识。</param>
/// <param name="OneTimeAtUtc">一次性触发绝对时间（UTC）；TriggerKind 为 OneTime 时必填。</param>
/// <param name="MisfirePolicy">错失触发补偿策略，取值自 JobMisfirePolicies。</param>
/// <param name="StartTime">调度生效起始时间，可空。</param>
/// <param name="EndTime">调度生效结束时间，可空。</param>
/// <param name="Args">调度级执行参数 JSON 覆盖；null 表示继承作业定义。</param>
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

/// <summary>
/// 更新作业调度计划的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <param name="TriggerKind">触发方式稳定机器码。</param>
/// <param name="CronExpression">Cron 表达式。</param>
/// <param name="TimeZoneId">IANA/Windows 标准时区标识。</param>
/// <param name="OneTimeAtUtc">一次性触发绝对时间（UTC）。</param>
/// <param name="MisfirePolicy">错失触发补偿策略。</param>
/// <param name="StartTime">调度生效起始时间。</param>
/// <param name="EndTime">调度生效结束时间。</param>
/// <param name="Args">调度级执行参数 JSON 覆盖。</param>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
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

/// <summary>
/// 切换作业调度计划启用/暂停状态的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
public sealed record ChangeHostJobScheduleStateRequest(int Version);

/// <summary>
/// 调度计划可选的作业定义下拉响应契约。
/// </summary>
/// <param name="Id">作业定义标识。</param>
/// <param name="JobKey">作业稳定业务键。</param>
/// <param name="HandlerKind">执行器稳定机器码。</param>
/// <param name="DisplayName">展示名称。</param>
public sealed record HostJobScheduleDefinitionOptionResponse(
    Guid Id,
    string JobKey,
    string HandlerKind,
    string DisplayName);

/// <summary>
/// Cron 表达式预览响应契约，用于前端人类可读解释与就近触发预览。
/// </summary>
/// <param name="HumanDescription">已本地化的 Cron 可读描述。</param>
/// <param name="NextExecutionAtUtc">下一次预计触发时间（UTC）。</param>
/// <param name="NextOccurrencesUtc">未来若干次触发时间的有序列表，用于预览。</param>
public sealed record HostJobScheduleCronPreviewResponse(
    string HumanDescription,
    DateTimeOffset NextExecutionAtUtc,
    IReadOnlyList<DateTimeOffset> NextOccurrencesUtc);

/// <summary>
/// 作业调度健康总览响应契约，用于运维面板。
/// </summary>
/// <param name="RegisteredHandlers">当前集群已注册的执行器稳定键集合。</param>
/// <param name="Backlog">当前积压快照。</param>
/// <param name="Workers">当前活动 Worker 实例集合。</param>
public sealed record HostJobHealthResponse(
    IReadOnlyList<string> RegisteredHandlers,
    HostJobHealthBacklogSnapshot Backlog,
    IReadOnlyList<HostJobWorkerInstanceResponse> Workers);

/// <summary>
/// 作业积压快照，反映待领取与到期重试的队列压力。
/// </summary>
/// <param name="PendingCount">处于 Pending 状态且未被任何 Worker 租约领取的执行数。</param>
/// <param name="OldestClaimableCreatedAtUtc">最早可领取执行的创建时间，可空表示积压为空。</param>
/// <param name="DueRetryCount">已到达下次重试时间但尚未被领取的重试执行数。</param>
/// <param name="OldestDueRetryAtUtc">最早到期重试的预计重试时间，可空表示无到期重试。</param>
public sealed record HostJobHealthBacklogSnapshot(
    long PendingCount,
    DateTimeOffset? OldestClaimableCreatedAtUtc,
    long DueRetryCount,
    DateTimeOffset? OldestDueRetryAtUtc);

/// <summary>
/// Worker 实例存活响应条目；IsStale 为 true 表示心跳超时，实例可能已失联。
/// </summary>
/// <param name="InstanceId">Worker 实例的唯一标识，进程启动时生成。</param>
/// <param name="HostProfile">主机与部署角色的复合描述，便于运维定位。</param>
/// <param name="StartedAtUtc">Worker 进程启动时间（UTC）。</param>
/// <param name="LastHeartbeatAtUtc">最近一次心跳写入时间（UTC）。</param>
/// <param name="WorkerVersion">Worker 构建版本号，可空。</param>
/// <param name="IsStale">true 表示心跳已超过阈值判定为失联，false 为健康。</param>
public sealed record HostJobWorkerInstanceResponse(
    Guid InstanceId,
    string HostProfile,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    string? WorkerVersion,
    bool IsStale);

/// <summary>
/// 单次作业执行记录响应契约，用于执行列表与详情。
/// </summary>
/// <param name="Id">执行记录标识。</param>
/// <param name="JobDefinitionId">关联作业定义标识。</param>
/// <param name="JobScheduleId">关联调度计划标识；手动触发时为 null。</param>
/// <param name="Status">执行状态稳定机器码，取值自 JobExecutionStatuses。</param>
/// <param name="TriggerKind">触发方式稳定机器码，取值自 JobTriggerKinds。</param>
/// <param name="ScheduledForUtc">调度原定触发时间（UTC），手动触发时为 null。</param>
/// <param name="ErrorMessage">最后一次失败的错误消息；成功或从未失败时为 null。</param>
/// <param name="StartedAtUtc">实际开始执行时间（UTC），Pending 时为 null。</param>
/// <param name="FinishedAtUtc">执行结束时间（UTC），仍在运行时为 null。</param>
/// <param name="NextAttemptAtUtc">下次重试预计时间（UTC），不再重试时为 null。</param>
/// <param name="AttemptCount">已执行尝试次数，从 1 开始。</param>
/// <param name="CreatedAtUtc">执行记录创建时间（UTC）。</param>
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
