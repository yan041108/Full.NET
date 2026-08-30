using System.Text.Json;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

internal sealed record StartWorkflowInstanceRequest(
    Guid DefinitionVersionId,
    string BusinessType,
    string BusinessId,
    JsonElement InitialValues,
    string IdempotencyKey);

internal sealed record CancelWorkflowInstanceRequest(
    long ExpectedRevision,
    string? Reason,
    string IdempotencyKey);

internal sealed record WorkflowInstanceResponse(
    Guid Id,
    Guid DefinitionVersionId,
    Guid FormVersionId,
    string BusinessType,
    string BusinessId,
    string StatusKey,
    long Revision,
    Guid? ActiveTodoId,
    DateTimeOffset StartedAtUtc);

internal sealed record WorkflowExecutionLogResponse(
    Guid Id,
    Guid InstanceId,
    Guid? StepId,
    string TransitionKey,
    string? FromStatusKey,
    string ToStatusKey,
    DateTimeOffset CreatedAtUtc);
