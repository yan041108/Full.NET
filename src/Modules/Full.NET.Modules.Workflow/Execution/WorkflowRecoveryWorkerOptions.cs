using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>Recovery Worker 的批大小、租约、退避与扫描边界。</summary>
internal sealed class WorkflowRecoveryWorkerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Workflow:RecoveryWorker";

    /// <summary>每轮最多领取的恢复任务数量。</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>未满批时两次轮询之间的等待毫秒数。</summary>
    public int PollMilliseconds { get; set; } = 1000;

    /// <summary>领取后租约有效秒数。</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>剩余租约低于该秒数时必须续租。</summary>
    public int RenewWhenRemainingSeconds { get; set; } = 30;

    /// <summary>单条恢复任务允许的最大尝试次数。</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>瞬时失败的基础退避秒数。</summary>
    public int RetryDelaySeconds { get; set; } = 2;

    /// <summary>退避模式；只允许 <c>fixed</c> 或 <c>exponential</c>。</summary>
    public string RetryBackoffMode { get; set; } = "exponential";

    /// <summary>退避后的最大延迟秒数。</summary>
    public int RetryMaxDelaySeconds { get; set; } = 3600;
}

/// <summary>拒绝会让 Worker 空转、饿死或无限重试的恢复选项。</summary>
internal sealed class WorkflowRecoveryWorkerOptionsValidator
    : IValidateOptions<WorkflowRecoveryWorkerOptions>
{
    /// <summary>校验批大小、租约窗口和退避边界。</summary>
    /// <param name="name">选项名称。</param>
    /// <param name="options">待校验的 Worker 选项。</param>
    /// <returns>成功或包含全部失败原因的结果。</returns>
    public ValidateOptionsResult Validate(string? name, WorkflowRecoveryWorkerOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 50)
        {
            failures.Add("Workflow:RecoveryWorker:BatchSize must be between 1 and 50.");
        }

        if (options.PollMilliseconds is < 100 or > 60000)
        {
            failures.Add("Workflow:RecoveryWorker:PollMilliseconds must be between 100 and 60000.");
        }

        if (options.LeaseSeconds is < 30 or > 3600)
        {
            failures.Add("Workflow:RecoveryWorker:LeaseSeconds must be between 30 and 3600.");
        }

        if (options.RenewWhenRemainingSeconds is < 5 or > 1800)
        {
            failures.Add("Workflow:RecoveryWorker:RenewWhenRemainingSeconds must be between 5 and 1800.");
        }

        if (options.RenewWhenRemainingSeconds >= options.LeaseSeconds)
        {
            failures.Add(
                "Workflow:RecoveryWorker:RenewWhenRemainingSeconds must be less than LeaseSeconds.");
        }

        if (options.MaxAttempts is < 1 or > 16)
        {
            failures.Add("Workflow:RecoveryWorker:MaxAttempts must be between 1 and 16.");
        }

        if (options.RetryDelaySeconds is < 1 or > 86400)
        {
            failures.Add("Workflow:RecoveryWorker:RetryDelaySeconds must be between 1 and 86400.");
        }

        if (!string.Equals(options.RetryBackoffMode, "fixed", StringComparison.Ordinal)
            && !string.Equals(options.RetryBackoffMode, "exponential", StringComparison.Ordinal))
        {
            failures.Add("Workflow:RecoveryWorker:RetryBackoffMode must be 'fixed' or 'exponential'.");
        }

        if (options.RetryMaxDelaySeconds is < 1 or > 86400)
        {
            failures.Add("Workflow:RecoveryWorker:RetryMaxDelaySeconds must be between 1 and 86400.");
        }

        if (options.RetryMaxDelaySeconds < options.RetryDelaySeconds)
        {
            failures.Add(
                "Workflow:RecoveryWorker:RetryMaxDelaySeconds must not be less than RetryDelaySeconds.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
