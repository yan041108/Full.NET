using System.Text.Json.Serialization;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Serialization;

/// <summary>持久化来源协议独立冻结命名与字段，不依赖 HTTP 宿主序列化选项。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AiKnowledgeFaqContent))]
[JsonSerializable(typeof(AiKnowledgeFaqTransferPackage))]
[JsonSerializable(typeof(AiKnowledgeFaqTransferItem))]
[JsonSerializable(typeof(AiKnowledgeFaqProvenance))]
[JsonSerializable(typeof(AiKnowledgeFaqGraph))]
[JsonSerializable(typeof(AiKnowledgeFaqParentAuthorization[]))]
[JsonSerializable(typeof(AiKnowledgeFaqDraftBinding[]))]
[JsonSerializable(typeof(AiKnowledgeFaqPublicationSnapshot))]
internal partial class AiKnowledgeFaqJsonContext : JsonSerializerContext;
