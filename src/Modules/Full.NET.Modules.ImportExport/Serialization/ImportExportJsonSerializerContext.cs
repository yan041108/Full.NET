using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;

namespace Full.NET.Modules.ImportExport.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StaticImportSchemaDefinition))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportSchemaDefinition>))]
[JsonSerializable(typeof(ImportExportTaskResponse))]
[JsonSerializable(typeof(ImportExportTaskDetailResponse))]
[JsonSerializable(typeof(PagedResult<ImportExportTaskResponse>))]
[JsonSerializable(typeof(StaticImportRowPreviewResult))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportRowPreviewResult>))]
[JsonSerializable(typeof(StaticImportRowExecutionResult))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportRowExecutionResult>))]
[JsonSerializable(typeof(ImportExportExecutionStateDocument))]
internal partial class ImportExportJsonSerializerContext : JsonSerializerContext;
