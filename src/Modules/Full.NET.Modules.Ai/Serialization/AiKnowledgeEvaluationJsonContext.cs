using System.Text.Json.Serialization;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Serialization;

/// <summary>持久评估修订使用独立静态闭合协议，不依赖宿主的 JSON 选项。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AiKnowledgeEvaluationBody))]
[JsonSerializable(typeof(AiKnowledgeEvaluationFootprint))]
[JsonSerializable(typeof(AiKnowledgeEvaluationBinding[]))]
[JsonSerializable(typeof(AiKnowledgeEvaluationCandidateEnvelope))]
internal partial class AiKnowledgeEvaluationJsonContext : JsonSerializerContext;
