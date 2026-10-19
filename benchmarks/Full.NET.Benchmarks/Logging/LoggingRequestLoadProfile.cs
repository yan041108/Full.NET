namespace Full.NET.Benchmarks.Logging;

/// <summary>请求日志实验的固定有界负载；目标速率不代表服务端实际在途数。</summary>
public sealed class LoggingRequestLoadProfile
{
    private LoggingRequestLoadProfile(int requests, int rate, int sendBudgetMilliseconds)
        => (Requests, RequestsPerSecond, SendBudgetMilliseconds) = (requests, rate, sendBudgetMilliseconds);

    /// <summary>实测请求数量，预热不计入此值。</summary>
    public int Requests { get; }
    /// <summary>固定节奏的目标请求数/秒。</summary>
    public int RequestsPerSecond { get; }
    /// <summary>整个发送阶段的有界完成期限，包含调度落后；单位毫秒。</summary>
    public int SendBudgetMilliseconds { get; }
    /// <summary>相同探针的预热请求数量。</summary>
    public int WarmupRequests => 200;
    /// <summary>允许系统生命周期事件的有限余量，不允许无界保存 Broker 收据。</summary>
    public int MaximumBrokerRecords => Requests + WarmupRequests + 1024;

    /// <summary>既有十秒路线比较档。</summary>
    public static LoggingRequestLoadProfile Comparison { get; } = new(5000, 500, 30000);

    /// <summary>容量档固定三十秒，只接受已经声明资源预算的两个速率。</summary>
    public static LoggingRequestLoadProfile Capacity(int requestsPerSecond)
    {
        if (requestsPerSecond is not (500 or 1000))
            throw new ArgumentOutOfRangeException(nameof(requestsPerSecond), "容量档只允许 500/1000 请求每秒。");
        return new(requestsPerSecond * 30, requestsPerSecond, 50000);
    }
}
