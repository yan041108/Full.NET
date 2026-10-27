using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;

internal sealed class SubmitEnterpriseRequestForApprovalService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    ICurrentTenant currentTenant,
    IWorkflowPublishedDefinitionDirectory definitionDirectory,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer,
    ICommandTransaction transaction,
    IOutboxWriter outbox)
{
    public async Task<Result<EnterpriseRequestResponse>> SubmitAsync(
        Guid requestId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                "enterprise_request.tenant_context_required",
                "Tenant context is required.",
                ErrorType.Forbidden));
        }

        var row = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                new { Id = requestId },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestErrorCodes.NotFound,
                "The resource was not found.",
                ErrorType.NotFound));
        }

        if (row.ApplicantUserId != actorUserId)
            return Result<EnterpriseRequestResponse>.Failure(new Error("enterprise_request.submit_forbidden",
                "Only the applicant may submit this request.", ErrorType.Forbidden));
        var authorization = await writeAuthorizer.EnsureCanWriteAsync(currentTenant.Id.Value,
            row.OrganizationUnitId, actorUserId, cancellationToken).ConfigureAwait(false);
        if (!authorization.IsSuccess) return Result<EnterpriseRequestResponse>.Failure(authorization.Error!);
        if (!authorization.Value)
            return Result<EnterpriseRequestResponse>.Failure(new Error("enterprise_request.submit_forbidden",
                "The request is outside the allowed organization scope.", ErrorType.Forbidden));
        // 响应丢失后的重复提交不再拒绝；已提交意图由可靠事件继续推进。
        if (row.Status == EnterpriseRequestStatusKeys.Submitted)
            return Result<EnterpriseRequestResponse>.Success(Map(row));
        if (!string.Equals(row.Status, EnterpriseRequestStatusKeys.Draft, StringComparison.Ordinal))
        {
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
        return await transaction.ExecuteResultAsync(async token =>
        {
        var affected = await commandExecutor.ExecuteAsync(
                EnterpriseRequestWorkflowSql.ApplySubmittedStatus,
                new
                {
                    Id = requestId,
                    TenantId = currentTenant.Id.Value,
                    Status = EnterpriseRequestStatusKeys.Submitted,
                    UpdatedAtUtc = now,
                    UpdatedById = actorUserId,
                    ExpectedStatus = EnterpriseRequestStatusKeys.Draft,
                    ExpectedVersion = row.Version,
                },
                token)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestErrorCodes.VersionConflict,
                "The resource was updated concurrently.",
                ErrorType.Conflict));
        }

        // 只提交本模块状态与意图；Workflow 在消费时使用自身事务和固定幂等键。
        await outbox.AddAsync(EnterpriseRequestSubmittedHandler.MessageType, 1,
            new EnterpriseRequestSubmitted(requestId, actorUserId, published.DefinitionVersionId, row.Version, row.Title), token)
            .ConfigureAwait(false);

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                new { Id = requestId },
                token)
            .ConfigureAwait(false);
        return updated is null
            ? Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestErrorCodes.NotFound,
                "The resource was not found.",
                ErrorType.NotFound))
            : Result<EnterpriseRequestResponse>.Success(Map(updated));
        }, cancellationToken).ConfigureAwait(false);
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
