using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Execution;

/// <summary>Delivery Worker 的批大小、租约、退避与积压采样边界。</summary>
internal sealed class NotificationDeliveryWorkerOptions
{
    public const string SectionName = "Notifications:Worker";

    /// <summary>每轮最多领取的投递数量。</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>同一批次内允许并行调用 Provider 的最大数量；默认串行。</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>未满批时两次轮询之间的等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 1000;

    /// <summary>领取后租约有效秒数。</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>单条投递允许的最大尝试次数。</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>瞬时失败的基础退避秒数。</summary>
    public int RetryDelaySeconds { get; set; } = 2;

    /// <summary>退避模式；只允许 <c>fixed</c> 或 <c>exponential</c>。</summary>
    public string RetryBackoffMode { get; set; } = "exponential";

    /// <summary>退避后的最大延迟秒数。</summary>
    public int RetryMaxDelaySeconds { get; set; } = 3600;

    /// <summary>积压快照采样间隔秒数。</summary>
    public int BacklogSampleSeconds { get; set; } = 30;
}

internal sealed class NotificationDeliveryWorkerOptionsValidator
    : IValidateOptions<NotificationDeliveryWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationDeliveryWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 50)
        {
            failures.Add("Notifications:Worker:BatchSize must be between 1 and 50.");
        }

        if (options.PollMilliseconds is < 100 or > 60000)
        {
            failures.Add("Notifications:Worker:PollMilliseconds must be between 100 and 60000.");
        }

        if (options.MaxConcurrency is < 1 or > 16)
        {
            failures.Add("Notifications:Worker:MaxConcurrency must be between 1 and 16.");
        }

        if (options.MaxConcurrency > options.BatchSize)
        {
            failures.Add("Notifications:Worker:MaxConcurrency must not exceed BatchSize.");
        }

        if (options.LeaseSeconds is < 30 or > 3600)
        {
            failures.Add("Notifications:Worker:LeaseSeconds must be between 30 and 3600.");
        }

        if (options.MaxAttempts is < 1 or > 16)
        {
            failures.Add("Notifications:Worker:MaxAttempts must be between 1 and 16.");
        }

        if (options.RetryDelaySeconds is < 1 or > 86400)
        {
            failures.Add("Notifications:Worker:RetryDelaySeconds must be between 1 and 86400.");
        }

        if (!string.Equals(options.RetryBackoffMode, "fixed", StringComparison.Ordinal)
            && !string.Equals(options.RetryBackoffMode, "exponential", StringComparison.Ordinal))
        {
            failures.Add("Notifications:Worker:RetryBackoffMode must be 'fixed' or 'exponential'.");
        }

        if (options.RetryMaxDelaySeconds is < 1 or > 86400)
        {
            failures.Add("Notifications:Worker:RetryMaxDelaySeconds must be between 1 and 86400.");
        }

        if (options.RetryMaxDelaySeconds < options.RetryDelaySeconds)
        {
            failures.Add(
                "Notifications:Worker:RetryMaxDelaySeconds must not be less than RetryDelaySeconds.");
        }

        if (options.BacklogSampleSeconds is < 5 or > 3600)
        {
            failures.Add("Notifications:Worker:BacklogSampleSeconds must be between 5 and 3600.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
