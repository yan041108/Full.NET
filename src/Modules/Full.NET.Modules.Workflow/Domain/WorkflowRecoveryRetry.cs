using Full.NET.Modules.Workflow.Execution;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>恢复失败分类与有界退避；耗尽后必须死信并暂停实例。</summary>
internal static class WorkflowRecoveryRetry
{
    /// <summary>源条件已消失或已修复。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>瞬时失败，允许按退避重试。</summary>
    public const string Retryable = "retryable";

    /// <summary>不可自动修复，耗尽后死信。</summary>
    public const string Exhausted = "exhausted";

    /// <summary>计算下一次自动领取时间。</summary>
    /// <param name="now">当前 UTC 时间。</param>
    /// <param name="attemptNumber">已经完成的尝试次数。</param>
    /// <param name="options">Worker 退避选项。</param>
    /// <returns>下一次允许领取的时间。</returns>
    public static DateTimeOffset ComputeNextAttempt(
        DateTimeOffset now,
        int attemptNumber,
        WorkflowRecoveryWorkerOptions options)
    {
        var baseSeconds = options.RetryDelaySeconds;
        var delaySeconds = string.Equals(options.RetryBackoffMode, "exponential", StringComparison.Ordinal)
            ? baseSeconds * Math.Pow(2, Math.Max(attemptNumber - 1, 0))
            : baseSeconds;
        delaySeconds = Math.Min(delaySeconds, options.RetryMaxDelaySeconds);
        return now.AddSeconds(Math.Max(1, delaySeconds));
    }

    /// <summary>把处理结果映射为任务状态、下次领取时间，以及是否必须暂停实例。</summary>
    /// <param name="resultCategory">闭合结果类别。</param>
    /// <param name="attemptNumber">本次结束后的累计尝试次数。</param>
    /// <param name="now">当前 UTC 时间。</param>
    /// <param name="options">Worker 选项。</param>
    /// <returns>状态、下次尝试时间和是否暂停实例。</returns>
    public static (string Status, DateTimeOffset? NextAttempt, bool SuspendInstance) ResolveOutcome(
        string resultCategory,
        int attemptNumber,
        DateTimeOffset now,
        WorkflowRecoveryWorkerOptions options)
    {
        if (resultCategory == Succeeded)
        {
            return (WorkflowRecoveryStatuses.Succeeded, null, false);
        }

        if (attemptNumber >= options.MaxAttempts)
        {
            return (WorkflowRecoveryStatuses.DeadLettered, null, true);
        }

        return (
            WorkflowRecoveryStatuses.Failed,
            ComputeNextAttempt(now, attemptNumber, options),
            false);
    }
}
