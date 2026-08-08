using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DictTypeResponse))]
[JsonSerializable(typeof(PagedResult<DictTypeResponse>))]
[JsonSerializable(typeof(IReadOnlyList<DictTypeResponse>))]
[JsonSerializable(typeof(CreateDictTypeRequest))]
[JsonSerializable(typeof(UpdateDictTypeRequest))]
[JsonSerializable(typeof(DeleteDictTypeRequest))]
[JsonSerializable(typeof(DictItemResponse))]
[JsonSerializable(typeof(PagedResult<DictItemResponse>))]
[JsonSerializable(typeof(IReadOnlyList<DictItemResponse>))]
[JsonSerializable(typeof(CreateDictItemRequest))]
[JsonSerializable(typeof(UpdateDictItemRequest))]
[JsonSerializable(typeof(DeleteDictItemRequest))]
[JsonSerializable(typeof(ConfigEntryResponse))]
[JsonSerializable(typeof(PagedResult<ConfigEntryResponse>))]
[JsonSerializable(typeof(IReadOnlyList<ConfigEntryResponse>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(CreateConfigEntryRequest))]
[JsonSerializable(typeof(UpdateConfigEntryRequest))]
[JsonSerializable(typeof(DeleteConfigEntryRequest))]
[JsonSerializable(typeof(BatchDeleteConfigEntriesRequest))]
[JsonSerializable(typeof(BatchUpdateConfigValuesRequest))]
[JsonSerializable(typeof(ConfigValueUpdate))]
[JsonSerializable(typeof(EnumCatalogSummary))]
[JsonSerializable(typeof(IReadOnlyList<EnumCatalogSummary>))]
[JsonSerializable(typeof(EnumCatalogDetail))]
[JsonSerializable(typeof(EnumCatalogMember))]
[JsonSerializable(typeof(GridColumnPreference[]))]
[JsonSerializable(typeof(UpdateGridPreferenceRequest))]
[JsonSerializable(typeof(GridPreferenceResponse))]
[JsonSerializable(typeof(DiagnosticPolicyResponse))]
[JsonSerializable(typeof(UpdateDiagnosticPolicyRequest))]
[JsonSerializable(typeof(RestoreDiagnosticPolicyRequest))]
internal partial class SettingsJsonSerializerContext : JsonSerializerContext;
