using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Resources;

/// <summary>知识库 ProblemDetails 标题按语言呈现，稳定错误码保持不变。</summary>
internal sealed class AiKnowledgeErrorResourceSource() : ResourceManagerErrorResourceSource(
    AiKnowledgeErrorCodes.Prefix, new ResourceManager("Full.NET.Modules.Ai.Resources.AiKnowledgeErrors", typeof(AiKnowledgeErrorResourceSource).Assembly));

/// <summary>评估集独立机器码沿用知识资源包，避免回退为服务内部英文说明。</summary>
internal sealed class AiKnowledgeEvaluationSetErrorResourceSource() : ResourceManagerErrorResourceSource(
    "ai.knowledge_evaluation_sets.", new ResourceManager("Full.NET.Modules.Ai.Resources.AiKnowledgeErrors", typeof(AiKnowledgeEvaluationSetErrorResourceSource).Assembly));

/// <summary>实际运行的稳定错误码按完整前缀登记，不扩大到其他AI模块。</summary>
internal sealed class AiKnowledgeEvaluationRunErrorResourceSource() : ResourceManagerErrorResourceSource(
    "ai.knowledge_evaluation_runs.", new ResourceManager("Full.NET.Modules.Ai.Resources.AiKnowledgeErrors", typeof(AiKnowledgeEvaluationRunErrorResourceSource).Assembly));
