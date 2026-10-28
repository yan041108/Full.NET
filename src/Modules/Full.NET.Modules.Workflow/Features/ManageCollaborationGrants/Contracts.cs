using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageCollaborationGrants;

/// <summary>目标地址只用于提出双边同意，不切换当前租户上下文。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ProposeWorkflowCollaborationGrantRequest(Guid? TargetTenantId, string TargetScopeKey, string IdempotencyKey, JsonElement Offer);
/// <summary>目标只提交自己的版本、成员和映射；安全摘要由服务器生成。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AcceptWorkflowCollaborationGrantRequest(long ExpectedRevision, string IdempotencyKey, JsonElement Acceptance);
/// <summary>任一参与方按权威修订撤销，原因保存于控制面。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RevokeWorkflowCollaborationGrantRequest(long ExpectedRevision, string IdempotencyKey, string Reason);
/// <summary>只向授权双方返回固定政策及安全摘要，不返回对方完整定义或业务数据。</summary>
internal sealed record WorkflowCollaborationGrantResponse(Guid Id, long Revision, string StatusKey, string SourceTenantScopeKey,
    string TargetTenantScopeKey, Guid SourceActorId, Guid? TargetActorId, JsonElement Offer, JsonElement? Acceptance,
    string? PolicyHash, JsonElement? TargetSummary, DateTimeOffset ExpiresAtUtc, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcceptedAtUtc, DateTimeOffset? RevokedAtUtc, Guid? RevokedById, string? RevokedByScopeKey);
/// <summary>摘要只包含版本身份、内容摘要和已同意映射的字段类型，不泄漏节点配置。</summary>
internal sealed record WorkflowCollaborationTargetSummary(Guid DefinitionVersionId, string DefinitionKey, string DefinitionContentHash,
    string FormContentHash, IReadOnlyDictionary<string,string> InputFields, IReadOnlyDictionary<string,string> OutputFields);
