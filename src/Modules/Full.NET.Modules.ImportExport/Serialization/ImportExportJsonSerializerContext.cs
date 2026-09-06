using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.ImportExport.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StaticImportSchemaDefinition))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportSchemaDefinition>))]
[JsonSerializable(typeof(ImportExportTaskResponse))]
[JsonSerializable(typeof(ImportExportTaskDetailResponse))]
[JsonSerializable(typeof(PagedResult<ImportExportTaskResponse>))]
[JsonSerializable(typeof(StaticImportRowPreviewResult))]
[JsonSerializable(typeof(IReadOnlyList<StaticImportRowPreviewResult>))]
internal partial class ImportExportJsonSerializerContext : JsonSerializerContext;
