namespace Full.NET.Hosting.Observability;

/// <summary>
/// 受治理日志分类常量；禁止由请求参数动态拼造。
/// </summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加到末尾。</remarks>
public static class LogClassification
{
    /// <summary>HTTP 操作日志分类；用于结构化日志中的分类字段，标识一次 HTTP 请求处理。</summary>
    public const string HttpOperation = "http.operation";

    /// <summary>诊断日志分类；用于开发与排障场景的详细日志，生产环境可按需关闭。</summary>
    public const string Diagnostic = "diagnostic";

    /// <summary>安全审计日志分类；用于认证、授权与敏感操作审计，默认全量保留。</summary>
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
/// <remarks>枚举成员数值发布后不可调整；新增成员只能追加到末尾。</remarks>
public enum LoggingCapacityProfile
{
    /// <summary>最小容量档位；适用于开发、单机演示或极低吞吐场景，日志采样与保留策略最激进。</summary>
    S = 0,

    /// <summary>中小容量档位；适用于单实例或低并发生产环境，作为默认容量的下限候选。</summary>
    M = 1,

    /// <summary>标准容量档位；适用于常规生产负载，是大多数部署的推荐默认档位。</summary>
    L = 2,

    /// <summary>大容量档位；适用于高并发或多实例聚合场景，放宽采样与保留阈值。</summary>
    XL = 3,

    /// <summary>超大容量档位；适用于核心链路或高基数日志场景，需配套更大的存储与索引预算。</summary>
    XXL = 4,

    /// <summary>极限容量档位；仅用于经容量评审的超大规模场景，启用最高保留与最低采样策略。</summary>
    Ultra = 5,
}

/// <summary>运行期压力状态；只允许收缩 Best Effort，不得改变 Priority/B0/B1。</summary>
/// <remarks>枚举成员数值发布后不可调整；新增成员只能追加到末尾。</remarks>
public enum LoggingPressureState
{
    /// <summary>正常压力状态；日志管道吞吐与延迟在阈值内，按既定容量档位全量处理。</summary>
    Normal = 0,

    /// <summary>降级压力状态；管道出现积压或延迟升高，仅收缩 Best Effort 类日志，Priority/B0/B1 不受影响。</summary>
    Degraded = 1,

    /// <summary>紧急压力状态；管道濒临过载，在 Degraded 基础上进一步丢弃低优先级日志直至恢复 Normal。</summary>
    Critical = 2,
}
