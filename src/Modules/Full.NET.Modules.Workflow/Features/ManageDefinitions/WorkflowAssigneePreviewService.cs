using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>按当前可信作用域预览办理人解析结果，供设计器范围校验。</summary>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
/// <param name="resolver">办理人解析器。</param>
/// <param name="hostUserDirectory">Host 活动用户批量目录。</param>
/// <param name="tenantUserDirectory">Tenant 活动用户批量目录。</param>
internal sealed class WorkflowAssigneePreviewService(
    ICurrentTenant currentTenant,
    WorkflowAssigneeResolver resolver,
    IHostUserBatchSelectionDirectory hostUserDirectory,
    ITenantUserSelectionDirectory tenantUserDirectory)
{
    /// <summary>预览办理人策略在当前作用域下对指定发起人的解析结果。</summary>
    /// <param name="request">办理人策略与可选发起人。</param>
    /// <param name="actorUserId">当前操作人，在未显式指定发起人时作为预览发起人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析成功时返回去重后的活动用户投影；失败时返回稳定业务错误。</returns>
    public async Task<Result<WorkflowAssigneePreviewResponse>> PreviewAsync(
        PreviewWorkflowAssigneeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!WorkflowAssigneePolicy.TryReadPolicy(request.AssigneePolicy, out var policy))
        {
            return Invalid();
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var initiatorUserId = request.InitiatorUserId ?? actorUserId;
        if (initiatorUserId == Guid.Empty)
        {
            return Invalid();
        }

        var resolved = await resolver.ResolveAsync(
                policy,
                [],
                scope,
                initiatorUserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Result<WorkflowAssigneePreviewResponse>.Failure(resolved.Error!);
        }

        var users = await MapUsersAsync(resolved.Value!, scope, cancellationToken).ConfigureAwait(false);
        if (users.Count != resolved.Value!.Count)
        {
            return Invalid();
        }

        return Result<WorkflowAssigneePreviewResponse>.Success(new WorkflowAssigneePreviewResponse(users));
    }

    /// <summary>把解析出的用户标识映射为设计器可展示的最小投影。</summary>
    /// <param name="userIds">解析出的用户标识。</param>
    /// <param name="scope">当前可信作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与输入顺序一致的用户投影。</returns>
    private async Task<IReadOnlyList<WorkflowRecipientCandidateResponse>> MapUsersAsync(
        IReadOnlyList<Guid> userIds,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.TenantId.HasValue)
        {
            var users = await tenantUserDirectory
                .FindActiveTenantUsersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
            return userIds
                .Where(users.ContainsKey)
                .Select(userId => new WorkflowRecipientCandidateResponse(
                    userId,
                    users[userId].Username,
                    users[userId].DisplayName))
                .ToArray();
        }

        var hostUsers = await hostUserDirectory
            .FindActiveHostUsersAsync(userIds, cancellationToken)
            .ConfigureAwait(false);
        return userIds
            .Where(hostUsers.ContainsKey)
            .Select(userId => new WorkflowRecipientCandidateResponse(
                userId,
                hostUsers[userId].Username,
                hostUsers[userId].DisplayName))
            .ToArray();
    }

    /// <summary>构造预览请求无效的统一业务错误。</summary>
    /// <returns>稳定验证错误。</returns>
    private static Result<WorkflowAssigneePreviewResponse> Invalid() =>
        Result<WorkflowAssigneePreviewResponse>.Failure(new Error(
            WorkflowErrorCodes.DefinitionAssigneePolicyInvalid,
            "The assignee policy could not be previewed.",
            ErrorType.Validation));
}
