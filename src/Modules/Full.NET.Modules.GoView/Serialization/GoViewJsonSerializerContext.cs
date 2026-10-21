using Full.NET.Abstractions.Results;
using System.Text.Json.Serialization;
using Full.NET.Modules.GoView.Contracts;

namespace Full.NET.Modules.GoView.Serialization;

/// <summary>GoView 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(PagedResult<GoViewProjectSummaryResponse>))]
[JsonSerializable(typeof(PagedResult<GoViewProjectVersionSummaryResponse>))]
[JsonSerializable(typeof(GoViewProjectResponse))]
[JsonSerializable(typeof(CreateGoViewProjectRequest))]
[JsonSerializable(typeof(UpdateGoViewProjectRequest))]
[JsonSerializable(typeof(PublishGoViewProjectRequest))]
[JsonSerializable(typeof(GoViewProjectVersionResponse))]
[JsonSerializable(typeof(IReadOnlyList<GoViewProjectResponse>))]
[JsonSerializable(typeof(IReadOnlyList<GoViewProjectVersionResponse>))]
[JsonSerializable(typeof(PreviewGoViewProjectRequest))]
[JsonSerializable(typeof(GoViewProjectPreviewResponse))]
/// <summary>GoView 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class GoViewJsonSerializerContext : JsonSerializerContext;
