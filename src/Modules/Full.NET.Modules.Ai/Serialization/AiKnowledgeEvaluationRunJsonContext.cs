using System.Text.Json.Serialization;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Serialization;

/// <summary>策略、原操作计划与实际输出使用独立静态协议；未知字段和重复字段不作兼容猜测。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,
    GenerationMode=JsonSourceGenerationMode.Metadata,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AiKnowledgeEvaluationStrategyDefinition))]
[JsonSerializable(typeof(AiKnowledgeEvaluationRunPlan))]
[JsonSerializable(typeof(AiKnowledgeEvaluationObservedResult))]
internal partial class AiKnowledgeEvaluationRunJsonContext:JsonSerializerContext;
