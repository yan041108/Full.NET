namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

using System.Text.Json;

internal sealed record ActWorkflowTodoRequest(
    long ExpectedRevision,
    JsonElement FieldPatch,
    string? Comment,
    string IdempotencyKey);

internal sealed record WorkflowTodoResponse(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid AssigneeUserId,
    string StatusKey,
    DateTimeOffset ArrivedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultActionKey,
    long Revision);

internal sealed record WorkflowTodoDetailResponse(
    Guid Id,
    Guid InstanceId,
    Guid StepId,
    Guid AssigneeUserId,
    string StatusKey,
    long Revision,
    Guid FormVersionId,
    JsonElement FormSchema,
    JsonElement Submission,
    IReadOnlyDictionary<string, string> FieldPolicies,
    long SubmissionRevision);
