using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Regions.Contracts;

namespace Full.NET.Modules.Regions.Serialization;

/// <summary>行政区域模块 JSON 源生成上下文。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AdministrativeRegionResponse))]
[JsonSerializable(typeof(AdministrativeRegionChildResponse))]
[JsonSerializable(typeof(AdministrativeRegionTreeNodeResponse))]
[JsonSerializable(typeof(AdministrativeRegionDatasetManifestResponse))]
[JsonSerializable(typeof(CreateAdministrativeRegionRequest))]
[JsonSerializable(typeof(UpdateAdministrativeRegionRequest))]
[JsonSerializable(typeof(DeleteAdministrativeRegionRequest))]
[JsonSerializable(typeof(ImportAdministrativeRegionsRequest))]
[JsonSerializable(typeof(ImportAdministrativeRegionItem))]
[JsonSerializable(typeof(ImportAdministrativeRegionsPreviewResponse))]
[JsonSerializable(typeof(ImportAdministrativeRegionsApplyResponse))]
[JsonSerializable(typeof(PagedResult<AdministrativeRegionResponse>))]
[JsonSerializable(typeof(IReadOnlyList<AdministrativeRegionChildResponse>))]
[JsonSerializable(typeof(IReadOnlyList<AdministrativeRegionTreeNodeResponse>))]
internal partial class RegionsJsonSerializerContext : JsonSerializerContext;
