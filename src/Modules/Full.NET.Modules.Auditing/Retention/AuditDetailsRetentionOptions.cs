using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

/// <summary>操作日志详情的独立到期清理预算；普通审计摘要保留开关不控制此清理。</summary>
internal sealed class AuditDetailsRetentionOptions
{
    public const string SectionName = "Auditing:DetailsRetention";

    public int BatchSize { get; set; } = 200;

    public int MaxBatchesPerRun { get; set; } = 15;

    public int PollSeconds { get; set; } = 60;
}

internal sealed class AuditDetailsRetentionOptionsValidator
    : IValidateOptions<AuditDetailsRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, AuditDetailsRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 2000)
        {
            failures.Add("Auditing:DetailsRetention:BatchSize must be between 1 and 2000.");
        }

        if (options.MaxBatchesPerRun is < 1 or > 100)
        {
            failures.Add("Auditing:DetailsRetention:MaxBatchesPerRun must be between 1 and 100.");
        }

        if (options.PollSeconds is < 60 or > 86400)
        {
            failures.Add("Auditing:DetailsRetention:PollSeconds must be between 60 and 86400.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
