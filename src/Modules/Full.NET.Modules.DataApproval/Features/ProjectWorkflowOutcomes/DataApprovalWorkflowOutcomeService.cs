using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Features;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;

/// <summary>消费工作流终态事件并驱动 DataApproval 请求状态与业务应用。</summary>
internal sealed class DataApprovalWorkflowOutcomeService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    ISerialRuleChangeApprovalApplier serialRuleApplier)
{
    /// <summary>按工作流实例终态更新 DataApproval 请求。</summary>
    /// <param name="tenantId">事件租户标识。</param>
    /// <param name="businessType">稳定业务类型。</param>
    /// <param name="businessId">稳定业务标识，即请求 Id 文本。</param>
    /// <param name="workflowStatusKey">工作流实例终态键。</param>
    /// <param name="actorUserId">流程发起人，用于应用已批准变更。</param>
    /// <param name="idempotencyKey">消息级幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task HandleTerminalWorkflowAsync(
        Guid tenantId,
        string businessType,
        string businessId,
        string workflowStatusKey,
        Guid actorUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                businessType,
                DataApprovalWorkflowBusinessTypes.SerialRuleUpdate,
                StringComparison.Ordinal))
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
        if (row is null || !DataApprovalStatusTransition.CanResolveFromWorkflow(row.StatusKey))
        {
            if (row is not null &&
                string.Equals(row.StatusKey, targetStatus, StringComparison.Ordinal))
            {
                return;
            }

            return;
        }

        if (string.Equals(targetStatus, DataApprovalStatusKeys.Approved, StringComparison.Ordinal) &&
            string.Equals(row.ScenarioKey, DataApprovalScenarioKeys.SerialRuleHostUpdate, StringComparison.Ordinal))
        {
            var apply = await serialRuleApplier.ApplyApprovedUpdateAsync(
                    row.TargetEntityId,
                    row.AfterSnapshotJson,
                    actorUserId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!apply.IsSuccess &&
                apply.Error?.Code is not SerialNumberErrorCodes.RuleVersionConflict)
            {
                throw new InvalidOperationException(
                    $"data_approvals.apply_failed:{apply.Error?.Code}");
            }
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

    private static DataApprovalManagementScope ResolveScope(Guid tenantId) =>
        tenantId == Guid.Empty
            ? new(null, "host", "host")
            : new(tenantId, "tenant", $"tenant:{tenantId:N}");
}
