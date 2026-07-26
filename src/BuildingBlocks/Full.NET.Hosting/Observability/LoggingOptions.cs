namespace Full.NET.Hosting.Observability;

/// <summary>
/// 定义普通与高优先级日志通道的有界容量。
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "FullNet:Logging";

    /// <summary>
    /// 获取或设置普通日志通道容量。
    /// </summary>
    public int AsyncBufferSize { get; set; } = 10_000;

    /// <summary>
    /// 获取或设置 Error/Critical 高优先级日志通道容量。
    /// </summary>
    public int HighPriorityAsyncBufferSize { get; set; } = 1_000;

    /// <summary>
    /// 获取或设置是否在普通日志队列满时阻塞调用方。
    /// </summary>
    /// <remarks>
    /// Full.NET 禁止启用该兼容配置；属性仅用于在启动时给出明确校验错误。
    /// </remarks>
    public bool BlockWhenFull { get; set; }

    /// <summary>
    /// 获取或设置两条日志通道在宿主退出时共享的最大排空时间。
    /// </summary>
    /// <remarks>
    /// 该预算只约束进程退出等待，不允许日志调用方在运行期间同步等待 Sink。
    /// </remarks>
    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
