using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, byte> AppliedKeys = new(StringComparer.Ordinal);

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

        if (AppliedKeys.ContainsKey(normalizedKey))
        {
            return await ruleService.GetAsync(ruleId, cancellationToken)
                .ConfigureAwait(false);
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
            result.Error?.Code == SerialNumberErrorCodes.RuleVersionConflict)
        {
            AppliedKeys.TryAdd(normalizedKey, 0);
        }

        return result;
    }
}
