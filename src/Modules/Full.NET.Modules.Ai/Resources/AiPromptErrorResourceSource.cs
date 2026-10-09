using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Resources;

/// <summary>按机器码本地化 Prompt 错误，不把译文用于业务判断。</summary>
internal sealed class AiPromptErrorResourceSource() : ResourceManagerErrorResourceSource(
    AiPromptErrorCodes.Prefix,
    new ResourceManager("Full.NET.Modules.Ai.Resources.AiPromptErrors", typeof(AiPromptErrorResourceSource).Assembly));
