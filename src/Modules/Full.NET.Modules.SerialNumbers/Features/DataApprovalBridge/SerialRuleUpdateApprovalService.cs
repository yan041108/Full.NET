using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>编排流水号规则更新审批的字段差异计算与 DataApproval 提交。</summary>
internal sealed class SerialRuleUpdateApprovalService(
    HostSerialRuleService ruleService,
    ISerialRuleChangeApprovalSource approvalSource,
    IDataApprovalSubmissionPort submissionPort,
    IDataApprovalScenarioPolicyPort scenarioPolicyPort)
{
    /// <summary>预览规则更新审批的字段差异，不创建审批请求。</summary>
    /// <param name="ruleId">目标规则标识。</param>
    /// <param name="request">提议更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<SerialRuleUpdateApprovalPreviewResponse>> PreviewAsync(
        Guid ruleId,
        UpdateSerialNumberRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateProposedUpdateAsync(ruleId, request, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return Result<SerialRuleUpdateApprovalPreviewResponse>.Failure(validation.Error!);
        }

        var snapshot = validation.Value!;
        var changes = SerialRuleFieldDiffBuilder.Build(snapshot.SnapshotJson, request);
        if (!SerialRuleFieldDiffBuilder.HasChanges(changes))
        {
            return Result<SerialRuleUpdateApprovalPreviewResponse>.Failure(new Error(
                SerialNumberErrorCodes.RuleInvalid,
                "The proposed update does not change any fields.",
                ErrorType.Validation));
        }

        return Result<SerialRuleUpdateApprovalPreviewResponse>.Success(
            new SerialRuleUpdateApprovalPreviewResponse(
                ruleId,
                snapshot.RuleKey,
                snapshot.DisplayName,
                changes,
                snapshot.SnapshotJson,
                SerializeUpdate(request)));
    }

    /// <summary>提交流水号规则更新审批请求并返回字段差异。</summary>
    /// <param name="ruleId">目标规则标识。</param>
    /// <param name="actorUserId">提交人用户标识。</param>
    /// <param name="request">提交审批请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<SerialRuleUpdateApprovalSubmissionResponse>> SubmitAsync(
        Guid ruleId,
        Guid actorUserId,
        SubmitSerialRuleUpdateApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(ruleId, request.Update, cancellationToken)
            .ConfigureAwait(false);
        if (!preview.IsSuccess)
        {
            return Result<SerialRuleUpdateApprovalSubmissionResponse>.Failure(preview.Error!);
        }

        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 128)
        {
            return Result<SerialRuleUpdateApprovalSubmissionResponse>.Failure(new Error(
                SerialNumberErrorCodes.IdempotencyKeyInvalid,
                "The idempotency key is invalid.",
                ErrorType.Validation));
        }

        var submitted = await submissionPort.SubmitAsync(
                actorUserId,
                new SubmitDataApprovalRequestCommand(
                    DataApprovalScenarioKeys.SerialRuleHostUpdate,
                    ruleId,
                    preview.Value!.AfterSnapshotJson,
                    idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false);
        if (!submitted.IsSuccess)
        {
            return Result<SerialRuleUpdateApprovalSubmissionResponse>.Failure(submitted.Error!);
        }

        var value = submitted.Value!;
        return Result<SerialRuleUpdateApprovalSubmissionResponse>.Success(
            new SerialRuleUpdateApprovalSubmissionResponse(
                value.RequestId,
                value.StatusKey,
                preview.Value.Changes,
                value.BeforeSnapshotJson,
                value.AfterSnapshotJson,
                value.WorkflowDefinitionVersionId,
                value.Version));
    }

    /// <summary>判断当前作用域是否要求流水号规则更新走审批。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<bool> RequiresApprovalAsync(CancellationToken cancellationToken = default) =>
        scenarioPolicyPort.BlocksDirectWriteAsync(
            DataApprovalScenarioKeys.SerialRuleHostUpdate,
            cancellationToken);

    private async Task<Result<SerialRuleApprovalSnapshot>> ValidateProposedUpdateAsync(
        Guid ruleId,
        UpdateSerialNumberRuleRequest request,
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
                SerialNumberErrorCodes.UpdateApprovalNotRequired,
                "The serial number rule update approval scenario is not enabled.",
                ErrorType.Validation));
        }

        var current = await ruleService.GetAsync(ruleId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(current.Error!);
        }

        if (current.Value!.Version != request.Version)
        {
            return Result<SerialRuleApprovalSnapshot>.Failure(new Error(
                SerialNumberErrorCodes.RuleVersionConflict,
                "The serial number rule version is out of date.",
                ErrorType.Conflict));
        }

        var snapshot = await approvalSource.GetSnapshotAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
        return snapshot;
    }

    private static string SerializeUpdate(UpdateSerialNumberRuleRequest request) =>
        JsonSerializer.Serialize(
            request,
            SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest);
}
