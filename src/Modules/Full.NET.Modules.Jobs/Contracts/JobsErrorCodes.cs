namespace Full.NET.Modules.Jobs.Contracts;

public static class JobsErrorCodes
{
    public const string Prefix = "jobs.";

    public const string DefinitionNotFound = "jobs.definition_not_found";

    public const string DefinitionConcurrencyConflict = "jobs.definition_concurrency_conflict";

    public const string DefinitionJobKeyExists = "jobs.definition_job_key_exists";

    public const string DefinitionValidationFailed = "jobs.definition_validation_failed";

    public const string DefinitionDisabled = "jobs.definition_disabled";

    public const string HandlerNotFound = "jobs.handler_not_found";

    public const string ExecutionNotFound = "jobs.execution_not_found";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        DefinitionNotFound,
        DefinitionConcurrencyConflict,
        DefinitionJobKeyExists,
        DefinitionValidationFailed,
        DefinitionDisabled,
        HandlerNotFound,
        ExecutionNotFound,
    ]);
}
