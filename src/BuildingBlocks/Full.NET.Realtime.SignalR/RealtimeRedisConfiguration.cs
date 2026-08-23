using StackExchange.Redis;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 构造 SignalR Redis Backplane 的 <see cref="ConfigurationOptions"/>；
/// 多实例横向扩展时由该 Backplane 在不同 API 实例间分发组消息与连接事件。
/// </summary>
/// <remarks>
/// <para>Backplane 是多实例 SignalR 拓扑的必需组件；缺省时同组消息只对当前实例可见，
/// 会导致跨实例用户漏收通知。生产环境必须与 Cache Redis 物理隔离，由
/// <c>ServiceCollectionExtensions</c> 强制校验连接串差异。</para>
/// <para>本类只负责生成连接选项，不持有连接；运行连接由 SignalR Redis 包统一管理，
/// 默认 <c>AbortOnConnectFail=false</c> 以避免 Backplane 短暂中断导致 API 宿主重启。</para>
/// </remarks>
internal static class RealtimeRedisConfiguration
{
    /// <summary>
    /// 按连接串与环境名构造 Redis Backplane 连接选项。
    /// </summary>
    /// <param name="connectionString">Redis 连接串，必须已通过调用方校验非空。</param>
    /// <param name="environmentName">环境名，仅允许 ASCII 字母、数字与连字符，且首尾为字母或数字。</param>
    /// <returns>已配置通道前缀与重连策略的 <see cref="ConfigurationOptions"/>。</returns>
    /// <exception cref="ArgumentException">连接串或环境名为空、空白或环境名不符合规范字符集。</exception>
    /// <remarks>
    /// 通道前缀含应用名 <c>fullnet</c> 与环境名，用于在共享 Redis 故障域内隔离不同应用/环境的 SignalR 通道；
    /// 物理隔离仍由独立连接串保证，前缀不能替代物理隔离边界。
    /// </remarks>
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
        // 前缀含应用名 fullnet + 环境，避免跨应用/跨环境通道碰撞；物理隔离仍靠独立连接串。
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
