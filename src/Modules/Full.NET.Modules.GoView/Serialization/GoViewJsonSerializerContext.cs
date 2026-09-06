using System.Text.Json.Serialization;
using Full.NET.Modules.GoView.Contracts;

namespace Full.NET.Modules.GoView.Serialization;

/// <summary>GoView 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(GoViewProjectResponse))]
[JsonSerializable(typeof(CreateGoViewProjectRequest))]
[JsonSerializable(typeof(UpdateGoViewProjectRequest))]
[JsonSerializable(typeof(PublishGoViewProjectRequest))]
[JsonSerializable(typeof(GoViewProjectVersionResponse))]
[JsonSerializable(typeof(IReadOnlyList<GoViewProjectResponse>))]
[JsonSerializable(typeof(IReadOnlyList<GoViewProjectVersionResponse>))]
[JsonSerializable(typeof(PreviewGoViewProjectRequest))]
[JsonSerializable(typeof(GoViewProjectPreviewResponse))]
internal partial class GoViewJsonSerializerContext : JsonSerializerContext;
