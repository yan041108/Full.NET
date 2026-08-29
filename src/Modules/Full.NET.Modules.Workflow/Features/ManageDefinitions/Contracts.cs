using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

internal sealed record CreateWorkflowDefinitionRequest(
    string DefinitionKey,
    WorkflowDefinitionDraft Draft);

internal sealed record UpdateWorkflowDefinitionDraftRequest(
    long ExpectedRevision,
    WorkflowDefinitionDraft Draft);

internal sealed record PublishWorkflowDefinitionRequest(
    long ExpectedRevision,
    Guid FormVersionId);
