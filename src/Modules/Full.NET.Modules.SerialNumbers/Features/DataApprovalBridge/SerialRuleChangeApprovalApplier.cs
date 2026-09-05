using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>在 DataApproval 审批通过后应用流水号规则更新，并对同一幂等键重放安全。</summary>
internal sealed class SerialRuleChangeApprovalApplier(
    HostSerialRuleService ruleService) : ISerialRuleChangeApprovalApplier
{
    /// <inheritdoc />
    public async Task<Result<SerialNumberRuleResponse>> ApplyApprovedUpdateAsync(
        Guid ruleId,
        string afterSnapshotJson,
        Guid actorUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = idempotencyKey?.Trim() ?? string.Empty;
        if (normalizedKey.Length is < 1 or > 128)
        {
            return Result<SerialNumberRuleResponse>.Failure(new Error(
                SerialNumberErrorCodes.IdempotencyKeyInvalid,
                "The idempotency key is invalid.",
                ErrorType.Validation));
        }

        UpdateSerialNumberRuleRequest request;
        try
        {
            request = JsonSerializer.Deserialize(
                afterSnapshotJson,
                SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest)!;
        }
        catch (JsonException)
        {
            return Result<SerialNumberRuleResponse>.Failure(new Error(
                SerialNumberErrorCodes.RuleInvalid,
                "The approved snapshot JSON is invalid.",
                ErrorType.Validation));
        }

        if (request is null)
        {
            return Result<SerialNumberRuleResponse>.Failure(new Error(
                SerialNumberErrorCodes.RuleInvalid,
                "The approved snapshot JSON is invalid.",
                ErrorType.Validation));
        }

        var result = await ruleService.UpdateAsync(
                ruleId,
                actorUserId,
                request,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess ||
            result.Error?.Code != SerialNumberErrorCodes.RuleVersionConflict)
        {
            return result;
        }

        // 进程内缓存不能证明重放已应用；冲突后必须读取权威状态并逐字段核对批准快照。
        var current = await ruleService.GetAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
        return current.IsSuccess && MatchesApprovedUpdate(current.Value!, request)
            ? current
            : result;
    }

    /// <summary>判断权威规则是否已经等于批准快照，用于区分幂等重放与真实并发冲突。</summary>
    /// <param name="current">当前权威规则。</param>
    /// <param name="approved">审批通过的更新快照。</param>
    /// <returns>所有可变业务字段一致时返回 <see langword="true"/>。</returns>
    private static bool MatchesApprovedUpdate(
        SerialNumberRuleResponse current,
        UpdateSerialNumberRuleRequest approved) =>
        string.Equals(current.DisplayName, approved.DisplayName, StringComparison.Ordinal) &&
        string.Equals(current.Description, approved.Description, StringComparison.Ordinal) &&
        current.Scope == approved.Scope &&
        current.ResetInterval == approved.ResetInterval &&
        string.Equals(current.Pattern, approved.Pattern, StringComparison.Ordinal) &&
        current.MinimumValue == approved.MinimumValue &&
        current.MaximumValue == approved.MaximumValue &&
        current.DisplayOrder == approved.DisplayOrder &&
        current.IsEnabled == approved.IsEnabled;
}
