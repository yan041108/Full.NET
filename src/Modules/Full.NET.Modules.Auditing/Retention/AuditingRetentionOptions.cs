using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

internal sealed class AuditingRetentionOptions
{
    public const string SectionName = "Auditing:Retention";

    /// <summary>
    /// 生产默认关闭；部署方确认适用的数据保留制度后才能启用。
    /// </summary>
    public bool Enabled { get; set; }

    public int AccessRetentionDays { get; set; } = 30;

    public int OperationRetentionDays { get; set; } = 365;

    public int ExceptionRetentionDays { get; set; } = 90;

    public int OutboundRetentionDays { get; set; } = 90;

    public int BatchSize { get; set; } = 200;

    public int MaxBatchesPerRun { get; set; } = 15;

    public int PollSeconds { get; set; } = 3600;
}

internal sealed class AuditingRetentionOptionsValidator
    : IValidateOptions<AuditingRetentionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AuditingRetentionOptions options)
    {
        var failures = new List<string>();
        ValidateRange(
            options.AccessRetentionDays,
            1,
            3650,
            "Auditing:Retention:AccessRetentionDays",
            failures);
        ValidateRange(
            options.OperationRetentionDays,
            1,
            3650,
            "Auditing:Retention:OperationRetentionDays",
            failures);
        ValidateRange(
            options.ExceptionRetentionDays,
            1,
            3650,
            "Auditing:Retention:ExceptionRetentionDays",
            failures);
        ValidateRange(
            options.OutboundRetentionDays,
            1,
            3650,
            "Auditing:Retention:OutboundRetentionDays",
            failures);
        ValidateRange(
            options.BatchSize,
            1,
            2000,
            "Auditing:Retention:BatchSize",
            failures);
        ValidateRange(
            options.MaxBatchesPerRun,
            1,
            100,
            "Auditing:Retention:MaxBatchesPerRun",
            failures);
        ValidateRange(
            options.PollSeconds,
            60,
            86400,
            "Auditing:Retention:PollSeconds",
            failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string key,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"{key} must be between {minimum} and {maximum}.");
        }
    }
}
