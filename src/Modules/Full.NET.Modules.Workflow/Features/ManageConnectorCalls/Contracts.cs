using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageConnectorCalls;

/// <summary>目录仅暴露当前可信作用域可选版本及参数类型，不包含目标地址和凭据引用。</summary>
public sealed record WorkflowConnectorCatalogResponse(IReadOnlyList<WorkflowConnectorCatalogItemResponse> Items);
/// <summary>单个连接器版本目录项：声明可选的连接器键、版本与幂等能力，仅暴露输入/输出参数结构，不包含目标地址与凭据引用。</summary>
public sealed record WorkflowConnectorCatalogItemResponse(string ConnectorKey,int ConnectorVersion,bool SupportsIdempotency,
    IReadOnlyList<WorkflowConnectorParameterResponse> InputParameters,IReadOnlyList<WorkflowConnectorParameterResponse> OutputParameters);
/// <summary>连接器参数描述：仅含参数键、字段类型键、是否必填与约束条件，不携带默认值或机密内容。</summary>
public sealed record WorkflowConnectorParameterResponse(string ParameterKey,string FieldTypeKey,bool Required,IReadOnlyDictionary<string,JsonElement> Constraints);
/// <summary>运维视图不暴露原始请求或结果，接纳结果只引用服务器已保存的尝试标识。</summary>
public sealed record WorkflowConnectorCallResponse(Guid Id,Guid InstanceId,string NodeKey,string ConnectorKey,int ConnectorVersion,
    string ModeKey,string StatusKey,long Revision,int AttemptCount,int ApplyAttemptCount,string? ErrorKey,long InputFormRevision,
    long? AppliedFormRevision,bool CancellationRequested,bool RequiresReconciliation,DateTimeOffset CreatedAtUtc,DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<WorkflowConnectorAttemptResponse> Attempts);
/// <summary>单次连接器尝试记录：以租约代次与状态暴露执行轨迹、HTTP 状态码及响应摘要，不返回原始请求体或完整响应载荷。</summary>
public sealed record WorkflowConnectorAttemptResponse(Guid Id,long LeaseGeneration,string StatusKey,string? OutcomeKey,
    int? HttpStatusCode,string? ErrorKey,string? ResponseHash,DateTimeOffset StartedAtUtc,DateTimeOffset? CompletedAtUtc);
/// <summary>连接器调用分页视图：按页返回运维可见调用项与稳定总数，供审计分页浏览。</summary>
public sealed record WorkflowConnectorCallPageResponse(IReadOnlyList<WorkflowConnectorCallResponse> Items,int Page,int PageSize,int Total);

/// <summary>对账不能伪造响应、Actor或Tenant；业务载荷及原输入始终来自原激活。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReconcileWorkflowConnectorRequest(long ExpectedInstanceRevision,long ExpectedRevision,string ActionKey,
    Guid? AttemptId,string? EvidenceReference,string Reason,string IdempotencyKey);
/// <summary>不可变对账回执不表示已自动恢复实例或撤销远端副作用。</summary>
public sealed record WorkflowConnectorResolutionResponse(Guid Id,Guid CallId,Guid InstanceId,string ActionKey,string StatusKey,
    long Revision,string InstanceStatusKey,long InstanceRevision,DateTimeOffset CreatedAtUtc);
