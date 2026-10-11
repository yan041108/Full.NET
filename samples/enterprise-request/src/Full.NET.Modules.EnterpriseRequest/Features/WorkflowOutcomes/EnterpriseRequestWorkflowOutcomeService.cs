using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;

namespace Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;

/// <summary>只接受提交日志绑定的流程实例，结果和终态回执在本模块事务中共同落库。</summary>
internal sealed class EnterpriseRequestWorkflowOutcomeService(
    IQueryExecutor queryExecutor, ICommandExecutor commandExecutor, IClock clock,
    ICommandTransaction transaction, ICurrentTenantContextWriter tenantWriter)
{
    public async Task HandleTerminalWorkflowAsync(
        string businessType, string businessId, string targetStatus,
        Guid instanceId, Guid? tenantId, Guid messageId,
        CancellationToken cancellationToken = default)
    {
        if (businessType != EnterpriseRequestWorkflowConstants.BusinessType ||
            !Guid.TryParse(businessId, out var requestId) || requestId == Guid.Empty ||
            !EnterpriseRequestStatusTransition.IsTerminal(targetStatus) ||
            instanceId == Guid.Empty || tenantId is null || tenantId == Guid.Empty || messageId == Guid.Empty)
            return;
        // 已取消的有效业务投递必须保持可重放，不建立作用域、不读取或提交成功回执。
        cancellationToken.ThrowIfCancellationRequested();
        // 已经由 Worker 校验的事件元数据用于恢复租户；事务内不跨模块解析租户。
        using var scope = new EnterpriseRequestEventTenantScope(tenantWriter,
            new TenantContext(tenantId.Value, tenantId.Value.ToString("D"), tenantId.Value.ToString("D")));
        var submission = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
            EnterpriseRequestApprovalSql.FindByRequest,
            EnterpriseRequestApprovalSql.Parameters(("RequestId", requestId)), cancellationToken).ConfigureAwait(false);
        if (submission is not null && (submission.RequestId != requestId || submission.TenantId != tenantId ||
            submission.WorkflowInstanceId != instanceId || submission.FinalStatus is not null))
            return;
        var row = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
            EnterpriseRequestSql.FindByIdStatement,
            EnterpriseRequestApprovalSql.Parameters(("Id", requestId)), cancellationToken).ConfigureAwait(false);
        if (row is null || row.Id != requestId || row.TenantId != tenantId || row.IsDeleted ||
            row.Status != EnterpriseRequestStatusKeys.Submitted)
            return;
        // 历史 Submitted 没有可信绑定时必须进入重试/死信，不得静默确认并丢失结果。
        if (submission is null)
            throw new InvalidOperationException("enterprise_request.legacy_binding_required");
        if (row.Version != submission.RequestVersion)
            throw new InvalidOperationException("enterprise_request.submitted_snapshot_mismatch");
        var now = clock.UtcNow;
        var result = await transaction.ExecuteResultAsync(async token =>
        {
            var affected = await commandExecutor.ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
                EnterpriseRequestApprovalSql.Parameters(("Id", requestId), ("Status", targetStatus),
                    ("UpdatedAtUtc", now), ("ExpectedStatus", EnterpriseRequestStatusKeys.Submitted),
                    ("ExpectedVersion", submission.RequestVersion)), token).ConfigureAwait(false);
            if (affected != 1) return Conflict();
            var recorded = await commandExecutor.ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
                EnterpriseRequestApprovalSql.Parameters(("Id", submission.Id), ("WorkflowInstanceId", instanceId),
                    ("FinalStatus", targetStatus), ("CompletedAtUtc", now), ("LastMessageId", messageId)), token).ConfigureAwait(false);
            return recorded == 1 ? Result<bool>.Success(true) : Conflict();
        }, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var latest = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
                EnterpriseRequestApprovalSql.FindByRequest, EnterpriseRequestApprovalSql.Parameters(("RequestId", requestId)),
                cancellationToken).ConfigureAwait(false);
            if (latest?.Id != submission.Id || latest.WorkflowInstanceId != instanceId || latest.FinalStatus is null)
                throw new InvalidOperationException("enterprise_request.status_update_conflict");
        }
    }
    private static Result<bool> Conflict() => Result<bool>.Failure(
        new Error("enterprise_request.status_update_conflict", "The submission changed concurrently.", ErrorType.Conflict));
}
