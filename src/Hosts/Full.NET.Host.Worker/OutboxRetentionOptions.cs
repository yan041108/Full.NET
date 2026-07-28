using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

internal sealed class OutboxRetentionOptions
{
    public const string SectionName = "OutboxRetention";

    /// <summary>
    /// 生产默认关闭；部署方确认数据保留制度后才能启用。
    /// </summary>
    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 30;

    public int BatchSize { get; set; } = 200;

    public int MaxBatchesPerRun { get; set; } = 15;

    public int PollSeconds { get; set; } = 3600;
}

internal sealed class OutboxRetentionOptionsValidator
    : IValidateOptions<OutboxRetentionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        OutboxRetentionOptions options)
    {
        var failures = new List<string>();
        ValidateRange(
            options.RetentionDays,
            1,
            3650,
            "OutboxRetention:RetentionDays",
            failures);
        ValidateRange(
            options.BatchSize,
            1,
            2000,
            "OutboxRetention:BatchSize",
            failures);
        ValidateRange(
            options.MaxBatchesPerRun,
            1,
            100,
            "OutboxRetention:MaxBatchesPerRun",
            failures);
        ValidateRange(
            options.PollSeconds,
            60,
            86400,
            "OutboxRetention:PollSeconds",
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
