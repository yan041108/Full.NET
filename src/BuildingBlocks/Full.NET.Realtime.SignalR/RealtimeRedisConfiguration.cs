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

        var configuration = ConfigurationOptions.Parse(connectionString);
        // Backplane 短暂中断不应迫使 API 宿主重启；投递失败仍由调用 Task 原样暴露。
        configuration.AbortOnConnectFail = false;
        configuration.ChannelPrefix = RedisChannel.Literal(
            $"fullnet:{environmentName.ToLowerInvariant()}:signalr:");
        return configuration;
    }
}
