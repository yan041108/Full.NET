using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>在 DataApproval 审批通过后禁用流水号规则，并对同一幂等键重放安全。</summary>
internal sealed class SerialRuleDisableApprovalApplier(
    HostSerialRuleService ruleService) : ISerialRuleDisableApprovalApplier
{
    /// <inheritdoc />
    public async Task<Result<SerialNumberRuleResponse>> ApplyApprovedDisableAsync(
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

        ChangeSerialNumberRuleStatusRequest request;
        try
        {
            request = JsonSerializer.Deserialize(
                afterSnapshotJson,
                SerialNumbersJsonSerializerContext.Default.ChangeSerialNumberRuleStatusRequest)!;
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

        var result = await ruleService.ApplyApprovedDisableAsync(
                ruleId,
                actorUserId,
                request.Version,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess ||
            result.Error?.Code != SerialNumberErrorCodes.RuleVersionConflict)
        {
            return result;
        }

        var current = await ruleService.GetAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
        return current.IsSuccess &&
               !current.Value!.IsEnabled &&
               current.Value.Version >= request.Version
            ? current
            : result;
    }
}
