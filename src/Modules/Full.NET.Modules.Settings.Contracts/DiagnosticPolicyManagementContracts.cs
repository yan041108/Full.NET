namespace Full.NET.Modules.Settings.Contracts;

/// <summary>Host 限时诊断策略权限。</summary>
public static class DiagnosticPolicyManagementPermissions
{
    /// <summary>查询限时诊断策略。</summary>
    public const string Read = "settings.diagnostic_policy.read";

    /// <summary>更新限时诊断策略。</summary>
    public const string Update = "settings.diagnostic_policy.update";

    /// <summary>恢复限时诊断策略生产安全默认。</summary>
    public const string Restore = "settings.diagnostic_policy.restore";

    /// <summary>迁移 070 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "settings.diagnostic_policy.write";
}

/// <summary>诊断策略 API 响应。</summary>
public sealed record DiagnosticPolicyResponse(
    long Version,
    string PressureState,
    bool IsDefault,
    DateTimeOffset LoadedAtUtc,
    IReadOnlyList<DiagnosticPolicyRuleResponse> ActiveRules,
    int ConfigEntryVersion);

public sealed record DiagnosticPolicyRuleResponse(
    string ScopeKind,
    string ScopeValue,
    double? SuccessSampleRateOverride,
    int? BestEffortCapacityOverride,
    int? MaxRequestPayloadBytesOverride,
    int? MaxResponsePayloadBytesOverride,
    DateTimeOffset ExpiresAtUtc);

/// <summary>更新诊断策略请求；禁止自由填写 Sink/索引/Metrics 标签。</summary>
public sealed record UpdateDiagnosticPolicyRequest(
    string PressureState,
    IReadOnlyList<DiagnosticPolicyRuleRequest> Rules,
    int ConfigEntryVersion);

public sealed record DiagnosticPolicyRuleRequest(
    string ScopeKind,
    string ScopeValue,
    double? SuccessSampleRateOverride,
    int? BestEffortCapacityOverride,
    int? MaxRequestPayloadBytesOverride,
    int? MaxResponsePayloadBytesOverride,
    DateTimeOffset ExpiresAtUtc);

/// <summary>恢复生产安全默认诊断策略。</summary>
public sealed record RestoreDiagnosticPolicyRequest(int ConfigEntryVersion);
