namespace Full.NET.Hosting.Observability;

/// <summary>限时诊断策略作用域种类；禁止扩展为任意动态标签。</summary>
public enum DiagnosticPolicyScopeKind
{
    Category = 0,
    DiagnosticGroup = 1,
    Endpoint = 2,
    Trace = 3,
    Tenant = 4,
}

/// <summary>诊断策略硬上限；避免无限定向诊断拖垮日志与缓存。</summary>
public static class DiagnosticPolicyLimits
{
    public const int MaxActiveRules = 32;
    public const int MaxTenantScopedRules = 8;
    public const int MaxTraceScopedRules = 8;
    public static readonly TimeSpan MinTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(2);
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
