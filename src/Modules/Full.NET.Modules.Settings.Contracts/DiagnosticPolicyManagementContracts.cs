namespace Full.NET.Modules.Settings.Contracts;

/// <summary>Host 限时诊断策略权限。</summary>
public static class DiagnosticPolicyManagementPermissions
{
    public const string Read = "settings.diagnostic-policy.read";
    public const string Write = "settings.diagnostic-policy.write";
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
