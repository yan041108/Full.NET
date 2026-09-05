using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在节点激活前把固化办理人策略与多人审批策略收敛为可写入步骤的快照。</summary>
/// <param name="resolver">办理人解析器。</param>
internal sealed class WorkflowApprovalAssigneeCoordinator(WorkflowAssigneeResolver resolver)
{
    /// <summary>解析下一审批等待点的有效办理人集合。</summary>
    /// <param name="assigneePolicy">发布版本固化的办理人策略。</param>
    /// <param name="approvalPolicy">发布版本固化的多人审批策略；为空时要求解析出唯一办理人。</param>
    /// <param name="scope">当前可信工作流管理作用域。</param>
    /// <param name="initiatorUserId">流程发起人标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>多人策略或单人 fallback 办理人；解析失败时返回业务错误。</returns>
    public async Task<Result<WorkflowApprovalActivationAssignees>> ResolveAsync(
        WorkflowAssigneePolicy assigneePolicy,
        WorkflowApprovalPolicy? approvalPolicy,
        WorkflowManagementScope scope,
        Guid initiatorUserId,
        CancellationToken cancellationToken = default)
    {
        var explicitApproverUserIds = approvalPolicy?.ApproverUserIds ?? [];
        var resolved = await resolver.ResolveAsync(
                assigneePolicy,
                explicitApproverUserIds,
                scope,
                initiatorUserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Result<WorkflowApprovalActivationAssignees>.Failure(resolved.Error!);
        }

        var userIds = resolved.Value!;
        if (approvalPolicy is not null)
        {
            if (userIds.Count is < 2 or > 20)
            {
                return Failure();
            }

            return Result<WorkflowApprovalActivationAssignees>.Success(
                new WorkflowApprovalActivationAssignees(
                    new WorkflowApprovalPolicy(
                        approvalPolicy.ModeKey,
                        userIds,
                        approvalPolicy.RequiredApprovals),
                    Guid.Empty));
        }

        if (userIds.Count != 1)
        {
            return Failure();
        }

        return Result<WorkflowApprovalActivationAssignees>.Success(
            new WorkflowApprovalActivationAssignees(null, userIds[0]));
    }

    /// <summary>构造办理人解析失败的统一业务错误。</summary>
    /// <returns>稳定验证错误。</returns>
    private static Result<WorkflowApprovalActivationAssignees> Failure() =>
        Result<WorkflowApprovalActivationAssignees>.Failure(new Error(
            WorkflowErrorCodes.DefinitionAssigneePolicyInvalid,
            "The assignee policy could not be resolved.",
            ErrorType.Validation));
}

/// <summary>描述审批节点激活时写入步骤的办理人快照。</summary>
/// <param name="ApprovalPolicy">多人审批策略；为空时表示单人办理。</param>
/// <param name="FallbackAssigneeUserId">单人办理时的办理人标识。</param>
internal sealed record WorkflowApprovalActivationAssignees(
    WorkflowApprovalPolicy? ApprovalPolicy,
    Guid FallbackAssigneeUserId);
