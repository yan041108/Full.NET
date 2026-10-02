using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Resources;

/// <summary>知识库 ProblemDetails 标题按语言呈现，稳定错误码保持不变。</summary>
internal sealed class AiKnowledgeErrorResourceSource() : ResourceManagerErrorResourceSource(
    AiKnowledgeErrorCodes.Prefix, new ResourceManager("Full.NET.Modules.Ai.Resources.AiKnowledgeErrors", typeof(AiKnowledgeErrorResourceSource).Assembly));
