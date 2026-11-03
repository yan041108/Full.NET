using System.Resources;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.Ai.Resources;

/// <summary>编排目标和提案错误按稳定机器码本地化，权限判断不依赖译文。</summary>
internal sealed class AiAgentRunErrorResourceSource() : ResourceManagerErrorResourceSource(
    "ai.agent_run.",
    new ResourceManager("Full.NET.Modules.Ai.Resources.AiAgentRunErrors", typeof(AiAgentRunErrorResourceSource).Assembly));
