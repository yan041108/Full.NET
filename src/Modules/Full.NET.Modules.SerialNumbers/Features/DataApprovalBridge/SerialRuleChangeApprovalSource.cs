using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>为 DataApproval 提供流水号规则变更前的稳定快照。</summary>
internal sealed class SerialRuleChangeApprovalSource(
    HostSerialRuleService ruleService) : ISerialRuleChangeApprovalSource
{
    /// <inheritdoc />
    public async Task<Result<SerialRuleApprovalSnapshot>> GetSnapshotAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        var result = await ruleService.GetAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(result.Error!);
        }

        var rule = result.Value!;
        var request = new UpdateSerialNumberRuleRequest(
            rule.DisplayName,
            rule.Description,
            rule.Scope,
            rule.ResetInterval,
            rule.Pattern,
            rule.MinimumValue,
            rule.MaximumValue,
            rule.DisplayOrder,
            rule.IsEnabled,
            rule.Version);
        var snapshotJson = JsonSerializer.Serialize(
            request,
            SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest);
        return Result<SerialRuleApprovalSnapshot>.Success(
            new SerialRuleApprovalSnapshot(
                rule.Id,
                rule.RuleKey,
                rule.DisplayName,
                rule.Version,
                snapshotJson));
    }
}
