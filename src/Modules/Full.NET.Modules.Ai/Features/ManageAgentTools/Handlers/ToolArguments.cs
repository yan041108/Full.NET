using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

/// <summary>固定分页参数，不允许携带租户、用户、审批或任意过滤表达式。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ToolPageArguments(
    [property: JsonPropertyName("page")] int Page = 1,
    [property: JsonPropertyName("pageSize")] int PageSize = 20);

/// <summary>Ping 只返回固定健康标记。</summary>
internal sealed record ToolPingResult([property: JsonPropertyName("ok")] bool Ok);

/// <summary>模型目录不携带端点、凭据或连接测试原文。</summary>
internal sealed record ToolModelItem(Guid Id, string Name, string ProviderKey, string ModelId);

/// <summary>参数采用源生成类型解析，并显式拒绝重复 JSON 属性。</summary>
internal static class ToolArguments
{
    internal static ToolPageArguments? Parse(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject()) if (!names.Add(property.Name)) return null;
        try
        {
            var value = arguments.Deserialize(AiToolJsonSerializerContext.Default.ToolPageArguments);
            return value is { Page: >= 1 and <= 1000000, PageSize: >= 1 and <= 100 } ? value : null;
        }
        catch (JsonException) { return null; }
    }
}
