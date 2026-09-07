using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Serialization;

/// <summary>AI 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(AiModelConfigListItem))]
[JsonSerializable(typeof(AiModelConfigResponse))]
[JsonSerializable(typeof(CreateAiModelConfigRequest))]
[JsonSerializable(typeof(UpdateAiModelConfigRequest))]
[JsonSerializable(typeof(TestAiModelConfigResult))]
[JsonSerializable(typeof(PagedResult<AiModelConfigListItem>))]
[JsonSerializable(typeof(AiTenantQuotaListItem))]
[JsonSerializable(typeof(AiTenantQuotaResponse))]
[JsonSerializable(typeof(UpdateAiTenantQuotaRequest))]
[JsonSerializable(typeof(PagedResult<AiTenantQuotaListItem>))]
[JsonSerializable(typeof(AiChatSessionListItem))]
[JsonSerializable(typeof(AiChatSessionResponse))]
[JsonSerializable(typeof(AiChatMessageResponse))]
[JsonSerializable(typeof(CreateAiChatSessionRequest))]
[JsonSerializable(typeof(UpdateAiChatSessionRequest))]
[JsonSerializable(typeof(StreamAiChatMessageRequest))]
[JsonSerializable(typeof(PagedResult<AiChatSessionListItem>))]
[JsonSerializable(typeof(AiAgentToolCatalogItem))]
[JsonSerializable(typeof(AiAgentToolCallListItem))]
[JsonSerializable(typeof(PagedResult<AiAgentToolCallListItem>))]
[JsonSerializable(typeof(AiChatStreamDeltaEvent))]
[JsonSerializable(typeof(AiChatStreamDoneEvent))]
[JsonSerializable(typeof(AiChatStreamErrorEvent))]
internal partial class AiJsonSerializerContext : JsonSerializerContext;
