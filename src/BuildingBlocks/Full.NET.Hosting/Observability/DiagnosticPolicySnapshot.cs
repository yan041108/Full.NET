namespace Full.NET.Hosting.Observability;

/// <summary>
/// 运行时不可变诊断策略快照。物化及热路径计算均忽略过期规则；加载失败必须回退安全默认值。
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
    public int ResolveBestEffortCapacity(
        int configuredCapacity,
        DateTimeOffset? utcNow = null)
    {
        var capacity = configuredCapacity;
        var now = utcNow ?? DateTimeOffset.UtcNow;
        foreach (var rule in ActiveRules)
        {
            // 容量闸门是实例级共享计数，只接受全局 HTTP 类别/组规则。
            if (rule.ExpiresAtUtc > now
                && rule.ScopeKind is DiagnosticPolicyScopeKind.Category
                    or DiagnosticPolicyScopeKind.DiagnosticGroup
                && string.Equals(
                    rule.ScopeValue,
                    LogClassification.HttpOperation,
                    StringComparison.Ordinal)
                && rule.BestEffortCapacityOverride is int overrideCapacity and > 0)
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
        => ResolveSuccessSamplePolicy(
            diagnosticGroup,
            endpoint,
            traceId,
            tenantId,
            DateTimeOffset.UtcNow).Rate;

    internal (double? Rate, DateTimeOffset? NextExpiryUtc) ResolveSuccessSamplePolicy(
        string? diagnosticGroup,
        string? endpoint,
        string? traceId,
        Guid? tenantId,
        DateTimeOffset utcNow)
    {
        double? rate = null;
        DateTimeOffset? nextExpiryUtc = null;
        foreach (var rule in ActiveRules)
        {
            if (rule.ExpiresAtUtc <= utcNow
                || !Matches(rule, diagnosticGroup, endpoint, traceId, tenantId))
            {
                continue;
            }

            if (rule.SuccessSampleRateOverride is double sample)
            {
                rate = rate is null ? sample : Math.Max(rate.Value, sample);
                nextExpiryUtc = nextExpiryUtc is null
                    ? rule.ExpiresAtUtc
                    : (rule.ExpiresAtUtc < nextExpiryUtc.Value
                        ? rule.ExpiresAtUtc
                        : nextExpiryUtc);
            }
        }

        return (rate, nextExpiryUtc);
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
                string.Equals(rule.ScopeValue, diagnosticGroup, StringComparison.Ordinal),
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
