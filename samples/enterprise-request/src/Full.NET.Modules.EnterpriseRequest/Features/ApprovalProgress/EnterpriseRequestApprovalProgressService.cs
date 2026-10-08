using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;

namespace Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;

/// <summary>审批进度遵循单据的组织读取范围，日志不能作为绕过业务授权的独立入口。</summary>
internal sealed class EnterpriseRequestApprovalProgressService(
    EnterpriseRequestQueryService requests, IQueryExecutor queries, ICurrentTenant tenant)
{
    public async Task<Result<EnterpriseRequestApprovalProgressResponse>> GetAsync(Guid requestId, Guid actorUserId,
        bool isSuperAdministrator, CancellationToken cancellationToken = default)
    {
        if (!tenant.IsAvailable || tenant.Id is not { } tenantId || tenantId == Guid.Empty ||
            requestId == Guid.Empty || actorUserId == Guid.Empty)
            return Failure(EnterpriseRequestErrorCodes.NotFound, ErrorType.NotFound);
        var read = await requests.GetByIdAsync(requestId, actorUserId, isSuperAdministrator, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return Result<EnterpriseRequestApprovalProgressResponse>.Failure(read.Error!);
        var row = read.Value!;
        if (row.Id != requestId || row.TenantId != tenantId || row.IsDeleted)
            return Failure(EnterpriseRequestErrorCodes.NotFound, ErrorType.NotFound);

        var submission = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
            EnterpriseRequestApprovalSql.FindByRequest, EnterpriseRequestApprovalSql.Parameters(("RequestId", requestId)), cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            if (row.Status != EnterpriseRequestStatusKeys.Draft && row.Status != EnterpriseRequestStatusKeys.Submitted &&
                !EnterpriseRequestStatusTransition.IsTerminal(row.Status))
                return Conflict();
            return Result<EnterpriseRequestApprovalProgressResponse>.Success(new(row.Id, row.Status, row.Version,
                row.Status == EnterpriseRequestStatusKeys.Draft ? EnterpriseRequestApprovalDeliveryState.NotSubmitted : EnterpriseRequestApprovalDeliveryState.RecoveryRequired,
                null, null, null, null, null, null));
        }
        // 两次读取可能跨越提交/终态事务，矛盾快照返回可重试冲突，不能拼成错误进度。
        if (submission.Id == Guid.Empty || submission.TenantId != tenantId || submission.RequestId != row.Id ||
            submission.OrganizationUnitId != row.OrganizationUnitId || submission.WorkflowInstanceId == Guid.Empty ||
            submission.WorkflowDefinitionVersionId == Guid.Empty || submission.RequestVersion <= 0 || submission.RequestVersion == long.MaxValue)
            return Conflict();
        EnterpriseRequestApprovalDeliveryState state;
        if (submission.FinalStatus is null)
        {
            if (row.Status != EnterpriseRequestStatusKeys.Submitted || row.Version != submission.RequestVersion ||
                submission.CompletedAtUtc is not null || submission.LastMessageId is not null)
                return Conflict();
            state = submission.StartedAtUtc is null ? EnterpriseRequestApprovalDeliveryState.Queued : EnterpriseRequestApprovalDeliveryState.Started;
        }
        else
        {
            if (!EnterpriseRequestStatusTransition.IsTerminal(submission.FinalStatus) || row.Status != submission.FinalStatus ||
                row.Version != submission.RequestVersion + 1 || submission.CompletedAtUtc is null ||
                submission.LastMessageId is null || submission.LastMessageId == Guid.Empty)
                return Conflict();
            state = EnterpriseRequestApprovalDeliveryState.Finalized;
        }
        return Result<EnterpriseRequestApprovalProgressResponse>.Success(new(row.Id, row.Status, row.Version, state,
            submission.WorkflowDefinitionVersionId, submission.WorkflowInstanceId, submission.RequestVersion,
            submission.CreatedAtUtc, submission.StartedAtUtc, submission.CompletedAtUtc));
    }

    private static Result<EnterpriseRequestApprovalProgressResponse> Conflict() => Failure(EnterpriseRequestErrorCodes.VersionConflict, ErrorType.Conflict);
    private static Result<EnterpriseRequestApprovalProgressResponse> Failure(string code, ErrorType type) =>
        Result<EnterpriseRequestApprovalProgressResponse>.Failure(new Error(code,
            type == ErrorType.NotFound ? "The resource was not found." : "The approval snapshot changed. Refresh to read its current progress.", type));
}
