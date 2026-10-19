using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Auditing.Contracts;

// Auditing 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
namespace Full.NET.Modules.Auditing.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Features.QueryLogTimeline.LogTimelineResponse))]
[JsonSerializable(typeof(AccessLogResponse))]
[JsonSerializable(typeof(PagedResult<AccessLogResponse>))]
[JsonSerializable(typeof(AccessLogCursorPageResponse))]
[JsonSerializable(typeof(OperationLogResponse))]
[JsonSerializable(typeof(OperationLogDetailsContextV1))]
[JsonSerializable(typeof(OperationLogExportRequestSummaryV1))]
[JsonSerializable(typeof(OperationLogExportResponseSummaryV1))]
[JsonSerializable(typeof(OperationLogDetailsResponse))]
[JsonSerializable(typeof(PagedResult<OperationLogResponse>))]
[JsonSerializable(typeof(ExceptionLogResponse))]
[JsonSerializable(typeof(PagedResult<ExceptionLogResponse>))]
[JsonSerializable(typeof(OutboundCallLogResponse))]
[JsonSerializable(typeof(PagedResult<OutboundCallLogResponse>))]
[JsonSerializable(typeof(OutboundCallAuditProbeRequest))]
[JsonSerializable(typeof(OutboundCallAuditRequest))]
[JsonSerializable(typeof(AuditLogTrendResponse))]
[JsonSerializable(typeof(AuditLogTrendBucketResponse))]
[JsonSerializable(typeof(DomainChangeDiffQueryResponse))]
[JsonSerializable(typeof(DomainAuditChangeDiffEntryResponse))]
[JsonSerializable(typeof(DomainAuditChangeDiffFieldResponse))]
[JsonSerializable(typeof(AuditLogExportRequest))]
[JsonSerializable(typeof(AuditLogExportMetadataResponse))]
/// <summary>Auditing 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class AuditingJsonSerializerContext : JsonSerializerContext;
