using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Hosting.Serialization;

/// <summary>无损解析字符串数值与带时区 ISO 时间；用于声明字符串线格式的生成契约和查询输入。</summary>
public static class FullNetWireScalars
{
    /// <summary>只接受十进制规范整数字符串，拒绝科学计数法、空白与超出 Int64 的值。</summary>
    public static bool TryInt64(string? text, out long value)
    {
        value = default;
        return IsDecimalSyntax(text, allowFraction: false) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>解析后重新核对精确十进制值，拒绝 decimal.TryParse 对超长尾数的静默舍入。</summary>
    public static bool TryDecimal(string? text, out decimal value)
    {
        value = default;
        if (!IsDecimalSyntax(text, allowFraction: true) || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value)) return false;
        var source = text!;
        var negative = source[0] == '-';
        var unsigned = negative ? source[1..] : source;
        var dot = unsigned.IndexOf('.');
        var integer = dot < 0 ? unsigned : unsigned[..dot];
        var fraction = dot < 0 ? string.Empty : unsigned[(dot + 1)..].TrimEnd('0');
        var normalized = (negative && (integer != "0" || fraction.Length > 0) ? "-" : string.Empty)
            + integer + (fraction.Length == 0 ? string.Empty : "." + fraction);
        return normalized == value.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    /// <summary>要求显式时区和不超过七位小数，保留 .NET 时间精度并统一为 UTC。</summary>
    public static bool TryUtcDateTime(string? text, out DateTimeOffset value)
    {
        value = default;
        if (text is null || text.Length is < 20 or > 40) return false;
        var hasZone = text.EndsWith('Z') || (text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':');
        if (!hasZone || !DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return false;
        value = value.ToUniversalTime();
        return true;
    }

    private static bool IsDecimalSyntax(string? text, bool allowFraction)
    {
        if (text is null || text.Length is < 1 or > 128) return false;
        var start = text[0] == '-' ? 1 : 0;
        if (start == text.Length) return false;
        var dot = text.IndexOf('.', start);
        if (dot >= 0 && (!allowFraction || dot == start || dot == text.Length - 1)) return false;
        var integerLength = (dot < 0 ? text.Length : dot) - start;
        if (integerLength > 1 && text[start] == '0') return false;
        for (var index = start; index < text.Length; index++)
            if (index != dot && text[index] is not (>= '0' and <= '9')) return false;
        return true;
    }
}

/// <summary>Int64 按字符串传输，避免浏览器 Number 提前丢失有效位。</summary>
public sealed class StringInt64JsonConverter : JsonConverter<long>
{
    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && FullNetWireScalars.TryInt64(reader.GetString(), out var value) ? value : throw new JsonException("Int64 must be an exact decimal string.");
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>可空 Int64 保留 null，不将空字符串或 JSON 数字当作字符串线格式。</summary>
public sealed class NullableStringInt64JsonConverter : JsonConverter<long?>
{
    /// <inheritdoc />
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Null
        ? null : reader.TokenType == JsonTokenType.String && FullNetWireScalars.TryInt64(reader.GetString(), out var value) ? value : throw new JsonException("Int64 must be an exact decimal string.");
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    { if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture)); }
}

/// <summary>Decimal 字符串必须无损表示 .NET decimal；禁止读取时静默舍入。</summary>
public sealed class StringDecimalJsonConverter : JsonConverter<decimal>
{
    /// <inheritdoc />
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && FullNetWireScalars.TryDecimal(reader.GetString(), out var value) ? value : throw new JsonException("Decimal must be an exact decimal string.");
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>可空 Decimal 只额外允许 null，沿用相同精确解析规则。</summary>
public sealed class NullableStringDecimalJsonConverter : JsonConverter<decimal?>
{
    /// <inheritdoc />
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Null
        ? null : reader.TokenType == JsonTokenType.String && FullNetWireScalars.TryDecimal(reader.GetString(), out var value) ? value : throw new JsonException("Decimal must be an exact decimal string.");
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    { if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture)); }
}

/// <summary>UTC 时间只接受精度受控的 ISO 时间，响应保留可表示的全部时间精度。</summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && FullNetWireScalars.TryUtcDateTime(reader.GetString(), out var value) ? value : throw new JsonException("DateTime must be an exact ISO timestamp with a zone.");
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>可空 UTC 时间保留 null，禁止借空串隐式创建默认日期。</summary>
public sealed class NullableUtcDateTimeJsonConverter : JsonConverter<DateTimeOffset?>
{
    /// <inheritdoc />
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Null
        ? null : reader.TokenType == JsonTokenType.String && FullNetWireScalars.TryUtcDateTime(reader.GetString(), out var value) ? value : throw new JsonException("DateTime must be an exact ISO timestamp with a zone.");
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    { if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); }
}
