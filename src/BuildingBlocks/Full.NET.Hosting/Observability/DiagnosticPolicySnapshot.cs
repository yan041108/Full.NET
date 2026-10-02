namespace Full.NET.Hosting.Observability;

/// <summary>
/// 运行时不可变诊断策略快照。过期规则在物化时剔除；加载失败必须回退安全默认值。
/// </summary>
/// <param name="Version">策略快照版本号；用于变更检测与缓存失效。</param>
/// <param name="PressureState">当前日志压力状态，决定采样与容量收缩策略。</param>
/// <param name="ActiveRules">当前生效的诊断规则集合；过期规则已在物化时剔除。</param>
/// <param name="LoadedAtUtc">快照加载时间（UTC）；用于判断是否需要刷新。</param>
/// <param name="IsDefault">是否为加载失败后的安全默认快照；默认快照不含自定义规则。</param>
public sealed record DiagnosticPolicySnapshot(
    long Version,
    LoggingPressureState PressureState,
    IReadOnlyList<DiagnosticPolicyRule> ActiveRules,
    DateTimeOffset LoadedAtUtc,
    bool IsDefault)
{
    /// <summary>
    /// 构造加载失败时使用的安全默认快照：Normal 压力、无自定义规则。
    /// </summary>
    /// <param name="utcNow">当前 UTC 时间，作为快照加载时间。</param>
    /// <returns>IsDefault 为 true 的最小安全快照。</returns>
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
    /// <param name="configuredCapacity">配置的 Best Effort 初始容量。</param>
    /// <returns>经规则覆盖与压力状态收缩后的最终容量，最小为 1。</returns>
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

    /// <summary>
    /// 按作用域匹配规则解析成功采样率覆盖值；多条规则命中时取最大值。
    /// </summary>
    /// <param name="diagnosticGroup">诊断分组键；Category 作用域规则忽略此参数。</param>
    /// <param name="endpoint">HTTP 端点路径；Endpoint 作用域规则按此精确匹配。</param>
    /// <param name="traceId">追踪标识；Trace 作用域规则按此精确匹配。</param>
    /// <param name="tenantId">租户标识；Tenant 作用域规则按此精确匹配。</param>
    /// <returns>命中规则的最大采样率覆盖值；无命中时为 <see langword="null"/>。</returns>
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
