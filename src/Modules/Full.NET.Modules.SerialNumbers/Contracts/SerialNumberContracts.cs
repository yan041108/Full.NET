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
public sealed record PreviewSerialNumberRequest(
    SerialNumberRuleScope Scope,
    string Pattern,
    string? TenantIdentifier,
    long SequenceValue,
    DateTimeOffset AtUtc);

/// <summary>流水号预览结果。</summary>
public sealed record SerialNumberPreviewResponse(string Value);

/// <summary>创建 Host 管理的流水号规则。</summary>
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
public sealed record ChangeSerialNumberRuleStatusRequest(long Version);

/// <summary>流水号规则的稳定响应。</summary>
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
    Task<Result<SerialNumberAllocation>> AllocateAsync(
        string ruleKey,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>Host 流水号规则权限。</summary>
public static class SerialNumberRulePermissions
{
    public const string Read = "serial_numbers.rules.read";

    public const string Create = "serial_numbers.rules.create";

    public const string Update = "serial_numbers.rules.update";

    public const string Enable = "serial_numbers.rules.enable";

    public const string Disable = "serial_numbers.rules.disable";

    public const string Preview = "serial_numbers.rules.preview";
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
}
