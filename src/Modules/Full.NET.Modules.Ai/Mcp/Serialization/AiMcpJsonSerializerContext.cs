using System.Text.Json.Serialization;

// Ai 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
namespace Full.NET.Modules.Ai.Mcp.Serialization;

[JsonSerializable(typeof(McpChatSessionSummary))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
/// <summary>Ai MCP 子模块 JSON 源生成上下文；仅注册已审计的 MCP 会话契约。</summary>
internal sealed partial class AiMcpJsonSerializerContext : JsonSerializerContext;
