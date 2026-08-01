namespace Full.NET.Hosting.Observability;

/// <summary>
/// 受治理日志分类常量；禁止由请求参数动态拼造。
/// </summary>
public static class LogClassification
{
    public const string HttpOperation = "http.operation";

    public const string Diagnostic = "diagnostic";

    public const string Security = "security";
}

/// <summary>普通 HTTP Operation Log 捕获模式。</summary>
public enum HttpOperationCaptureMode
{
    /// <summary>不生成普通 HttpOperationCompleted 事件。</summary>
    Disabled = 0,

    /// <summary>只记录摘要字段（生产默认候选）。</summary>
    Summary = 1,

    /// <summary>摘要加按 Route 白名单投影的脱敏载荷。</summary>
    SanitizedPayload = 2,
}

/// <summary>部署时选定的日志容量档位；禁止按瞬时并发自动切档。</summary>
public enum LoggingCapacityProfile
{
    S = 0,
    M = 1,
    L = 2,
    XL = 3,
    XXL = 4,
    Ultra = 5,
}

/// <summary>运行期压力状态；只允许收缩 Best Effort，不得改变 Priority/B0/B1。</summary>
public enum LoggingPressureState
{
    Normal = 0,
    Degraded = 1,
    Critical = 2,
}
