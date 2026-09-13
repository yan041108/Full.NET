using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

/// <summary>重命名会话参数；不允许携带租户、用户或审批字段。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RenameChatSessionArguments(
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("title")] string Title);

internal static class RenameChatSessionArgumentParser
{
    internal static RenameChatSessionArguments? Parse(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject()) if (!names.Add(property.Name)) return null;
        try
        {
            var value = arguments.Deserialize(AiToolJsonSerializerContext.Default.RenameChatSessionArguments);
            if (value is null || value.SessionId == Guid.Empty) return null;
            var title = value.Title.Trim();
            return string.IsNullOrWhiteSpace(title) || title.Length > 256 ? null : value with { Title = title };
        }
        catch (JsonException) { return null; }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RenameChatSessionResult(
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("title")] string Title);
