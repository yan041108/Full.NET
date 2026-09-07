using System.Text.Json;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Serialization;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>报表参数 Schema 与布局配置 JSON 序列化辅助。</summary>
internal static class ReportingDefinitionJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly ReportingJsonSerializerContext SerializerContext = new(SerializerOptions);

    /// <summary>序列化参数 Schema。</summary>
    /// <param name="entries">已验证的报表参数定义。</param>
    public static string SerializeParameterSchema(IReadOnlyList<ReportingParameterSchemaEntry> entries) =>
        JsonSerializer.Serialize(entries, SerializerContext.IReadOnlyListReportingParameterSchemaEntry);

    /// <summary>反序列化参数 Schema。</summary>
    /// <param name="json">持久化的 JSON 快照。</param>
    public static IReadOnlyList<ReportingParameterSchemaEntry> DeserializeParameterSchema(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize(json, SerializerContext.IReadOnlyListReportingParameterSchemaEntry) ?? [];
    }

    /// <summary>规范化布局配置 JSON。</summary>
    public static string NormalizeLayoutConfig(string? layoutConfigJson)
    {
        if (string.IsNullOrWhiteSpace(layoutConfigJson))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(layoutConfigJson);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? layoutConfigJson.Trim()
            : throw new JsonException("Layout config must be a JSON object.");
    }
}
