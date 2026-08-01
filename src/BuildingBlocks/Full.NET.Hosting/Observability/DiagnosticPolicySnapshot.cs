namespace Full.NET.Hosting.Observability;

/// <summary>
/// 运行时不可变诊断策略快照。过期规则在物化时剔除；加载失败必须回退安全默认值。
/// </summary>
public sealed record DiagnosticPolicySnapshot(
    long Version,
    LoggingPressureState PressureState,
    IReadOnlyList<DiagnosticPolicyRule> ActiveRules,
    DateTimeOffset LoadedAtUtc,
    bool IsDefault)
{
    public static DiagnosticPolicySnapshot CreateDefault(DateTimeOffset utcNow) =>
        new(
            Version: 0,
            LoggingPressureState.Normal,
            Array.Empty<DiagnosticPolicyRule>(),
            utcNow,
            IsDefault: true);

    /// <summary>
    /// 在 Degraded/Critical 下只收缩 Best Effort 容量；Priority/B0/B1 不得被本路径削弱。
    /// </summary>
    public int ResolveBestEffortCapacity(int configuredCapacity)
    {
        var capacity = configuredCapacity;
        foreach (var rule in ActiveRules)
        {
            if (rule.BestEffortCapacityOverride is int overrideCapacity and > 0)
            {
                capacity = Math.Min(capacity, overrideCapacity);
            }
        }

        return PressureState switch
        {
            LoggingPressureState.Degraded => Math.Max(1, capacity / 2),
            LoggingPressureState.Critical => Math.Max(1, capacity / 4),
            _ => capacity,
        };
    }

    public double? ResolveSuccessSampleRateOverride(
        string? diagnosticGroup,
        string? endpoint,
        string? traceId,
        Guid? tenantId)
    {
        double? rate = null;
        foreach (var rule in ActiveRules)
        {
            if (!Matches(rule, diagnosticGroup, endpoint, traceId, tenantId))
            {
                continue;
            }

            if (rule.SuccessSampleRateOverride is double sample)
            {
                rate = rate is null ? sample : Math.Max(rate.Value, sample);
            }
        }

        return rate;
    }

    private static bool Matches(
        DiagnosticPolicyRule rule,
        string? diagnosticGroup,
        string? endpoint,
        string? traceId,
        Guid? tenantId) =>
        rule.ScopeKind switch
        {
            DiagnosticPolicyScopeKind.Category =>
                string.Equals(rule.ScopeValue, LogClassification.Diagnostic, StringComparison.Ordinal)
                || string.Equals(rule.ScopeValue, LogClassification.HttpOperation, StringComparison.Ordinal),
            DiagnosticPolicyScopeKind.DiagnosticGroup =>
                string.Equals(rule.ScopeValue, diagnosticGroup, StringComparison.Ordinal),
            DiagnosticPolicyScopeKind.Endpoint =>
                string.Equals(rule.ScopeValue, endpoint, StringComparison.Ordinal),
            DiagnosticPolicyScopeKind.Trace =>
                string.Equals(rule.ScopeValue, traceId, StringComparison.Ordinal),
            DiagnosticPolicyScopeKind.Tenant =>
                tenantId is Guid id
                && Guid.TryParse(rule.ScopeValue, out var scoped)
                && scoped == id,
            _ => false,
        };
}
