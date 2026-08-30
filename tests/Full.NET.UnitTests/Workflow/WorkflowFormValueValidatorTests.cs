using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowFormValueValidatorTests
{
    [TestMethod]
    public void Validate_accepts_choice_values_declared_by_the_published_schema()
    {
        var schema = Schema(
            Field("decision", "radio", true, "{\"options\":[\"approve\",\"reject\"]}"),
            Field("reviewers", "checkbox", true, "{\"options\":[\"owner\",\"finance\"]}"));

        var values = ParseElement("{\"decision\":\"approve\",\"reviewers\":[\"owner\",\"finance\"]}");

        Assert.IsTrue(WorkflowFormValueValidator.Validate(schema, values));
    }

    [TestMethod]
    [DataRow("radio", "\"bypass\"")]
    [DataRow("select", "\"bypass\"")]
    [DataRow("checkbox", "[\"owner\",\"bypass\"]")]
    [DataRow("checkbox", "[]")]
    [DataRow("checkbox", "[\"owner\",\"owner\"]")]
    public void Validate_rejects_choice_values_not_allowed_by_the_published_schema(
        string fieldTypeKey,
        string valueJson)
    {
        var schema = Schema(Field("choice", fieldTypeKey, true, "{\"options\":[\"owner\",\"finance\"]}"));
        var values = ParseElement($"{{\"choice\":{valueJson}}}");

        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, values));
    }

    [TestMethod]
    public void Validate_accepts_text_length_and_integer_range_boundaries()
    {
        var schema = Schema(
            Field("summary", "text", true, "{\"minLength\":2,\"maxLength\":4}"),
            Field("count", "integer", true, "{\"minimum\":10,\"maximum\":20}"));
        var values = ParseElement("{\"summary\":\"测试\",\"count\":20}");

        Assert.IsTrue(WorkflowFormValueValidator.Validate(schema, values));
    }

    [TestMethod]
    [DataRow("text", "{\"minLength\":2,\"maxLength\":4}", "\"a\"")]
    [DataRow("textarea", "{\"minLength\":2,\"maxLength\":4}", "\"abcde\"")]
    [DataRow("integer", "{\"minimum\":10,\"maximum\":20}", "9")]
    [DataRow("integer", "{\"minimum\":10,\"maximum\":20}", "21")]
    public void Validate_rejects_values_outside_published_text_and_integer_constraints(
        string fieldTypeKey,
        string constraintsJson,
        string valueJson)
    {
        var schema = Schema(Field("value", fieldTypeKey, true, constraintsJson));
        var values = ParseElement($"{{\"value\":{valueJson}}}");

        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, values));
    }

    [TestMethod]
    public void Validate_accepts_canonical_decimal_strings_at_published_boundaries()
    {
        var schema = Schema(
            Field("amount", "money", true, "{\"scale\":2,\"minimum\":\"10.00\",\"maximum\":\"12.30\"}"),
            Field("ratio", "decimal", true, "{\"scale\":3,\"minimum\":-2,\"maximum\":0}"));
        var values = ParseElement("{\"amount\":\"12.30\",\"ratio\":\"-1.25\"}");

        Assert.IsTrue(WorkflowFormValueValidator.Validate(schema, values));
    }

    [TestMethod]
    [DataRow("money", "{\"scale\":2}", "12.30")]
    [DataRow("money", "{\"scale\":2}", "\"12.345\"")]
    [DataRow("money", "{\"scale\":2}", "\"01.25\"")]
    [DataRow("decimal", "{\"scale\":3}", "\"1e2\"")]
    [DataRow("decimal", "{\"scale\":3,\"minimum\":\"-2.000\",\"maximum\":\"2.000\"}", "\"2.001\"")]
    public void Validate_rejects_noncanonical_or_out_of_range_decimal_values(
        string fieldTypeKey,
        string constraintsJson,
        string valueJson)
    {
        var schema = Schema(Field("value", fieldTypeKey, true, constraintsJson));
        var values = ParseElement($"{{\"value\":{valueJson}}}");

        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, values));
    }

    private static WorkflowFormSchema Schema(params WorkflowFormField[] fields) =>
        new(1, 1, [new WorkflowFormSection("main", fields)]);

    private static WorkflowFormField Field(
        string key,
        string type,
        bool required,
        string constraints) =>
        new(key, type, required, ParseObject(constraints));

    private static IReadOnlyDictionary<string, JsonElement> ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
