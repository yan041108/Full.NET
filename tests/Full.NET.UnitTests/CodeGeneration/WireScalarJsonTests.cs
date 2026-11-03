using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Hosting.Serialization;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>通过源生成 JSON 上下文读取实际线格式，拒绝可能被 .NET 解析器静默舍入的输入。</summary>
[TestClass]
public sealed class WireScalarJsonTests
{
    [TestMethod]
    public void Maximum_integer_decimal_and_microsecond_time_round_trip_as_strings_and_nulls()
    {
        const string json = """{"quantity":"9223372036854775807","price":"12345678901234.5678","time":"2026-10-26T20:00:00.123456+08:00","optionalQuantity":null,"optionalPrice":null,"optionalTime":null}""";
        var value = JsonSerializer.Deserialize(json, WireScalarTestContext.Default.WireScalarTestValue)!;
        Assert.AreEqual(long.MaxValue, value.Quantity);
        Assert.AreEqual(12345678901234.5678m, value.Price);
        Assert.AreEqual(TimeSpan.Zero, value.Time.Offset);
        var output = JsonSerializer.Serialize(value, WireScalarTestContext.Default.WireScalarTestValue);
        using var parsed = JsonDocument.Parse(output);
        Assert.AreEqual(JsonValueKind.String, parsed.RootElement.GetProperty("quantity").ValueKind);
        Assert.AreEqual("12345678901234.5678", parsed.RootElement.GetProperty("price").GetString());
        Assert.AreEqual(value, JsonSerializer.Deserialize(output, WireScalarTestContext.Default.WireScalarTestValue));
    }

    [TestMethod]
    [DataRow("1.000000000000000000000000000001")]
    [DataRow("0.00000000000000000000000000001")]
    [DataRow("79228162514264337593543950336")]
    [DataRow("1e2")]
    [DataRow(" 1.00")]
    public void Decimal_text_that_cannot_round_trip_exactly_is_rejected(string text)
    {
        var json = """{"quantity":"1","price":"VALUE","time":"2026-10-26T00:00:00Z"}""".Replace("VALUE", text, StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json, WireScalarTestContext.Default.WireScalarTestValue));
    }

    [TestMethod]
    [DataRow("{\"quantity\":1,\"price\":\"1.00\",\"time\":\"2026-10-26T00:00:00Z\"}")]
    [DataRow("{\"quantity\":\"1\",\"price\":1.00,\"time\":\"2026-10-26T00:00:00Z\"}")]
    [DataRow("{\"quantity\":\"9223372036854775808\",\"price\":\"1.00\",\"time\":\"2026-10-26T00:00:00Z\"}")]
    [DataRow("{\"quantity\":\"1\",\"price\":\"1.00\"}")]
    [DataRow("{\"quantity\":\"1\",\"price\":\"1.00\",\"time\":\"2026-10-26T00:00:00.00000001Z\"}")]
    [DataRow("{\"quantity\":\"1\",\"price\":\"1.00\",\"time\":\"2026-10-26T00:00:00\"}")]
    public void Numeric_JSON_missing_required_time_or_unrepresentable_timestamp_is_rejected(string json) =>
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json, WireScalarTestContext.Default.WireScalarTestValue));
}

internal sealed record WireScalarTestValue(
    [property: JsonRequired, JsonConverter(typeof(StringInt64JsonConverter))] long Quantity,
    [property: JsonRequired, JsonConverter(typeof(StringDecimalJsonConverter))] decimal Price,
    [property: JsonRequired, JsonConverter(typeof(UtcDateTimeJsonConverter))] DateTimeOffset Time,
    [property: JsonConverter(typeof(NullableStringInt64JsonConverter))] long? OptionalQuantity,
    [property: JsonConverter(typeof(NullableStringDecimalJsonConverter))] decimal? OptionalPrice,
    [property: JsonConverter(typeof(NullableUtcDateTimeJsonConverter))] DateTimeOffset? OptionalTime);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WireScalarTestValue))]
internal partial class WireScalarTestContext : JsonSerializerContext;
