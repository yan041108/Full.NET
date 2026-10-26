using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Features;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;

/// <summary>消费工作流终态事件并驱动 DataApproval 请求状态与业务应用。</summary>
internal sealed class DataApprovalWorkflowOutcomeService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    DataApprovalRequestApplicationService applicationService)
{
    /// <summary>按工作流实例终态更新 DataApproval 请求。</summary>
    /// <param name="tenantId">事件租户标识。</param>
    /// <param name="workflowInstanceId">事件所属真实根实例，必须与请求已关联实例一致。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识，即请求 Id 文本。</param>
    /// <param name="workflowStatusKey">工作流实例终态键。</param>
    /// <param name="actorUserId">流程发起人，用于应用已批准变更。</param>
    /// <param name="idempotencyKey">消息级幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task HandleTerminalWorkflowAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string businessType,
        string businessId,
        string workflowStatusKey,
        Guid actorUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (workflowInstanceId == Guid.Empty || !IsSerialNumberRuleBusinessType(businessType))
        {
            return;
        }

        if (!Guid.TryParse(businessId, out var requestId))
        {
            return;
        }

        var targetStatus = DataApprovalStatusTransition.MapWorkflowTerminalStatus(workflowStatusKey);
        if (targetStatus is null)
        {
            return;
        }

        var scope = ResolveScope(tenantId);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestByBusinessId,
                DataApprovalSqlParameters.Create(
                    ("BusinessId", requestId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        // 自动流程可能先完成、后由独立所有者事务关联；保留消息重试，不能静默吞掉终态事实。
        if (row.WorkflowInstanceId is null)
        {
            if (row.StatusKey is DataApprovalStatusKeys.Pending or DataApprovalStatusKeys.InReview)
                throw new InvalidOperationException("data_approvals.workflow_link_pending");
            return;
        }
        // 业务ID不是调用授权：只有该请求固化的根实例、发起人、场景与作用域可以驱动业务应用。
        if (row.WorkflowInstanceId != workflowInstanceId || row.SubmittedByUserId != actorUserId ||
            row.TenantId != scope.TenantId || row.TenantScopeKey != scope.TenantScopeKey ||
            DataApprovalScenarioCatalog.Find(row.ScenarioKey)?.WorkflowBusinessType != businessType) return;

        if (string.Equals(row.StatusKey, targetStatus, StringComparison.Ordinal))
        {
            return;
        }

        if (!DataApprovalStatusTransition.CanResolveFromWorkflow(row.StatusKey))
        {
            return;
        }

        if (string.Equals(targetStatus, DataApprovalStatusKeys.Approved, StringComparison.Ordinal))
        {
            // 节点命令已由所有者凭据提交；根终态只结清审批请求，不能再执行旧的规则应用路径。
            if (await queryExecutor.QuerySingleOrDefaultAsync<int>(DataApprovalCommandSql.CountApplied,
                    DataApprovalSqlParameters.Create(("RequestId", row.Id), ("InstanceId", workflowInstanceId), ("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false) > 0)
            {
                var completed = await applicationService.CompleteApprovedApplicationAsync(row, cancellationToken).ConfigureAwait(false);
                if (!completed.IsSuccess) throw new InvalidOperationException("data_approvals.command_finalize_conflict");
                return;
            }
            await applicationService.TryApplyApprovedChangeAsync(
                    row,
                    actorUserId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DataApprovalSql.UpdateStatus,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("StatusKey", targetStatus),
                    ("ResolvedAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("ExpectedStatusKey", row.StatusKey),
                    ("ExpectedVersion", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            var latest = await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                    DataApprovalSql.FindRequestByBusinessId,
                    DataApprovalSqlParameters.Create(
                        ("BusinessId", requestId),
                        ("TenantScopeKey", scope.TenantScopeKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (latest is null ||
                !string.Equals(latest.StatusKey, targetStatus, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("data_approvals.status_update_conflict");
            }
        }
    }

    private static bool IsSerialNumberRuleBusinessType(string businessType) =>
        string.Equals(businessType, DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, StringComparison.Ordinal) ||
        string.Equals(businessType, DataApprovalWorkflowBusinessTypes.SerialRuleDisable, StringComparison.Ordinal);

    private static DataApprovalManagementScope ResolveScope(Guid tenantId) =>
        tenantId == Guid.Empty
            ? new(null, "host", "host")
            : new(tenantId, "tenant", $"tenant:{tenantId:N}");
}
