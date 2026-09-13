using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.AgenticWeb.Mcp;

/// <summary>MCP 模块 JSON 源生成上下文；避免 Native AOT 下反射序列化。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(McpProtectedResourceMetadataResponse))]
internal sealed partial class McpJsonSerializerContext : JsonSerializerContext;

/// <summary>OAuth 受保护资源元数据响应；字段名与 MCP 互操作约定一致。</summary>
internal sealed record McpProtectedResourceMetadataResponse(
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("authorization_servers")] string[] AuthorizationServers,
    [property: JsonPropertyName("bearer_methods_supported")] string[] BearerMethodsSupported);