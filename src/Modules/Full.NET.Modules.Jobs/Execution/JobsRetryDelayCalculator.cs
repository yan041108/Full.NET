namespace Full.NET.Modules.Jobs.Execution;

/// <summary>
/// 计算显式可重试失败的有界延迟，并保持固定模式的兼容行为。
/// </summary>
internal static class JobsRetryDelayCalculator
{
    /// <summary>
    /// 按当前一基尝试次数计算退避与对称抖动后的秒数。
    /// </summary>
    public static int CalculateSeconds(
        JobsWorkerOptions options,
        int attemptCount,
        double unitIntervalSample)
    {
        ArgumentNullException.ThrowIfNull(options);

        long delaySeconds = options.RetryDelaySeconds;
        if (string.Equals(
                options.RetryBackoffMode,
                "exponential",
                StringComparison.Ordinal))
        {
            var remainingDoublings = Math.Max(attemptCount - 1, 0);
            while (remainingDoublings > 0
                   && delaySeconds < options.RetryMaxDelaySeconds)
            {
                delaySeconds = delaySeconds
                    > options.RetryMaxDelaySeconds / 2L
                    ? options.RetryMaxDelaySeconds
                    : delaySeconds * 2L;
                remainingDoublings--;
            }
        }

        delaySeconds = Math.Min(delaySeconds, options.RetryMaxDelaySeconds);
        var normalizedSample =
            (Math.Clamp(unitIntervalSample, 0d, 1d) * 2d) - 1d;
        var jitterFactor =
            1d + (normalizedSample * options.RetryJitterPercent / 100d);
        var jitteredDelay = (long)Math.Round(
            delaySeconds * jitterFactor,
            MidpointRounding.AwayFromZero);

        return (int)Math.Clamp(
            jitteredDelay,
            1L,
            options.RetryMaxDelaySeconds);
    }
}

/// <summary>
/// 提供不携带任务或租户上下文的单位区间随机样本。
/// </summary>
internal interface IJobsRetryJitterSource
{
    /// <summary>返回用于对称抖动计算的单位区间样本。</summary>
    double NextUnitInterval();
}

/// <summary>
/// 使用进程共享随机源提供无状态重试抖动样本。
/// </summary>
internal sealed class SystemJobsRetryJitterSource : IJobsRetryJitterSource
{
    /// <inheritdoc />
    public double NextUnitInterval()
    {
        return Random.Shared.NextDouble();
    }
}
