using Full.NET.Abstractions.Results;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>流水号拥有业务快照和应用，审批模块只依赖自己定义的场景端口。</summary>
internal sealed class SerialRuleApprovalScenarioHandler(
    ISerialRuleChangeApprovalSource source,
    ISerialRuleChangeApprovalApplier update,
    ISerialRuleDisableApprovalApplier disable) : IDataApprovalScenarioHandler
{
    public bool Supports(string scenarioKey) => scenarioKey is
        DataApprovalScenarioKeys.SerialRuleHostUpdate or DataApprovalScenarioKeys.SerialRuleHostDisable;

    public async Task<Result<string>> GetSnapshotAsync(string scenarioKey, Guid targetEntityId, CancellationToken cancellationToken)
    {
        var result = await source.GetSnapshotAsync(targetEntityId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result<string>.Success(result.Value!.SnapshotJson) : Result<string>.Failure(result.Error!);
    }

    public async Task<Result<bool>> ApplyAsync(string scenarioKey, Guid targetEntityId, string snapshotJson,
        Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var result = scenarioKey == DataApprovalScenarioKeys.SerialRuleHostUpdate
            ? await update.ApplyApprovedUpdateAsync(targetEntityId, snapshotJson, actorUserId, idempotencyKey, cancellationToken).ConfigureAwait(false)
            : await disable.ApplyApprovedDisableAsync(targetEntityId, snapshotJson, actorUserId, idempotencyKey, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(result.Error!);
    }
}
