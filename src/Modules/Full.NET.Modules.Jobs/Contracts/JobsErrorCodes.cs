namespace Full.NET.Modules.Jobs.Contracts;

public static class JobsErrorCodes
{
    public const string Prefix = "jobs.";

    public const string DefinitionNotFound = "jobs.definition_not_found";

    public const string DefinitionConcurrencyConflict = "jobs.definition_concurrency_conflict";

    public const string DefinitionJobKeyExists = "jobs.definition_job_key_exists";

    public const string DefinitionValidationFailed = "jobs.definition_validation_failed";

    public const string HandlerKindRequired = "jobs.handler_kind_required";

    public const string SensitiveHeaderInPlainHeaders =
        "jobs.sensitive_header_in_plain_headers";

    public const string DefinitionDisabled = "jobs.definition_disabled";

    /// <summary>作业定义仍存在活跃计划或执行记录，禁止删除。</summary>
    public const string DefinitionHasActiveDependents = "jobs.definition_has_active_dependents";

    public const string HandlerNotFound = "jobs.handler_not_found";

    public const string ExecutionNotFound = "jobs.execution_not_found";

    public const string ScheduleNotFound = "jobs.schedule_not_found";

    public const string ScheduleConcurrencyConflict =
        "jobs.schedule_concurrency_conflict";

    public const string ScheduleValidationFailed =
        "jobs.schedule_validation_failed";

    /// <summary>任务计划仍存在未终结的执行记录，禁止删除。</summary>
    public const string ScheduleHasActiveExecutions = "jobs.schedule_has_active_executions";

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
