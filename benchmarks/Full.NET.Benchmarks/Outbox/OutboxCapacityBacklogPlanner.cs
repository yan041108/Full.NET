namespace Full.NET.Benchmarks.Outbox;

/// <summary>
/// 根据预热实测速率为正式采样窗口规划充足积压，避免固定种子数量把队列耗尽误判为容量失败。
/// </summary>
public static class OutboxCapacityBacklogPlanner
{
    private const double SamplingSafetyFactor = 1.5d;

    /// <summary>
    /// 计算采样开始前应保留的最小待处理消息数。
    /// </summary>
    public static int CalculateSamplingTarget(
        int configuredSeedMessages,
        long warmupCompletedMessages,
        TimeSpan warmup,
        TimeSpan duration,
        int batchSize,
        int replicas)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            configuredSeedMessages);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupCompletedMessages);
        ArgumentOutOfRangeException.ThrowIfLessThan(warmup, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            duration,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(replicas);

        if (warmup == TimeSpan.Zero || warmupCompletedMessages == 0)
        {
            return configuredSeedMessages;
        }

        var observedMessagesPerSecond =
            warmupCompletedMessages / warmup.TotalSeconds;
        var observedWindowDemand = Math.Ceiling(
            observedMessagesPerSecond
            * duration.TotalSeconds
            * SamplingSafetyFactor);
        var inFlightReserve = checked((long)batchSize * replicas);
        var adaptiveTarget =
            checked((long)observedWindowDemand) + inFlightReserve;
        return checked((int)Math.Max(configuredSeedMessages, adaptiveTarget));
    }

    /// <summary>
    /// 计算把当前积压补到采样目标所需的新消息数。
    /// </summary>
    public static int CalculateDeficit(
        long currentPendingMessages,
        int targetPendingMessages)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentPendingMessages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            targetPendingMessages);

        return currentPendingMessages >= targetPendingMessages
            ? 0
            : checked((int)(targetPendingMessages - currentPendingMessages));
    }
}
