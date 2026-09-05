using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageInstances;

namespace Full.NET.Modules.Workflow.Features.CrossModulePorts;

/// <summary>将跨模块取消命令适配到工作流实例管理服务。</summary>
internal sealed class WorkflowInstanceCancellerAdapter(
    WorkflowInstanceManagementService instanceManagement) : IWorkflowInstanceCanceller
{
    /// <inheritdoc />
    public async Task<Result<WorkflowInstanceLifecycleResult>> CancelAsync(
        Guid actorUserId,
        CancelWorkflowInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await instanceManagement.CancelAsync(
            command.InstanceId,
            actorUserId,
            new CancelWorkflowInstanceRequest(
                command.ExpectedRevision,
                command.Reason,
                command.IdempotencyKey),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result<WorkflowInstanceLifecycleResult>.Failure(result.Error!);
        }

        var instance = result.Value!;
        return Result<WorkflowInstanceLifecycleResult>.Success(
            new WorkflowInstanceLifecycleResult(
                instance.Id,
                instance.StatusKey,
                instance.Revision));
    }
}
