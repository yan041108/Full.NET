using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>在表单提交变更时同步附件 claim 与本地投影，保证 Files 删除保护与运行时授权一致。</summary>
internal sealed class WorkflowFormAttachmentCoordinator(
    IHostFileReferenceClaimService hostFileReferenceClaimService,
    IHostFileDescriptorReader hostFileDescriptorReader,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>
    /// 在持久化新的提交 JSON 前校验并同步附件 claim；失败时回滚本次新增的 Pending claim。
    /// </summary>
    /// <param name="actorUserId">当前操作人标识。</param>
    /// <param name="submissionId">表单提交标识。</param>
    /// <param name="instanceId">流程实例标识。</param>
    /// <param name="tenantScopeKey">租户作用域键。</param>
    /// <param name="schema">已发布表单结构。</param>
    /// <param name="previousValues">变更前的字段值。</param>
    /// <param name="nextValues">变更后的字段值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步成功或稳定业务错误。</returns>
    public async Task<Result<bool>> SynchronizeAsync(
        Guid actorUserId,
        Guid submissionId,
        Guid instanceId,
        string tenantScopeKey,
        WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> previousValues,
        IReadOnlyDictionary<string, JsonElement> nextValues,
        CancellationToken cancellationToken = default)
    {
        var previousAttachments = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, previousValues);
        var nextAttachments = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, nextValues);
        var added = new List<(string FieldKey, Guid FileId)>();
        var removed = new List<(string FieldKey, Guid FileId)>();

        foreach (var (fieldKey, fileIds) in nextAttachments)
        {
            previousAttachments.TryGetValue(fieldKey, out var previousIds);
            foreach (var fileId in fileIds.Where(fileId => previousIds?.Contains(fileId) != true))
            {
                added.Add((fieldKey, fileId));
            }
        }

        foreach (var (fieldKey, fileIds) in previousAttachments)
        {
            nextAttachments.TryGetValue(fieldKey, out var nextIds);
            foreach (var fileId in fileIds.Where(fileId => nextIds?.Contains(fileId) != true))
            {
                removed.Add((fieldKey, fileId));
            }
        }

        if (added.Count == 0 && removed.Count == 0)
        {
            return Result<bool>.Success(true);
        }

        var fieldMap = schema.Sections.SelectMany(section => section.Fields)
            .Where(field => field.FieldTypeKey == "attachment")
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);

        var claimedKeys = new List<string>();
        foreach (var (fieldKey, fileId) in added)
        {
            if (!fieldMap.TryGetValue(fieldKey, out var field))
            {
                return AttachmentInvalid();
            }

            var validation = await ValidateNewAttachmentAsync(
                    actorUserId,
                    submissionId,
                    field,
                    fileId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!validation.IsSuccess)
            {
                await ReleaseClaimedAsync(claimedKeys, cancellationToken).ConfigureAwait(false);
                return validation;
            }

            var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(
                submissionId,
                fileId);
            var claimResult = await hostFileReferenceClaimService
                .ClaimAsync(
                    new HostFileReferenceClaimRequest(
                        idempotencyKey,
                        HostFileReferenceClaimConsumerModules.Workflow,
                        submissionId,
                        fileId),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!claimResult.IsSuccess)
            {
                await ReleaseClaimedAsync(claimedKeys, cancellationToken).ConfigureAwait(false);
                return AttachmentInvalid();
            }

            claimedKeys.Add(idempotencyKey);
        }

        try
        {
            var now = clock.UtcNow;
            foreach (var (fieldKey, fileId) in removed)
            {
                await commandExecutor.ExecuteAsync(
                        WorkflowFormSubmissionAttachmentSql.DeleteBySubmissionFieldFile,
                        WorkflowSqlParameters.Create(
                            ("SubmissionId", submissionId),
                            ("FieldKey", fieldKey),
                            ("FileId", fileId)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var (fieldKey, fileId) in added)
            {
                await commandExecutor.ExecuteAsync(
                        WorkflowFormSubmissionAttachmentSql.Insert,
                        WorkflowSqlParameters.Create(
                            ("Id", idGenerator.NewId()),
                            ("SubmissionId", submissionId),
                            ("InstanceId", instanceId),
                            ("FieldKey", fieldKey),
                            ("FileId", fileId),
                            ("TenantScopeKey", tenantScopeKey),
                            ("CreatedAtUtc", now)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await ReleaseClaimedAsync(claimedKeys, cancellationToken).ConfigureAwait(false);
            throw;
        }

        foreach (var idempotencyKey in claimedKeys)
        {
            var confirmResult = await hostFileReferenceClaimService
                .ConfirmAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (!confirmResult.IsSuccess)
            {
                return AttachmentInvalid();
            }
        }

        foreach (var (fieldKey, fileId) in removed)
        {
            var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(
                submissionId,
                fileId);
            _ = await hostFileReferenceClaimService
                .ReleaseAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    private async Task<Result<bool>> ValidateNewAttachmentAsync(
        Guid actorUserId,
        Guid submissionId,
        WorkflowFormField field,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        if (!WorkflowFormAttachmentConstraints.TryRead(
                field,
                out _,
                out var maxSizeBytes,
                out var allowedExtensions))
        {
            return AttachmentInvalid();
        }

        var descriptor = await hostFileDescriptorReader
            .GetReadyDescriptorAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        if (descriptor is null ||
            descriptor.SizeBytes > maxSizeBytes ||
            !WorkflowFormAttachmentValueRules.MatchesAllowedExtension(
                descriptor.OriginalFileName,
                allowedExtensions))
        {
            return AttachmentInvalid();
        }

        if (descriptor.CreatedByUserId != actorUserId)
        {
            var reused = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                    WorkflowFormSubmissionAttachmentSql.ExistsForSubmissionFile,
                    WorkflowSqlParameters.Create(("SubmissionId", submissionId), ("FileId", fileId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (reused != 1)
            {
                return AttachmentInvalid();
            }
        }

        return Result<bool>.Success(true);
    }

    private async Task ReleaseClaimedAsync(
        IReadOnlyList<string> idempotencyKeys,
        CancellationToken cancellationToken)
    {
        foreach (var idempotencyKey in idempotencyKeys)
        {
            _ = await hostFileReferenceClaimService
                .ReleaseAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Result<bool> AttachmentInvalid() =>
        Result<bool>.Failure(new Error(
            WorkflowErrorCodes.FormAttachmentInvalid,
            "The workflow form attachment is invalid or unauthorized.",
            ErrorType.Validation));
}
