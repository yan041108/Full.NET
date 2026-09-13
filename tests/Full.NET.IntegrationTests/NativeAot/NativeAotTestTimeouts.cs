namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>Native AOT 外部进程 E2E 在 CI 上的保守超时预算。</summary>
internal static class NativeAotTestTimeouts
{
    /// <summary>原生宿主首次监听与健康检查。</summary>
    public static TimeSpan ProcessStartup => TimeSpan.FromMinutes(5);

    /// <summary>关键 HTTP 写路径在冷启动后的单次往返。</summary>
    public static TimeSpan HttpClient => TimeSpan.FromMinutes(3);

    /// <summary>一次性 Worker 命令（退役扫描）完成窗口。</summary>
    public static TimeSpan WorkerOneShotCommand => TimeSpan.FromMinutes(5);
}