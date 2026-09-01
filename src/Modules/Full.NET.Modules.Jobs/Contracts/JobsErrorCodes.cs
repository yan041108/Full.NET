namespace Full.NET.Modules.Jobs.Contracts;

public static class JobsErrorCodes
{
    /// <summary>Jobs 模块所有错误码的前缀。</summary>
    public const string Prefix = "jobs.";

    /// <summary>作业定义标识不存在或已删除。</summary>
    public const string DefinitionNotFound = "jobs.definition_not_found";

    /// <summary>乐观并发冲突：作业定义已被其他请求修改。</summary>
    public const string DefinitionConcurrencyConflict = "jobs.definition_concurrency_conflict";

    /// <summary>作业业务键已存在，禁止重复创建。</summary>
    public const string DefinitionJobKeyExists = "jobs.definition_job_key_exists";

    /// <summary>作业定义字段或调度参数校验失败。</summary>
    public const string DefinitionValidationFailed = "jobs.definition_validation_failed";

    /// <summary>创建/更新作业时必须指定有效的处理器种类。</summary>
    public const string HandlerKindRequired = "jobs.handler_kind_required";

    /// <summary>HTTP 处理器将敏感头放入普通 Headers 集合，必须移到 SensitiveHeaders。</summary>
    public const string SensitiveHeaderInPlainHeaders =
        "jobs.sensitive_header_in_plain_headers";

    /// <summary>作业定义已被禁用，禁止触发新的执行。</summary>
    public const string DefinitionDisabled = "jobs.definition_disabled";

    /// <summary>作业定义仍存在活跃计划或执行记录，禁止删除。</summary>
    public const string DefinitionHasActiveDependents = "jobs.definition_has_active_dependents";

    /// <summary>未在处理器注册表中找到对应 HandlerKind 的实现。</summary>
    public const string HandlerNotFound = "jobs.handler_not_found";

    /// <summary>执行标识不存在或属于其他租户。</summary>
    public const string ExecutionNotFound = "jobs.execution_not_found";

    /// <summary>计划标识不存在或已被删除。</summary>
    public const string ScheduleNotFound = "jobs.schedule_not_found";

    /// <summary>乐观并发冲突：任务计划已被其他请求修改。</summary>
    public const string ScheduleConcurrencyConflict =
        "jobs.schedule_concurrency_conflict";

    /// <summary>任务计划字段或 Cron/间隔表达式校验失败。</summary>
    public const string ScheduleValidationFailed =
        "jobs.schedule_validation_failed";

    /// <summary>任务计划仍存在未终结的执行记录，禁止删除。</summary>
    public const string ScheduleHasActiveExecutions = "jobs.schedule_has_active_executions";

    /// <summary>已发布的全部 Jobs 错误码集合。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        DefinitionNotFound,
        DefinitionConcurrencyConflict,
        DefinitionJobKeyExists,
        DefinitionValidationFailed,
        HandlerKindRequired,
        SensitiveHeaderInPlainHeaders,
        DefinitionDisabled,
        DefinitionHasActiveDependents,
        HandlerNotFound,
        ExecutionNotFound,
        ScheduleNotFound,
        ScheduleConcurrencyConflict,
        ScheduleValidationFailed,
        ScheduleHasActiveExecutions,
    ]);
}
