using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.Modules.Workflow.Features.ManageForms;

internal sealed record CreateWorkflowFormRequest(
    string FormKey,
    WorkflowFormSchema Draft);

internal sealed record UpdateWorkflowFormDraftRequest(
    long ExpectedRevision,
    WorkflowFormSchema Draft);

internal sealed record PublishWorkflowFormRequest(long ExpectedRevision);

internal sealed record WorkflowFormResponse(
    Guid Id,
    string FormKey,
    WorkflowFormSchema Draft,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

internal sealed record WorkflowFormVersionResponse(
    Guid Id,
    Guid FormDefinitionId,
    int VersionNumber,
    int SchemaVersion,
    int AdapterVersion,
    int ComponentCatalogVersion,
    string FormSchemaJson,
    string WebRenderSchemaJson,
    string ContentHash,
    Guid PublishedById,
    DateTimeOffset PublishedAtUtc);
