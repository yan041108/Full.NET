using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;

internal sealed class SubmitEnterpriseRequestForApprovalService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    ICurrentTenant currentTenant,
    IWorkflowPublishedDefinitionDirectory definitionDirectory,
    ICommandTransaction transaction,
    IOutboxWriter outbox,
    IIdGenerator ids,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    public async Task<Result<EnterpriseRequestResponse>> SubmitAsync(
        Guid requestId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null || currentTenant.Id == Guid.Empty)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                "enterprise_request.tenant_context_required",
                "Tenant context is required.",
                ErrorType.Forbidden));
        }

        if (actorUserId == Guid.Empty)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                OrganizationErrorCodes.WriteAccessDenied,
                "A valid actor is required.",
                ErrorType.Forbidden));
        }

        var row = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                EnterpriseRequestApprovalSql.Parameters(("Id", requestId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || row.Id != requestId || row.TenantId != currentTenant.Id.Value || row.IsDeleted)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestErrorCodes.NotFound,
                "The resource was not found.",
                ErrorType.NotFound));
        }

        // 提交与编辑同属组织归属写入；使用记录原机构，不能信任请求头替换目标。
        // 在状态更新及跨模块流程启动前拒绝，避免无权调用留下 Submitted 单据。
        var authorization = await writeAuthorizer.EnsureCanWriteAsync(
                currentTenant.Id.Value, row.OrganizationUnitId, actorUserId, cancellationToken)
            .ConfigureAwait(false);
        if (!authorization.IsSuccess || !authorization.Value)
        {
            return Result<EnterpriseRequestResponse>.Failure(authorization.Error ?? new Error(
                OrganizationErrorCodes.WriteAccessDenied,
                "Write access to the organization unit was denied.",
                ErrorType.Forbidden));
        }

        if (!string.Equals(row.Status, EnterpriseRequestStatusKeys.Draft, StringComparison.Ordinal))
        {
            // HTTP 响应丢失后允许原提交人重放；不能为历史无绑定单据猜测流程身份。
            if (row.Status == EnterpriseRequestStatusKeys.Submitted)
            {
                var existing = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
                    EnterpriseRequestApprovalSql.FindByRequest,
                    EnterpriseRequestApprovalSql.Parameters(("RequestId", requestId)), cancellationToken).ConfigureAwait(false);
                if (existing is not null && existing.RequestId == requestId && existing.TenantId == currentTenant.Id &&
                    existing.SubmittedById == actorUserId && existing.RequestVersion == row.Version &&
                    existing.OrganizationUnitId == row.OrganizationUnitId && existing.WorkflowInstanceId != Guid.Empty)
                    return Result<EnterpriseRequestResponse>.Success(Map(row));
            }

            return Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestWorkflowErrorCodes.InvalidStatus,
                "Only draft requests can be submitted for approval.",
                ErrorType.Conflict));
        }

        var published = await definitionDirectory.FindLatestPublishedAsync(
                EnterpriseRequestWorkflowConstants.DefinitionKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (published is null)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestWorkflowErrorCodes.WorkflowDefinitionMissing,
                "The approval workflow definition is not published.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var submissionId = ids.NewId();
        var instanceId = ids.NewId();
        var committed = await transaction.ExecuteResultAsync(async token =>
        {
            var affected = await commandExecutor.ExecuteAsync(
                    EnterpriseRequestWorkflowSql.ApplySubmittedStatus,
                    EnterpriseRequestApprovalSql.Parameters(("Id", requestId),
                        ("Status", EnterpriseRequestStatusKeys.Submitted), ("UpdatedAtUtc", now),
                        ("UpdatedById", actorUserId), ("ExpectedStatus", EnterpriseRequestStatusKeys.Draft),
                        ("ExpectedVersion", row.Version)),
                    token)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return Result<bool>.Failure(new Error(
                    EnterpriseRequestErrorCodes.VersionConflict,
                    "The resource was updated concurrently.",
                    ErrorType.Conflict));
            }

            // 状态、固定实例身份与 Outbox 共同提交；流程调用不进入本模块事务。
            var inserted = await commandExecutor.ExecuteAsync(EnterpriseRequestApprovalSql.Insert,
                EnterpriseRequestApprovalSql.Parameters(("Id", submissionId), ("TenantId", currentTenant.Id.Value),
                    ("RequestId", requestId), ("RequestVersion", checked(row.Version + 1)),
                    ("WorkflowDefinitionVersionId", published.DefinitionVersionId), ("WorkflowInstanceId", instanceId),
                    ("SubmittedById", actorUserId), ("OrganizationUnitId", row.OrganizationUnitId),
                    ("BusinessTitle", row.Title), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
            if (inserted != 1)
                return Result<bool>.Failure(new Error(EnterpriseRequestErrorCodes.VersionConflict,
                    "The submission could not be recorded.", ErrorType.Conflict));
            await outbox.AddAsync(EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType,
                EnterpriseRequestApprovalSubmittedIntegrationEvent.SchemaVersion,
                new EnterpriseRequestApprovalSubmittedIntegrationEvent(submissionId, requestId), token).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
            return Result<EnterpriseRequestResponse>.Failure(committed.Error!);

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                EnterpriseRequestApprovalSql.Parameters(("Id", requestId)),
                cancellationToken)
            .ConfigureAwait(false);
        return updated is null
            ? Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestErrorCodes.NotFound,
                "The resource was not found.",
                ErrorType.NotFound))
            : Result<EnterpriseRequestResponse>.Success(Map(updated));
    }

    private static EnterpriseRequestResponse Map(EnterpriseRequestRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.RequestNumber,
            record.Title,
            record.Status,
            record.TotalAmount,
            record.ApplicantUserId,
            record.Version,
            record.CreatedAtUtc,
            record.CreatedById,
            record.UpdatedAtUtc,
            record.UpdatedById,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);
}
