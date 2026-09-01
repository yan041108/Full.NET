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
public sealed record DiagnosticPolicyRule(
    DiagnosticPolicyScopeKind ScopeKind,
    string ScopeValue,
    double? SuccessSampleRateOverride,
    int? BestEffortCapacityOverride,
    int? MaxRequestPayloadBytesOverride,
    int? MaxResponsePayloadBytesOverride,
    DateTimeOffset ExpiresAtUtc);

/// <summary>持久化到配置项的版本化诊断策略文档。</summary>
public sealed record DiagnosticPolicyDocument(
    long Version,
    LoggingPressureState PressureState,
    IReadOnlyList<DiagnosticPolicyRule> Rules);
