namespace Full.NET.Messaging.Kafka;

internal static class KafkaTopicNames
{
    public const string DeadLetterSuffix = ".dlq";
    public const string RetrySegment = ".retry.";
    public static string GetDeadLetterTopic(string baseTopic) => baseTopic + DeadLetterSuffix;
    public static string GetRetryTopic(string baseTopic, string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (baseTopic.Contains(RetrySegment, StringComparison.Ordinal)
            || baseTopic.EndsWith(DeadLetterSuffix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Base topics must not use reserved retry or dead-letter suffixes.",
                nameof(baseTopic));
        }

        return $"{baseTopic}{RetrySegment}{stage}";
    }
    public static string ResolveBaseTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (topic.EndsWith(DeadLetterSuffix, StringComparison.Ordinal)) return topic[..^DeadLetterSuffix.Length];
        var retryIndex = topic.LastIndexOf(RetrySegment, StringComparison.Ordinal);
        return retryIndex < 0 ? topic : topic[..retryIndex];
    }
    public static bool TryGetRetryStage(string topic, out string stage)
    {
        stage = string.Empty;
        var retryIndex = topic.LastIndexOf(RetrySegment, StringComparison.Ordinal);
        if (retryIndex < 0) return false;
        stage = topic[(retryIndex + RetrySegment.Length)..];
        return !string.IsNullOrWhiteSpace(stage);
    }
}
