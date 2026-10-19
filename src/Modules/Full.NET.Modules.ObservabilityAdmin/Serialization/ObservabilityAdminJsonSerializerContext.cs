// ObservabilityAdmin 模块 JSON 源生成上下文；Native AOT 绑定契约类型，避免反射序列化。
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;

namespace Full.NET.Modules.ObservabilityAdmin.Serialization;

/// <summary>为 Host 日志控制面提供 Native AOT 静态 JSON 元数据。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LogFileSummary))]
[JsonSerializable(typeof(LogFileList))]
[JsonSerializable(typeof(IReadOnlyList<LogFileSummary>))]
[JsonSerializable(typeof(LogFileTail))]
[JsonSerializable(typeof(ServerInstanceCatalogEntry))]
[JsonSerializable(typeof(IReadOnlyList<ServerInstanceCatalogEntry>))]
[JsonSerializable(typeof(ServerRuntimeMetric))]
[JsonSerializable(typeof(ServerRuntimeSnapshot))]
[JsonSerializable(typeof(CachePolicySummary))]
[JsonSerializable(typeof(IReadOnlyList<CachePolicySummary>))]
[JsonSerializable(typeof(CacheInvalidationOperationSummary))]
[JsonSerializable(typeof(CacheInvalidationParameterSummary))]
[JsonSerializable(typeof(CacheInvalidationRequest))]
[JsonSerializable(typeof(CacheInvalidationResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ElasticsearchLogPipelineHealthResponse))]
/// <summary>ObservabilityAdmin 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class ObservabilityAdminJsonSerializerContext
    : JsonSerializerContext;
