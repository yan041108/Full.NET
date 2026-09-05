using Full.NET.Abstractions.Results;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Features.ManageRequests;
using Full.NET.Modules.DataApproval.Features.ManageScenarios;

namespace Full.NET.Modules.DataApproval.Features.CrossModulePorts;

/// <summary>向业务模块暴露 DataApproval 场景策略与请求提交能力。</summary>
internal sealed class DataApprovalSubmissionAdapter(
    DataApprovalScenarioService scenarioService,
    DataApprovalRequestService requestService) :
    IDataApprovalScenarioPolicyPort,
    IDataApprovalSubmissionPort
{
    /// <inheritdoc />
    public async Task<bool> BlocksDirectWriteAsync(
        string scenarioKey,
        CancellationToken cancellationToken = default)
    {
        var result = await scenarioService.GetAsync(scenarioKey, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess &&
               result.Value!.IsRegistered &&
               result.Value.IsEnabled &&
               result.Value.WorkflowDefinitionVersionId is not null;
    }

    /// <inheritdoc />
    public async Task<Result<SubmittedDataApprovalRequest>> SubmitAsync(
        Guid actorUserId,
        SubmitDataApprovalRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await requestService.CreateAsync(
                actorUserId,
                new CreateDataApprovalRequestBody(
                    command.ScenarioKey,
                    command.TargetEntityId,
                    command.ProposedChangeJson,
                    command.IdempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result<SubmittedDataApprovalRequest>.Failure(result.Error!);
        }

        var value = result.Value!;
        return Result<SubmittedDataApprovalRequest>.Success(
            new SubmittedDataApprovalRequest(
                value.Id,
                value.ScenarioKey,
                value.StatusKey,
                value.BeforeSnapshotJson,
                value.AfterSnapshotJson,
                value.WorkflowDefinitionVersionId,
                value.Version));
    }
}
