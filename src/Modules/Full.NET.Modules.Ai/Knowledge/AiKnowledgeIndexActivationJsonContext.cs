using System.Text.Json.Serialization;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完成回执使用固定静态 JSON 契约，避免在原生闭包内依赖反射序列化。</summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AiKnowledgeIndexVectorReceiptRecord[]))]
internal partial class AiKnowledgeIndexActivationJsonContext : JsonSerializerContext;
