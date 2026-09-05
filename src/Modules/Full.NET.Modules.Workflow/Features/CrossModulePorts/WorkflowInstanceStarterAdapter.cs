using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageInstances;

namespace Full.NET.Modules.Workflow.Features.CrossModulePorts;

/// <summary>将跨模块启动命令适配到工作流实例管理服务。</summary>
internal sealed class WorkflowInstanceStarterAdapter(
    WorkflowInstanceManagementService instanceManagement) : IWorkflowInstanceStarter
{
    /// <inheritdoc />
    public async Task<Result<WorkflowInstanceLifecycleResult>> StartAsync(
        Guid actorUserId,
        StartWorkflowInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        JsonElement initialValues;
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(command.InitialValuesJson)
                    ? "{}"
                    : command.InitialValuesJson);
            initialValues = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Result<WorkflowInstanceLifecycleResult>.Failure(new Error(
                WorkflowErrorCodes.SchemaInvalid,
                "The workflow initial values JSON is invalid.",
                ErrorType.Validation));
        }

        var result = await instanceManagement.StartAsync(
            actorUserId,
            new StartWorkflowInstanceRequest(
                command.DefinitionVersionId,
                command.BusinessType,
                command.BusinessId,
                initialValues,
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
