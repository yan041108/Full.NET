using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Mcp.Serialization;

[JsonSerializable(typeof(McpChatSessionSummary))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AiMcpJsonSerializerContext : JsonSerializerContext;
