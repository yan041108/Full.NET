using StackExchange.Redis;

namespace Full.NET.Realtime.SignalR;

internal static class RealtimeRedisConfiguration
{
    public static ConfigurationOptions Create(
        string connectionString,
        string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        if (!IsCanonicalEnvironmentName(environmentName))
        {
            throw new ArgumentException(
                "Environment name must contain only ASCII letters, digits, or hyphens and start and end with a letter or digit.",
                nameof(environmentName));
        }

        var configuration = ConfigurationOptions.Parse(connectionString);
        // Backplane 短暂中断不应迫使 API 宿主重启；投递失败仍由调用 Task 原样暴露。
        configuration.AbortOnConnectFail = false;
        configuration.ChannelPrefix = RedisChannel.Literal(
            $"fullnet:{environmentName.ToLowerInvariant()}:signalr:");
        return configuration;
    }

    private static bool IsCanonicalEnvironmentName(string value)
    {
        // 环境名直接进入 Redis Channel Prefix，必须拒绝会制造分段歧义或跨环境碰撞的值。
        return IsAsciiLetterOrDigit(value[0])
            && IsAsciiLetterOrDigit(value[^1])
            && value.All(character =>
                IsAsciiLetterOrDigit(character)
                || character == '-');
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';
}
