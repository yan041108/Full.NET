using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageConnectorCalls;

/// <summary>目录仅暴露当前可信作用域可选版本及参数类型，不包含目标地址和凭据引用。</summary>
public sealed record WorkflowConnectorCatalogResponse(IReadOnlyList<WorkflowConnectorCatalogItemResponse> Items);
public sealed record WorkflowConnectorCatalogItemResponse(string ConnectorKey,int ConnectorVersion,bool SupportsIdempotency,
    IReadOnlyList<WorkflowConnectorParameterResponse> InputParameters,IReadOnlyList<WorkflowConnectorParameterResponse> OutputParameters);
public sealed record WorkflowConnectorParameterResponse(string ParameterKey,string FieldTypeKey,bool Required,IReadOnlyDictionary<string,JsonElement> Constraints);
/// <summary>运维视图不暴露原始请求或结果，接纳结果只引用服务器已保存的尝试标识。</summary>
public sealed record WorkflowConnectorCallResponse(Guid Id,Guid InstanceId,string NodeKey,string ConnectorKey,int ConnectorVersion,
    string ModeKey,string StatusKey,long Revision,int AttemptCount,int ApplyAttemptCount,string? ErrorKey,long InputFormRevision,
    long? AppliedFormRevision,bool CancellationRequested,bool RequiresReconciliation,DateTimeOffset CreatedAtUtc,DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<WorkflowConnectorAttemptResponse> Attempts);
public sealed record WorkflowConnectorAttemptResponse(Guid Id,long LeaseGeneration,string StatusKey,string? OutcomeKey,
    int? HttpStatusCode,string? ErrorKey,string? ResponseHash,DateTimeOffset StartedAtUtc,DateTimeOffset? CompletedAtUtc);
public sealed record WorkflowConnectorCallPageResponse(IReadOnlyList<WorkflowConnectorCallResponse> Items,int Page,int PageSize,int Total);

/// <summary>对账不能伪造响应、Actor或Tenant；业务载荷及原输入始终来自原激活。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReconcileWorkflowConnectorRequest(long ExpectedInstanceRevision,long ExpectedRevision,string ActionKey,
    Guid? AttemptId,string? EvidenceReference,string Reason,string IdempotencyKey);
/// <summary>不可变对账回执不表示已自动恢复实例或撤销远端副作用。</summary>
public sealed record WorkflowConnectorResolutionResponse(Guid Id,Guid CallId,Guid InstanceId,string ActionKey,string StatusKey,
    long Revision,string InstanceStatusKey,long InstanceRevision,DateTimeOffset CreatedAtUtc);
