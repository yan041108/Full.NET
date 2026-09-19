using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.SerialNumbers.Contracts;

/// <summary>定义流水号计数器的可信作用域。</summary>
public enum SerialNumberRuleScope
{
    /// <summary>所有租户共享一个 Host 全局计数器。</summary>
    Host = 0,

    /// <summary>每个租户拥有独立计数器。</summary>
    Tenant = 1,
}

/// <summary>定义流水号达到边界后使用的 UTC 重置周期。</summary>
public enum SerialNumberResetInterval
{
    /// <summary>永不重置。</summary>
    Never = 0,

    /// <summary>按 UTC 日期重置。</summary>
    Day = 1,

    /// <summary>按 UTC 月份重置。</summary>
    Month = 2,

    /// <summary>按 UTC 年份重置。</summary>
    Year = 3,
}

/// <summary>Host 管理端请求的纯函数流水号预览。</summary>
/// <param name="Scope">取号作用域，决定使用 Host 全局计数器还是租户计数器。</param>
/// <param name="Pattern">渲染模板；占位符由服务端解析。</param>
/// <param name="TenantIdentifier">租户标识；Scope 为 Tenant 时必填，Host 时可空。</param>
/// <param name="SequenceValue">用于渲染的序列值，即假设下一次分配的序号。</param>
/// <param name="AtUtc">预览基准时间（UTC），用于计算 ResetBucket。</param>
/// <param name="ResetInterval">UTC 重置周期，决定 ResetBucket 的取值。</param>
public sealed record PreviewSerialNumberRequest(
    SerialNumberRuleScope Scope,
    string Pattern,
    string? TenantIdentifier,
    long SequenceValue,
    DateTimeOffset AtUtc,
    SerialNumberResetInterval ResetInterval = SerialNumberResetInterval.Never);

/// <summary>流水号预览结果；不读取或修改计数器状态。</summary>
/// <param name="Value">按 Pattern 渲染后的预览值。</param>
/// <param name="ResetBucket">当前 UTC 时间对应的重置桶。</param>
/// <param name="SequenceValue">用于渲染的序列值，即下一次将分配的序号。</param>
public sealed record SerialNumberPreviewResponse(
    string Value,
    string ResetBucket,
    long SequenceValue);

/// <summary>创建 Host 管理的流水号规则。</summary>
/// <param name="RuleKey">规则稳定键；创建后不可改名，跨模块引用基于该键。</param>
/// <param name="DisplayName">规则展示名称。</param>
/// <param name="Description">规则说明，可空。</param>
/// <param name="Scope">取号作用域，决定计数器隔离粒度。</param>
/// <param name="ResetInterval">UTC 重置周期，决定计数器重置时机。</param>
/// <param name="Pattern">渲染模板；占位符由服务端解析。</param>
/// <param name="MinimumValue">序号最小边界（含），到达边界后分配拒绝。</param>
/// <param name="MaximumValue">序号最大边界（含），到达边界后分配拒绝。</param>
/// <param name="DisplayOrder">同列表展示顺序，升序。</param>
/// <param name="IsEnabled">是否启用；禁用后拒绝新的取号请求。</param>
public sealed record CreateSerialNumberRuleRequest(
    string RuleKey,
    string DisplayName,
    string? Description,
    SerialNumberRuleScope Scope,
    SerialNumberResetInterval ResetInterval,
    string Pattern,
    long MinimumValue,
    long MaximumValue,
    int DisplayOrder,
    bool IsEnabled);

/// <summary>更新流水号规则并使用乐观并发版本。</summary>
/// <param name="DisplayName">规则展示名称。</param>
/// <param name="Description">规则说明，可空。</param>
/// <param name="Scope">取号作用域；已有分配记录时变更受 RuleSemanticsLocked 限制。</param>
/// <param name="ResetInterval">UTC 重置周期。</param>
/// <param name="Pattern">渲染模板；变更可能影响已分配序列的可读性。</param>
/// <param name="MinimumValue">序号最小边界（含）。</param>
/// <param name="MaximumValue">序号最大边界（含）。</param>
/// <param name="DisplayOrder">展示顺序。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
public sealed record UpdateSerialNumberRuleRequest(
    string DisplayName,
    string? Description,
    SerialNumberRuleScope Scope,
    SerialNumberResetInterval ResetInterval,
    string Pattern,
    long MinimumValue,
    long MaximumValue,
    int DisplayOrder,
    bool IsEnabled,
    long Version);

/// <summary>启用或禁用规则时携带的乐观并发版本。</summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
public sealed record ChangeSerialNumberRuleStatusRequest(long Version);

/// <summary>流水号规则的稳定响应。</summary>
/// <param name="Id">规则稳定标识。</param>
/// <param name="RuleKey">规则稳定键，跨模块引用基于该键。</param>
/// <param name="DisplayName">规则展示名称。</param>
/// <param name="Description">规则说明，可空。</param>
/// <param name="Scope">取号作用域。</param>
/// <param name="ResetInterval">UTC 重置周期。</param>
/// <param name="Pattern">渲染模板。</param>
/// <param name="MinimumValue">序号最小边界（含）。</param>
/// <param name="MaximumValue">序号最大边界（含）。</param>
/// <param name="DisplayOrder">展示顺序。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="CreatedByUserId">创建者用户标识。</param>
/// <param name="UpdatedAtUtc">最后更新时间（UTC），可空。</param>
/// <param name="UpdatedByUserId">最后更新者用户标识，可空。</param>
/// <param name="Version">乐观并发版本号，用于后续更新请求的 CAS 守卫。</param>
public sealed record SerialNumberRuleResponse(
    Guid Id,
    string RuleKey,
    string DisplayName,
    string? Description,
    SerialNumberRuleScope Scope,
    SerialNumberResetInterval ResetInterval,
    string Pattern,
    long MinimumValue,
    long MaximumValue,
    int DisplayOrder,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version);

/// <summary>一次成功且可按幂等键重放的流水号分配。</summary>
/// <param name="RuleKey">分配所基于的规则稳定键。</param>
/// <param name="SerialNumber">按 Pattern 渲染后的最终流水号字符串。</param>
/// <param name="SequenceValue">本次分配消耗的原始序列值。</param>
/// <param name="ResetBucket">分配时刻对应的 UTC 重置桶，用于唯一性边界。</param>
/// <param name="AllocatedAtUtc">分配发生时间（UTC）。</param>
public sealed record SerialNumberAllocation(
    string RuleKey,
    string SerialNumber,
    long SequenceValue,
    string ResetBucket,
    DateTimeOffset AllocatedAtUtc);

/// <summary>
/// 业务模块使用的强类型取号端口；调用者必须提供稳定幂等键。
/// </summary>
public interface ISerialNumberAllocator
{
    /// <summary>
    /// 按规则键分配一次流水号；幂等键保证相同调用重复执行不产生重复分配。
    /// </summary>
    /// <remarks>
    /// 至少一次交付：调用方重试是安全的；服务端以 (ruleKey, idempotencyKey) 做幂等去重。
    /// 规则禁用、序号耗尽或租户上下文不匹配时返回失败 Result，不抛异常。
    /// </remarks>
    /// <param name="ruleKey">规则稳定键，决定计数器与 Pattern。</param>
    /// <param name="idempotencyKey">调用方提供的稳定幂等键，重复请求返回首次分配结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时携带 <see cref="SerialNumberAllocation"/>；失败时携带稳定错误码。</returns>
    Task<Result<SerialNumberAllocation>> AllocateAsync(
        string ruleKey,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>Host 流水号规则权限。</summary>
public static class SerialNumberRulePermissions
{
    /// <summary>读取流水号规则列表与详情。</summary>
    public const string Read = "serial_numbers.rules.read";

    /// <summary>创建新的流水号规则。</summary>
    public const string Create = "serial_numbers.rules.create";

    /// <summary>修改现有流水号规则的 Pattern、边界与显示顺序。</summary>
    public const string Update = "serial_numbers.rules.update";

    /// <summary>启用流水号规则，允许后续分配取号。</summary>
    public const string Enable = "serial_numbers.rules.enable";

    /// <summary>停用流水号规则，拒绝新的取号请求。</summary>
    public const string Disable = "serial_numbers.rules.disable";

    /// <summary>使用指定模式与序列预览流水号输出结果。</summary>
    public const string Preview = "serial_numbers.rules.preview";

    /// <summary>提交流水号规则更新审批请求。</summary>
    public const string SubmitUpdateApproval = "serial_numbers.rules.submit_update_approval";

    /// <summary>提交流水号规则禁用审批请求。</summary>
    public const string SubmitDisableApproval = "serial_numbers.rules.submit_disable_approval";
}

/// <summary>SerialNumbers 模块稳定错误码。</summary>
public static class SerialNumberErrorCodes
{
    /// <summary>流水号 Pattern 不满足受限语法或长度边界。</summary>
    public const string PatternInvalid = "serial_numbers.pattern.invalid";

    /// <summary>规则请求不满足稳定边界。</summary>
    public const string RuleInvalid = "serial_numbers.rule.invalid";

    /// <summary>规则键已存在。</summary>
    public const string RuleKeyExists = "serial_numbers.rule.key_exists";

    /// <summary>规则不存在。</summary>
    public const string RuleNotFound = "serial_numbers.rule.not_found";

    /// <summary>规则版本已被并发更新。</summary>
    public const string RuleVersionConflict =
        "serial_numbers.rule.version_conflict";

    /// <summary>规则已有分配记录，影响编号唯一性的语义字段不可再变更。</summary>
    public const string RuleSemanticsLocked =
        "serial_numbers.rule.semantics_locked";

    /// <summary>规则已禁用。</summary>
    public const string RuleDisabled = "serial_numbers.rule.disabled";

    /// <summary>可信租户上下文与规则作用域不匹配。</summary>
    public const string TenantContextRequired =
        "serial_numbers.tenant_context.required";

    /// <summary>幂等键不满足长度或字符边界。</summary>
    public const string IdempotencyKeyInvalid =
        "serial_numbers.idempotency_key.invalid";

    /// <summary>当前 reset bucket 的序列已耗尽。</summary>
    public const string SequenceExhausted =
        "serial_numbers.sequence.exhausted";

    /// <summary>场景未启用审批时不能提交更新审批。</summary>
    public const string UpdateApprovalNotRequired =
        "serial_numbers.rule.update_approval_not_required";

    /// <summary>规则更新必须经 DataApproval 审批，不能直接写入。</summary>
    public const string UpdateRequiresApproval =
        "serial_numbers.rule.update_requires_approval";

    /// <summary>场景未启用审批时不能提交禁用审批。</summary>
    public const string DisableApprovalNotRequired =
        "serial_numbers.rule.disable_approval_not_required";

    /// <summary>规则禁用必须经 DataApproval 审批，不能直接停用。</summary>
    public const string DisableRequiresApproval =
        "serial_numbers.rule.disable_requires_approval";

    /// <summary>规则已禁用，不能重复提交禁用审批。</summary>
    public const string RuleAlreadyDisabled =
        "serial_numbers.rule.already_disabled";
}

/// <summary>流水号规则变更审批所需的稳定快照摘要。</summary>
/// <param name="RuleId">目标规则标识。</param>
/// <param name="RuleKey">规则稳定键。</param>
/// <param name="DisplayName">规则显示名称。</param>
/// <param name="Version">提交审批时的乐观并发版本。</param>
/// <param name="SnapshotJson">可反序列化为 <see cref="UpdateSerialNumberRuleRequest"/> 的 JSON 文本。</param>
public sealed record SerialRuleApprovalSnapshot(
    Guid RuleId,
    string RuleKey,
    string DisplayName,
    long Version,
    string SnapshotJson);

/// <summary>读取流水号规则变更审批前的稳定快照。</summary>
public interface ISerialRuleChangeApprovalSource
{
    /// <summary>读取指定规则当前可审批变更的快照。</summary>
    /// <param name="ruleId">目标规则标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<SerialRuleApprovalSnapshot>> GetSnapshotAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default);
}

/// <summary>在审批通过后把提议变更应用到流水号规则。</summary>
public interface ISerialRuleChangeApprovalApplier
{
    /// <summary>按已批准快照更新目标规则，并保证幂等重放安全。</summary>
    /// <param name="ruleId">目标规则标识。</param>
    /// <param name="afterSnapshotJson">审批通过的变更 JSON，须可反序列化为 <see cref="UpdateSerialNumberRuleRequest"/>。</param>
    /// <param name="actorUserId">执行应用的用户标识。</param>
    /// <param name="idempotencyKey">稳定幂等键，用于重放保护。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<SerialNumberRuleResponse>> ApplyApprovedUpdateAsync(
        Guid ruleId,
        string afterSnapshotJson,
        Guid actorUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>在审批通过后把提议禁用应用到流水号规则。</summary>
public interface ISerialRuleDisableApprovalApplier
{
    /// <summary>按已批准快照禁用目标规则，并保证幂等重放安全。</summary>
    /// <param name="ruleId">目标规则标识。</param>
    /// <param name="afterSnapshotJson">审批通过的变更 JSON，须可反序列化为 <see cref="ChangeSerialNumberRuleStatusRequest"/>。</param>
    /// <param name="actorUserId">执行应用的用户标识。</param>
    /// <param name="idempotencyKey">稳定幂等键，用于重放保护。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<SerialNumberRuleResponse>> ApplyApprovedDisableAsync(
        Guid ruleId,
        string afterSnapshotJson,
        Guid actorUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>流水号规则更新审批的单个字段差异。</summary>
/// <param name="FieldKey">稳定字段键，供前端 i18n 映射。</param>
/// <param name="BeforeValue">变更前值文本。</param>
/// <param name="AfterValue">变更后值文本。</param>
/// <param name="Changed">是否发生变更。</param>
public sealed record SerialRuleFieldChange(
    string FieldKey,
    string? BeforeValue,
    string? AfterValue,
    bool Changed);

/// <summary>提交流水号规则更新审批的请求体。</summary>
/// <param name="Update">强类型提议更新。</param>
/// <param name="IdempotencyKey">调用方幂等键。</param>
public sealed record SubmitSerialRuleUpdateApprovalRequest(
    UpdateSerialNumberRuleRequest Update,
    string IdempotencyKey);

/// <summary>流水号规则更新审批差异预览响应。</summary>
/// <param name="RuleId">目标规则标识。</param>
/// <param name="RuleKey">规则稳定键。</param>
/// <param name="DisplayName">规则显示名称。</param>
/// <param name="Changes">字段级差异列表。</param>
/// <param name="BeforeSnapshotJson">变更前快照 JSON。</param>
/// <param name="AfterSnapshotJson">提议变更 JSON。</param>
public sealed record SerialRuleUpdateApprovalPreviewResponse(
    Guid RuleId,
    string RuleKey,
    string DisplayName,
    IReadOnlyList<SerialRuleFieldChange> Changes,
    string BeforeSnapshotJson,
    string AfterSnapshotJson);

/// <summary>流水号规则更新审批提交结果。</summary>
/// <param name="RequestId">DataApproval 请求标识。</param>
/// <param name="StatusKey">审批请求状态键。</param>
/// <param name="Changes">字段级差异列表。</param>
/// <param name="BeforeSnapshotJson">变更前快照 JSON。</param>
/// <param name="AfterSnapshotJson">提议变更 JSON。</param>
/// <param name="WorkflowDefinitionVersionId">固定的工作流定义版本标识。</param>
/// <param name="RequestVersion">审批请求乐观并发版本。</param>
public sealed record SerialRuleUpdateApprovalSubmissionResponse(
    Guid RequestId,
    string StatusKey,
    IReadOnlyList<SerialRuleFieldChange> Changes,
    string? BeforeSnapshotJson,
    string AfterSnapshotJson,
    Guid WorkflowDefinitionVersionId,
    long RequestVersion);

/// <summary>提交流水号规则禁用审批的请求体。</summary>
/// <param name="StatusChange">强类型提议禁用（仅版本号）。</param>
/// <param name="IdempotencyKey">调用方幂等键。</param>
public sealed record SubmitSerialRuleDisableApprovalRequest(
    ChangeSerialNumberRuleStatusRequest StatusChange,
    string IdempotencyKey);

/// <summary>流水号规则禁用审批预览响应。</summary>
/// <param name="RuleId">目标规则标识。</param>
/// <param name="RuleKey">规则稳定键。</param>
/// <param name="DisplayName">规则显示名称。</param>
/// <param name="Version">提交审批时的乐观并发版本。</param>
/// <param name="BeforeSnapshotJson">变更前快照 JSON。</param>
/// <param name="AfterSnapshotJson">提议禁用 JSON。</param>
public sealed record SerialRuleDisableApprovalPreviewResponse(
    Guid RuleId,
    string RuleKey,
    string DisplayName,
    long Version,
    string BeforeSnapshotJson,
    string AfterSnapshotJson);

/// <summary>流水号规则禁用审批提交结果。</summary>
/// <param name="RequestId">DataApproval 请求标识。</param>
/// <param name="StatusKey">审批请求状态键。</param>
/// <param name="BeforeSnapshotJson">变更前快照 JSON。</param>
/// <param name="AfterSnapshotJson">提议禁用 JSON。</param>
/// <param name="WorkflowDefinitionVersionId">固定的工作流定义版本标识。</param>
/// <param name="RequestVersion">审批请求乐观并发版本。</param>
public sealed record SerialRuleDisableApprovalSubmissionResponse(
    Guid RequestId,
    string StatusKey,
    string? BeforeSnapshotJson,
    string AfterSnapshotJson,
    Guid WorkflowDefinitionVersionId,
    long RequestVersion);
