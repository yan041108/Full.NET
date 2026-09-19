using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;

internal sealed class SubmitEnterpriseRequestForApprovalService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    ICurrentTenant currentTenant,
    IWorkflowPublishedDefinitionDirectory definitionDirectory,
    IWorkflowInstanceStarter workflowStarter)
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
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestErrorCodes.VersionConflict,
                "The resource was updated concurrently.",
                ErrorType.Conflict));
        }

        var start = await workflowStarter.StartAsync(
                actorUserId,
                new StartWorkflowInstanceCommand(
                    published.DefinitionVersionId,
                    EnterpriseRequestWorkflowConstants.BusinessType,
                    requestId.ToString("D"),
                    "{}",
                    $"submit:{requestId:D}:{row.Version}",
                    row.Title),
                cancellationToken)
            .ConfigureAwait(false);
        if (!start.IsSuccess)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                EnterpriseRequestWorkflowErrorCodes.WorkflowStartFailed,
                start.Error?.Message ?? "Workflow start failed.",
                ErrorType.Conflict));
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                new { Id = requestId },
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