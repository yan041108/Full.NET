namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 无在途 Handler 时允许较长阻塞 Poll；存在分区完成命令时缩短 Poll，
/// 避免固定 Heartbeat 周期成为每分区吞吐上限。
/// </summary>
internal static class KafkaConsumerPollTiming
{
    public static TimeSpan Resolve(
        KafkaMessagingOptions options,
        int inFlightCount,
        bool hasPendingCompletion)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(inFlightCount);
        return TimeSpan.FromMilliseconds(
            inFlightCount > 0 || hasPendingCompletion
                ? options.CompletionPollMilliseconds
                : options.HandlerHeartbeatMilliseconds);
    }
}
