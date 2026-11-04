using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>保持报表结果键与列机器码精确一致，不受宿主字典 CamelCase 策略影响。</summary>
/// <remarks>仅用于 <see cref="ReportingExecutionRow.Values"/>；静态转换字符串或空单元格，不调用反射序列化。</remarks>
public sealed class ReportingResultValuesJsonConverter : JsonConverter<IReadOnlyDictionary<string, string?>>
{
    /// <summary>空单元格合法，但整个结果字典不能为空。</summary>
    public override bool HandleNull => true;

    /// <summary>读取按原始列键索引的文本单元格，拒绝重复列、null 字典和非文本值。</summary>
    /// <param name="reader">定位到结果字典的 JSON 读取器。</param>
    /// <param name="typeToConvert">静态结果字典类型。</param>
    /// <param name="options">宿主选项；不将字典命名策略应用到列机器码。</param>
    /// <returns>按 Ordinal 区分列键大小写的结果字典。</returns>
    /// <exception cref="JsonException">字典结构或单元格类型非法。</exception>
    public override IReadOnlyDictionary<string, string?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A reporting result dictionary is required.");

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return values;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Invalid reporting result column.");
            var key = reader.GetString()!;
            if (!reader.Read() || reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
                throw new JsonException("Reporting result cells must be text or null.");
            var value = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            if (!values.TryAdd(key, value))
                throw new JsonException("Duplicate reporting result column.");
        }
        throw new JsonException("The reporting result dictionary is incomplete.");
    }

    /// <summary>按原始列键写入文本单元格；对象属性仍由宿主使用既有 CamelCase 策略。</summary>
    /// <param name="writer">当前 JSON 写入器。</param>
    /// <param name="value">与列定义键逐字匹配的结果字典。</param>
    /// <param name="options">宿主选项；列键不执行命名转换。</param>
    /// <exception cref="JsonException">结果字典为 null。</exception>
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, string?> value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("A reporting result dictionary is required.");
        writer.WriteStartObject();
        foreach (var cell in value)
        {
            // 列键是机器码，CamelCase 会造成查找失败，甚至将大小写不同的列压成重复键。
            writer.WritePropertyName(cell.Key);
            if (cell.Value is null) writer.WriteNullValue();
            else writer.WriteStringValue(cell.Value);
        }
        writer.WriteEndObject();
    }
}
