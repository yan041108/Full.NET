using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.Identity.Contracts;

// ImportExport 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
namespace Full.NET.Modules.ImportExport.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StaticImportSchemaDefinition))]
[JsonSerializable(typeof(ImportColumnMappingRequest))]
[JsonSerializable(typeof(ImportColumnMappingSnapshot))]
[JsonSerializable(typeof(SessionBindingSnapshot))]
[JsonSerializable(typeof(ImportProfileDetail))]
[JsonSerializable(typeof(IReadOnlyList<ImportProfileSummary>))]
[JsonSerializable(typeof(SaveImportProfileRequest))]
[JsonSerializable(typeof(DeleteImportProfileRequest))]
[JsonSerializable(typeof(CancelImportExportTaskRequest))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportSchemaDefinition>))]
[JsonSerializable(typeof(ImportExportTaskResponse))]
[JsonSerializable(typeof(ImportExportTaskDetailResponse))]
[JsonSerializable(typeof(PagedResult<ImportExportTaskResponse>))]
[JsonSerializable(typeof(CursorPage<ImportExportTaskResponse>))]
[JsonSerializable(typeof(StaticImportRowPreviewResult))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportRowPreviewResult>))]
[JsonSerializable(typeof(StaticImportRowExecutionResult))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportRowExecutionResult>))]
[JsonSerializable(typeof(ImportExportExecutionStateDocument))]
/// <summary>ImportExport 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class ImportExportJsonSerializerContext : JsonSerializerContext;
