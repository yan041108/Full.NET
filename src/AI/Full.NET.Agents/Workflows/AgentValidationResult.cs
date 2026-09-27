using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Agents.Workflows;

/// <summary>模型校验节点的封闭结果；不允许自由文本或附加字段扩大通过语义。</summary>
internal sealed record AgentValidationResult([property: JsonPropertyName("decision")] string Decision)
{
    internal static bool IsApproved(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256)
            return false;

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            // 重复字段即使值相同也拒绝，避免不同 JSON 消费者采用不同的覆盖顺序。
            var properties = document.RootElement.EnumerateObject();
            if (!properties.MoveNext() || !properties.Current.NameEquals("decision")
                || properties.Current.Value.ValueKind != JsonValueKind.String || properties.MoveNext())
                return false;

            var result = JsonSerializer.Deserialize(document.RootElement, AgentWorkflowJsonContext.Default.AgentValidationResult);
            return string.Equals(result?.Decision, "approved", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
