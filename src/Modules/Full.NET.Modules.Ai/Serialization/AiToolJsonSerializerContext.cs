using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

namespace Full.NET.Modules.Ai.Serialization;

/// <summary>工具 JSON 固定 camelCase 与拒绝额外字段，不依赖 HTTP 选项。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ToolPageArguments))]
[JsonSerializable(typeof(ToolPingResult))]
[JsonSerializable(typeof(ToolModelList))]
[JsonSerializable(typeof(PagedResult<AiChatSessionListItem>))]
[JsonSerializable(typeof(RenameChatSessionArguments))]
[JsonSerializable(typeof(RenameChatSessionResult))]
internal partial class AiToolJsonSerializerContext : JsonSerializerContext;
