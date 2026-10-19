using System.Text.Json;
using System.Text.Json.Serialization;

// Agent 工具循环的 AOT 友好 JSON 契约；类型仅用于序列化边界，不对外暴露 HTTP。
namespace Full.NET.Agents.Runtime;

internal sealed record ToolLoopDeniedPayload(string StatusKey, string ErrorCode);

internal sealed record ToolLoopExecutionPayload(
    string StatusKey,
    string? ErrorCode,
    JsonElement? Value,
    bool IsUntrusted);

internal sealed record AgentSessionMessageEntry(string Role, string? Text, int Contents);

internal sealed record AgentSessionSnapshot(List<AgentSessionMessageEntry> Messages);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ToolLoopDeniedPayload))]
[JsonSerializable(typeof(ToolLoopExecutionPayload))]
[JsonSerializable(typeof(AgentSessionSnapshot))]
[JsonSerializable(typeof(AgentSessionMessageEntry))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
/// <summary>Agent 工具循环 JSON 源生成上下文；仅用于运行时序列化边界。</summary>
internal sealed partial class AgentToolLoopJsonSerializerContext : JsonSerializerContext;