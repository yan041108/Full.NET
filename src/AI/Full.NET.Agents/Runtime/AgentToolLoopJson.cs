using System.Text.Json;
using System.Text.Json.Serialization;

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
internal sealed partial class AgentToolLoopJsonSerializerContext : JsonSerializerContext;