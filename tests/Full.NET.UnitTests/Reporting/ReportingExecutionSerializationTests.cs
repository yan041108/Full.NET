using System.Text.Json;
using System.Text.Json.Schema;
using Full.NET.Hosting.Serialization;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingExecutionSerializationTests
{
    [TestMethod]
    public void Http_generated_serialization_preserves_ordinal_result_keys_and_null_cells()
    {
        using var services = new ServiceCollection().AddFullNetJson().BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        options.TypeInfoResolverChain.Insert(0, ReportingJsonSerializerContext.Default);
        var row = new ReportingExecutionRow(new Dictionary<string, string?>
        {
            ["EngineVersion"] = "16.0.4135.4", ["engineVersion"] = "case-sensitive", ["NULLValue"] = null,
        });

        var json = JsonSerializer.Serialize(row, options);
        var restored = JsonSerializer.Deserialize<ReportingExecutionRow>(json, options)!;

        Assert.AreEqual(JsonNamingPolicy.CamelCase, options.DictionaryKeyPolicy);
        Assert.AreEqual("{\"values\":{\"EngineVersion\":\"16.0.4135.4\",\"engineVersion\":\"case-sensitive\",\"NULLValue\":null}}", json);
        Assert.AreEqual(3, restored.Values.Count);
        Assert.AreEqual("16.0.4135.4", restored.Values["EngineVersion"]);
        Assert.AreEqual("case-sensitive", restored.Values["engineVersion"]);
        Assert.IsNull(restored.Values["NULLValue"]);
        var schema = options.GetJsonSchemaAsNode(typeof(ReportingExecutionRow));
        var cellSchema = schema["properties"]?["values"]?["additionalProperties"];
        Assert.IsNotNull(cellSchema, "Result dictionary schema must retain its typed cell contract.");
        var cellTypes = cellSchema["type"]!.AsArray().Select(type => type!.GetValue<string>()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "string", "null" }, cellTypes);
    }

    [TestMethod]
    [DataRow("{\"values\":null}")]
    [DataRow("{\"values\":{\"EngineVersion\":\"first\",\"EngineVersion\":\"second\"}}")]
    [DataRow("{\"values\":{\"EngineVersion\":{\"secret\":true}}}")]
    public void Http_generated_deserialization_rejects_invalid_result_dictionary(string json)
    {
        using var services = new ServiceCollection().AddFullNetJson().BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        options.TypeInfoResolverChain.Insert(0, ReportingJsonSerializerContext.Default);

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ReportingExecutionRow>(json, options));
    }
}
