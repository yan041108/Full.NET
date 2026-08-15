namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Topic 名称标准化构造器；统一管理基础 Topic、重试 Topic 与死信 Topic 的命名规则。
/// 禁止业务代码直接拼接字符串，防止自定义后缀与系统治理 Topic（.retry.*/.dlq）发生命名冲突。
/// </summary>
internal static class KafkaTopicNames
{
    /// <summary>
    /// 死信 Topic 固定后缀，附加在基础 Topic 名称之后。
    /// </summary>
    public const string DeadLetterSuffix = ".dlq";

    /// <summary>
    /// 重试 Topic 中间段，格式为 <c>{baseTopic}.retry.{stage}</c>，stage 为退避阶段标识符。
    /// </summary>
    public const string RetrySegment = ".retry.";

    /// <summary>
    /// 根据基础 Topic 名称获取对应死信 Topic。
    /// </summary>
    /// <param name="baseTopic">目录注册的基础业务 Topic。</param>
    /// <returns>基础名 + <see cref="DeadLetterSuffix"/> 组成的死信 Topic 名。</returns>
    public static string GetDeadLetterTopic(string baseTopic) => baseTopic + DeadLetterSuffix;

    /// <summary>
    /// 根据基础 Topic 与指定重试阶段构造重试 Topic。
    /// </summary>
    /// <param name="baseTopic">基础业务 Topic；不得已包含重试段或死信后缀。</param>
    /// <param name="stage">重试阶段标识，例如 <c>5s</c>、<c>1m</c>、<c>15m</c>。</param>
    /// <exception cref="ArgumentException">基础 Topic 已包含保留后缀时抛出。</exception>
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

    /// <summary>
    /// 从任意 Topic 名（基础/重试/死信）回推对应基础业务 Topic。
    /// </summary>
    /// <param name="topic">原始 Topic 名称。</param>
    /// <returns>去除重试段与死信后缀后的基础 Topic。</returns>
    public static string ResolveBaseTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (topic.EndsWith(DeadLetterSuffix, StringComparison.Ordinal)) return topic[..^DeadLetterSuffix.Length];
        var retryIndex = topic.LastIndexOf(RetrySegment, StringComparison.Ordinal);
        return retryIndex < 0 ? topic : topic[..retryIndex];
    }

    /// <summary>
    /// 尝试从 Topic 名中解析出重试阶段标识。
    /// </summary>
    /// <param name="topic">待检测 Topic 名。</param>
    /// <param name="stage">解析出的重试阶段字符串，失败时设为空字符串。</param>
    /// <returns>true 表示该 Topic 为重试 Topic 且阶段合法。</returns>
    public static bool TryGetRetryStage(string topic, out string stage)
    {
        stage = string.Empty;
        var retryIndex = topic.LastIndexOf(RetrySegment, StringComparison.Ordinal);
        if (retryIndex < 0) return false;
        stage = topic[(retryIndex + RetrySegment.Length)..];
        return !string.IsNullOrWhiteSpace(stage);
    }
}
