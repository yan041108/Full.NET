namespace Full.NET.Hosting.Observability;

/// <summary>限时诊断策略作用域种类；禁止扩展为任意动态标签。</summary>
public enum DiagnosticPolicyScopeKind
{
    /// <summary>按日志类别（CategoryName）匹配。</summary>
    Category = 0,
    /// <summary>按 OpenTelemetry 诊断组匹配。</summary>
    DiagnosticGroup = 1,
    /// <summary>按 HTTP 端点路由模板匹配。</summary>
    Endpoint = 2,
    /// <summary>按单次 TraceId 匹配；仅允许极短 TTL。</summary>
    Trace = 3,
    /// <summary>按租户 Id 匹配；用于定位单租户异常。</summary>
    Tenant = 4,
}

/// <summary>诊断策略硬上限；避免无限定向诊断拖垮日志与缓存。</summary>
public static class DiagnosticPolicyLimits
{
    /// <summary>全节点允许同时生效的最大定向规则数量。</summary>
    public const int MaxActiveRules = 32;
    /// <summary>单租户维度可同时生效的最大规则数量。</summary>
    public const int MaxTenantScopedRules = 8;
    /// <summary>Trace 维度可同时生效的最大规则数量。</summary>
    public const int MaxTraceScopedRules = 8;
    /// <summary>定向规则的最短有效期；防止过于频繁的策略切换。</summary>
    public static readonly TimeSpan MinTtl = TimeSpan.FromMinutes(1);
    /// <summary>定向规则的最长有效期；到期必须重新审批或续期。</summary>
    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(2);
    /// <summary>配置项持久化使用的稳定键。</summary>
    public const string ConfigKey = "fullnet.logging.diagnostic-policy";
}

/// <summary>单条受控诊断规则；过期后不得继续放宽采样或容量。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ScopeKind">规则作用域种类；决定 ScopeValue 的语义。</param>
/// <param name="ScopeValue">作用域匹配值；语义随 ScopeKind 变化（类别名、路由模板、TraceId、租户 Id 等）。</param>
/// <param name="SuccessSampleRateOverride">成功请求采样率覆盖值（0-1）；null 表示沿用全局策略。</param>
/// <param name="BestEffortCapacityOverride">尽力而为容量覆盖值；null 表示沿用全局策略。</param>
/// <param name="MaxRequestPayloadBytesOverride">请求体最大字节数覆盖值；null 表示沿用全局策略。</param>
/// <param name="MaxResponsePayloadBytesOverride">响应体最大字节数覆盖值；null 表示沿用全局策略。</param>
/// <param name="ExpiresAtUtc">规则过期时间（UTC）；到期后必须停止放宽，不得自动续期。</param>
public sealed record DiagnosticPolicyRule(
    DiagnosticPolicyScopeKind ScopeKind,
    string ScopeValue,
    double? SuccessSampleRateOverride,
    int? BestEffortCapacityOverride,
    int? MaxRequestPayloadBytesOverride,
    int? MaxResponsePayloadBytesOverride,
    DateTimeOffset ExpiresAtUtc);

/// <summary>持久化到配置项的版本化诊断策略文档。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Version">文档版本号；用于乐观并发，写入时须递增。</param>
/// <param name="PressureState">当前日志压力状态；决定全局默认采样与容量策略。</param>
/// <param name="Rules">当前生效的定向诊断规则集合；数量不得超过 MaxActiveRules。</param>
public sealed record DiagnosticPolicyDocument(
    long Version,
    LoggingPressureState PressureState,
    IReadOnlyList<DiagnosticPolicyRule> Rules);
