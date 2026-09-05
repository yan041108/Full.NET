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

internal sealed record WorkflowNodeTypeCatalogResponse(
    int CatalogVersion,
    int DefinitionSchemaVersion,
    IReadOnlyList<WorkflowNodeTypeResponse> NodeTypes);

internal sealed record WorkflowNodeTypeResponse(
    string NodeTypeKey,
    int NodeSchemaVersion,
    bool Designable,
    bool Publishable,
    bool Executable,
    bool SupportsFieldPolicies);

/// <summary>供工作流设计器选择抄送人的最小用户投影。</summary>
/// <param name="Id">稳定用户标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">显示名称。</param>
internal sealed record WorkflowRecipientCandidateResponse(
    Guid Id,
    string Username,
    string DisplayName);

/// <summary>工作流抄送候选人的受控分页结果。</summary>
/// <param name="Items">当前页候选人。</param>
/// <param name="Page">从 1 开始的页码。</param>
/// <param name="PageSize">受控单页数量。</param>
/// <param name="Total">活动用户总数。</param>
internal sealed record WorkflowRecipientCandidatePageResponse(
    IReadOnlyList<WorkflowRecipientCandidateResponse> Items,
    int Page,
    int PageSize,
    long Total);

/// <summary>供工作流设计器选择办理人角色的最小角色投影。</summary>
/// <param name="Id">稳定角色标识。</param>
/// <param name="Code">稳定角色编码。</param>
/// <param name="Name">角色显示名称。</param>
internal sealed record WorkflowRoleCandidateResponse(
    Guid Id,
    string Code,
    string Name);

/// <summary>工作流办理人角色候选的受控分页结果。</summary>
/// <param name="Items">当前页候选人。</param>
/// <param name="Page">从 1 开始的页码。</param>
/// <param name="PageSize">受控单页数量。</param>
/// <param name="Total">活动角色总数。</param>
internal sealed record WorkflowRoleCandidatePageResponse(
    IReadOnlyList<WorkflowRoleCandidateResponse> Items,
    int Page,
    int PageSize,
    long Total);

/// <summary>供工作流设计器选择机构负责人的最小机构单元投影。</summary>
/// <param name="Id">稳定机构单元标识。</param>
/// <param name="Code">稳定机构编码。</param>
/// <param name="Name">机构显示名称。</param>
internal sealed record WorkflowOrganizationUnitCandidateResponse(
    Guid Id,
    string Code,
    string Name);

/// <summary>工作流机构单元候选的受控分页结果。</summary>
/// <param name="Items">当前页候选人。</param>
/// <param name="Page">从 1 开始的页码。</param>
/// <param name="PageSize">受控单页数量。</param>
/// <param name="Total">活动机构单元总数。</param>
internal sealed record WorkflowOrganizationUnitCandidatePageResponse(
    IReadOnlyList<WorkflowOrganizationUnitCandidateResponse> Items,
    int Page,
    int PageSize,
    long Total);

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
