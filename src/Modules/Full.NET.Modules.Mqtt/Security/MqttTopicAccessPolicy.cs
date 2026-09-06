namespace Full.NET.Modules.Mqtt.Security;

/// <summary>MQTT 发布主题 ACL 判定；Host 与租户上下文使用不同前缀规则。</summary>
internal static class MqttTopicAccessPolicy
{
    /// <summary>判定当前上下文是否允许向指定主题发布。</summary>
    /// <param name="topic">目标主题。</param>
    /// <param name="isHost">是否处于 Host 上下文。</param>
    /// <param name="tenantId">当前租户标识；Host 上下文可为 <see langword="null"/>。</param>
    /// <param name="allowedPrefixes">配置允许的主题前缀白名单。</param>
    /// <returns>允许发布时返回 <see langword="true"/>。</returns>
    public static bool CanPublish(
        string topic,
        bool isHost,
        Guid? tenantId,
        IReadOnlyList<string> allowedPrefixes)
    {
        if (!IsPublishableTopic(topic))
        {
            return false;
        }

        if (!MatchesAllowedPrefix(topic, allowedPrefixes))
        {
            return false;
        }

        if (isHost)
        {
            return IsHostAllowedTopic(topic);
        }

        return tenantId is not null && IsTenantAllowedTopic(topic, tenantId.Value);
    }

    private static bool IsPublishableTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var normalized = topic.Trim();
        if (normalized.Contains('#', StringComparison.Ordinal)
            || normalized.Contains('+', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesAllowedPrefix(string topic, IReadOnlyList<string> allowedPrefixes)
    {
        foreach (var prefix in allowedPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            if (topic.StartsWith(prefix.Trim(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHostAllowedTopic(string topic)
    {
        if (topic.StartsWith("fullnet/", StringComparison.Ordinal))
        {
            return true;
        }

        return topic.StartsWith("tenants/", StringComparison.Ordinal)
            && TryReadTenantSegment(topic, out _);
    }

    private static bool IsTenantAllowedTopic(string topic, Guid tenantId)
    {
        if (!topic.StartsWith("tenants/", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadTenantSegment(topic, out var topicTenantId))
        {
            return false;
        }

        return topicTenantId == tenantId;
    }

    private static bool TryReadTenantSegment(string topic, out Guid tenantId)
    {
        tenantId = default;
        const string prefix = "tenants/";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = topic[prefix.Length..];
        var slashIndex = remainder.IndexOf('/');
        var tenantSegment = slashIndex < 0 ? remainder : remainder[..slashIndex];
        return Guid.TryParse(tenantSegment, out tenantId);
    }
}
