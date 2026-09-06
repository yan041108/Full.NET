using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>编排流水号规则禁用审批的校验与 DataApproval 提交。</summary>
internal sealed class SerialRuleDisableApprovalService(
    HostSerialRuleService ruleService,
    ISerialRuleChangeApprovalSource approvalSource,
    IDataApprovalSubmissionPort submissionPort,
    IDataApprovalScenarioPolicyPort scenarioPolicyPort)
{
    /// <summary>预览规则禁用审批，不创建审批请求。</summary>
    /// <param name="ruleId">目标规则标识。</param>
    /// <param name="request">提议禁用请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<SerialRuleDisableApprovalPreviewResponse>> PreviewAsync(
        Guid ruleId,
        ChangeSerialNumberRuleStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateProposedDisableAsync(ruleId, request, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<SerialRuleDisableApprovalPreviewResponse>.Failure(validation.Error!);
        }

        var snapshot = validation.Value!;
        return Result<SerialRuleDisableApprovalPreviewResponse>.Success(
            new SerialRuleDisableApprovalPreviewResponse(
                ruleId,
                snapshot.RuleKey,
                snapshot.DisplayName,
                request.Version,
                snapshot.SnapshotJson,
                SerializeStatusChange(request)));
    }

    /// <summary>提交流水号规则禁用审批请求。</summary>
    /// <param name="ruleId">目标规则标识。</param>
    /// <param name="actorUserId">提交人用户标识。</param>
    /// <param name="request">提交审批请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<SerialRuleDisableApprovalSubmissionResponse>> SubmitAsync(
        Guid ruleId,
        Guid actorUserId,
        SubmitSerialRuleDisableApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(ruleId, request.StatusChange, cancellationToken)
            .ConfigureAwait(false);
        if (!preview.IsSuccess)
        {
            return Result<SerialRuleDisableApprovalSubmissionResponse>.Failure(preview.Error!);
        }

        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 128)
        {
            return Result<SerialRuleDisableApprovalSubmissionResponse>.Failure(new Error(
                SerialNumberErrorCodes.IdempotencyKeyInvalid,
                "The idempotency key is invalid.",
                ErrorType.Validation));
        }

        var submitted = await submissionPort.SubmitAsync(
                actorUserId,
                new SubmitDataApprovalRequestCommand(
                    DataApprovalScenarioKeys.SerialRuleHostDisable,
                    ruleId,
                    preview.Value!.AfterSnapshotJson,
                    idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);
        if (!submitted.IsSuccess)
        {
            return Result<SerialRuleDisableApprovalSubmissionResponse>.Failure(submitted.Error!);
        }

        var value = submitted.Value!;
        return Result<SerialRuleDisableApprovalSubmissionResponse>.Success(
            new SerialRuleDisableApprovalSubmissionResponse(
                value.RequestId,
                value.StatusKey,
                value.BeforeSnapshotJson,
                value.AfterSnapshotJson,
                value.WorkflowDefinitionVersionId,
                value.Version));
    }

    /// <summary>判断当前作用域是否要求流水号规则禁用走审批。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<bool> RequiresApprovalAsync(CancellationToken cancellationToken = default) =>
        scenarioPolicyPort.BlocksDirectWriteAsync(
            DataApprovalScenarioKeys.SerialRuleHostDisable,
            cancellationToken);

    private async Task<Result<SerialRuleApprovalSnapshot>> ValidateProposedDisableAsync(
        Guid ruleId,
        ChangeSerialNumberRuleStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Version < 1)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(new Error(
                SerialNumberErrorCodes.RuleInvalid,
                "The serial number rule request is invalid.",
                ErrorType.Validation));
        }

        var requiresApproval = await RequiresApprovalAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!requiresApproval)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(new Error(
                SerialNumberErrorCodes.DisableApprovalNotRequired,
                "The serial number rule disable approval scenario is not enabled.",
                ErrorType.Validation));
        }

        var current = await ruleService.GetAsync(ruleId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(current.Error!);
        }

        if (!current.Value!.IsEnabled)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(new Error(
                SerialNumberErrorCodes.RuleAlreadyDisabled,
                "The serial number rule is already disabled.",
                ErrorType.Validation));
        }

        if (current.Value.Version != request.Version)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(new Error(
                SerialNumberErrorCodes.RuleVersionConflict,
                "The serial number rule version is out of date.",
                ErrorType.Conflict));
        }

        return await approvalSource.GetSnapshotAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string SerializeStatusChange(ChangeSerialNumberRuleStatusRequest request) =>
        JsonSerializer.Serialize(
            request,
            SerialNumbersJsonSerializerContext.Default.ChangeSerialNumberRuleStatusRequest);
}
