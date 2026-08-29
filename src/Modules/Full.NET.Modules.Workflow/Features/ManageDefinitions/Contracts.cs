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

internal sealed record WorkflowDefinitionResponse(
    Guid Id,
    string DefinitionKey,
    WorkflowDefinitionDraft Draft,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

internal sealed record WorkflowDefinitionVersionResponse(
    Guid Id,
    Guid DefinitionId,
    Guid FormVersionId,
    int VersionNumber,
    int SchemaVersion,
    string CanonicalJson,
    string ContentHash,
    Guid PublishedById,
    DateTimeOffset PublishedAtUtc);
