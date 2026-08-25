namespace Full.NET.Abstractions.Results;

/// <summary>
/// 表示服务在进入受保护资源前因静态容量边界拒绝本次操作。
/// </summary>
/// <remarks>
/// 该异常不包含下游连接字符串、池大小或队列深度，允许 HTTP 与 Worker 边界使用稳定失败语义，
/// 同时避免把内部容量拓扑暴露给调用方。
/// </remarks>
public sealed class ServiceCapacityExceededException(
    ServiceCapacityFailureKind kind,
    TimeSpan retryAfter)
    : Exception("The service capacity limit was reached.")
{
    /// <summary>获取容量拒绝类别。</summary>
    public ServiceCapacityFailureKind Kind { get; } = kind;

    /// <summary>获取调用方再次尝试前的最短建议等待时间。</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// 定义不会暴露资源细节的稳定容量失败类别。
/// </summary>
public enum ServiceCapacityFailureKind
{
    /// <summary>队列已满或配置为不允许排队。</summary>
    Rejected = 1,

    /// <summary>在静态等待上限内未取得许可证。</summary>
    Timeout = 2,
}
