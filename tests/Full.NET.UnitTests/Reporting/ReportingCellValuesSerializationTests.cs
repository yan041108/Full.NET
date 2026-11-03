using System.Text.Json;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Serialization;

namespace Full.NET.UnitTests.Reporting;

/// <summary>源生成序列化不能把结果字典的机器码改成另一组列键。</summary>
[TestClass]
public sealed class ReportingCellValuesSerializationTests
{
    private static ReportingJsonSerializerContext Context() => new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    });

    [TestMethod]
    public void Column_keys_preserve_case_null_and_unicode_under_http_camel_case_options()
    {
        var row = new ReportingExecutionRow(new Dictionary<string, string?>
        { ["EngineVersion"] = "2026.1", ["engineVersion"] = "distinct", ["SchemaName"] = null, ["字段-Key"] = "文字" });
        var json = JsonSerializer.Serialize(row, Context().ReportingExecutionRow);
        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("values");
        Assert.AreEqual("2026.1", values.GetProperty("EngineVersion").GetString());
        Assert.AreEqual("distinct", values.GetProperty("engineVersion").GetString());
        Assert.AreEqual(JsonValueKind.Null, values.GetProperty("SchemaName").ValueKind);
        Assert.AreEqual("文字", values.GetProperty("字段-Key").GetString());
        Assert.AreEqual(4, values.EnumerateObject().Count());
    }

    [TestMethod]
    public void Source_generated_round_trip_preserves_literal_cell_content()
    {
        var row = new ReportingExecutionRow(new Dictionary<string, string?> { ["EngineVersion"] = "<script>literal</script>" });
        var context = Context(); var json = JsonSerializer.Serialize(row, context.ReportingExecutionRow);
        var read = JsonSerializer.Deserialize(json, context.ReportingExecutionRow)!;
        Assert.AreEqual("<script>literal</script>", read.Values["EngineVersion"]);
        Assert.IsFalse(read.Values.ContainsKey("engineVersion"));
    }

    [TestMethod]
    [DataRow("{\"values\":null}")]
    [DataRow("{\"values\":[]}")]
    [DataRow("{\"values\":{\"EngineVersion\":1}}")]
    [DataRow("{\"values\":{\"EngineVersion\":{}}}")]
    [DataRow("{\"values\":{\"EngineVersion\":\"a\",\"EngineVersion\":\"b\"}}")]
    public void Invalid_cell_shape_or_duplicate_key_is_rejected(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json, Context().ReportingExecutionRow));
    }
}
