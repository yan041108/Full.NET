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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 PressureState、ConfigEntryVersion 等稳定键发布后不可改名或删除。</remarks>
/// <param name="Version">策略版本号；用于乐观并发校验。</param>
/// <param name="PressureState">压力状态稳定键；调用方据此选择降级档位。</param>
/// <param name="IsDefault">是否处于生产安全默认档；true 表示未被外部覆盖。</param>
/// <param name="LoadedAtUtc">策略加载时间（UTC）；用于判断缓存新鲜度。</param>
/// <param name="ActiveRules">当前生效的规则集合；按 Scope 排序后逐条投影。</param>
/// <param name="ConfigEntryVersion">底层配置条目版本；与写入端做幂等与并发对账。</param>
public sealed record DiagnosticPolicyResponse(
    long Version,
    string PressureState,
    bool IsDefault,
    DateTimeOffset LoadedAtUtc,
    IReadOnlyList<DiagnosticPolicyRuleResponse> ActiveRules,
    int ConfigEntryVersion);

/// <summary>限时诊断策略单条生效规则的响应投影。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ScopeKind 取值集合发布后不可改名或删除。</remarks>
/// <param name="ScopeKind">作用域种类稳定键；决定 ScopeValue 解释方式。</param>
/// <param name="ScopeValue">作用域值；按 ScopeKind 解析为目标实体或维度。</param>
/// <param name="SuccessSampleRateOverride">成功采样率覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="BestEffortCapacityOverride">兜底容量覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="MaxRequestPayloadBytesOverride">请求体字节上限覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="MaxResponsePayloadBytesOverride">响应体字节上限覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="ExpiresAtUtc">规则失效时间（UTC）；过期后不再生效。</param>
public sealed record DiagnosticPolicyRuleResponse(
    string ScopeKind,
    string ScopeValue,
    double? SuccessSampleRateOverride,
    int? BestEffortCapacityOverride,
    int? MaxRequestPayloadBytesOverride,
    int? MaxResponsePayloadBytesOverride,
    DateTimeOffset ExpiresAtUtc);

/// <summary>更新诊断策略请求；禁止自由填写 Sink/索引/Metrics 标签。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ConfigEntryVersion 必须与当前持久化版本一致，否则按并发冲突拒绝。</remarks>
/// <param name="PressureState">目标压力状态稳定键；仅允许使用预定义档位。</param>
/// <param name="Rules">覆盖规则集合；空列表表示仅切换档位并清空覆盖。</param>
/// <param name="ConfigEntryVersion">调用方读取时的配置条目版本；用于乐观并发校验。</param>
public sealed record UpdateDiagnosticPolicyRequest(
    string PressureState,
    IReadOnlyList<DiagnosticPolicyRuleRequest> Rules,
    int ConfigEntryVersion);

/// <summary>限时诊断策略单条规则的写入请求；覆盖项为空表示沿用生产安全默认。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 覆盖项 null 与显式默认值语义不同，禁止混淆。</remarks>
/// <param name="ScopeKind">作用域种类稳定键；决定 ScopeValue 解释方式。</param>
/// <param name="ScopeValue">作用域值；按 ScopeKind 解析为目标实体或维度。</param>
/// <param name="SuccessSampleRateOverride">成功采样率覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="BestEffortCapacityOverride">兜底容量覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="MaxRequestPayloadBytesOverride">请求体字节上限覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="MaxResponsePayloadBytesOverride">响应体字节上限覆盖值；null 表示沿用生产安全默认。</param>
/// <param name="ExpiresAtUtc">规则失效时间（UTC）；过期后不再生效。</param>
public sealed record DiagnosticPolicyRuleRequest(
    string ScopeKind,
    string ScopeValue,
    double? SuccessSampleRateOverride,
    int? BestEffortCapacityOverride,
    int? MaxRequestPayloadBytesOverride,
    int? MaxResponsePayloadBytesOverride,
    DateTimeOffset ExpiresAtUtc);

/// <summary>恢复生产安全默认诊断策略。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ConfigEntryVersion">调用方读取时的配置条目版本；用于乐观并发校验，避免覆盖他人并发更新。</param>
public sealed record RestoreDiagnosticPolicyRequest(int ConfigEntryVersion);
