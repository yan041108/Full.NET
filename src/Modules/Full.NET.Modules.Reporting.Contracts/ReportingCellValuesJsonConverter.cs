using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>保留报表单元格的稳定列键；HTTP 的字典命名策略不能改变 Columns 中的机器码。</summary>
public sealed class ReportingCellValuesJsonConverter : JsonConverter<IReadOnlyDictionary<string, string?>>
{
    /// <summary>null 字典值属于损坏契约，必须由转换器明确拒绝。</summary>
    public override bool HandleNull => true;

    /// <summary>按原始列键读取字符串或 null，不接受结构化单元格与重复列键。</summary>
    public override IReadOnlyDictionary<string, string?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Reporting cell values must be an object.");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return values;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("A reporting column key is required.");
            var key = reader.GetString()!;
            if (!reader.Read() || reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
                throw new JsonException("Reporting cells must be strings or null.");
            if (!values.TryAdd(key, reader.GetString())) throw new JsonException("Duplicate reporting column key.");
        }
        throw new JsonException("The reporting cell values object is incomplete.");
    }

    /// <summary>直接写入原始列键与文本值，避免全局 DictionaryKeyPolicy 改写字段身份。</summary>
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, string?> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        foreach (var cell in value) writer.WriteString(cell.Key, cell.Value);
        writer.WriteEndObject();
    }
}
