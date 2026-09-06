using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.K3Cloud.Contracts;

namespace Full.NET.Modules.K3Cloud.Serialization;

/// <summary>K3Cloud 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(K3CloudConnectionConfigResponse))]
[JsonSerializable(typeof(CreateK3CloudConnectionConfigRequest))]
[JsonSerializable(typeof(UpdateK3CloudConnectionConfigRequest))]
[JsonSerializable(typeof(TestK3CloudConnectionConfigResult))]
[JsonSerializable(typeof(IReadOnlyList<K3CloudConnectionConfigResponse>))]
[JsonSerializable(typeof(K3CloudDocumentSyncResponse))]
[JsonSerializable(typeof(CreateK3CloudDocumentSyncRequest))]
[JsonSerializable(typeof(PagedResult<K3CloudDocumentSyncResponse>))]
internal partial class K3CloudJsonSerializerContext : JsonSerializerContext;
