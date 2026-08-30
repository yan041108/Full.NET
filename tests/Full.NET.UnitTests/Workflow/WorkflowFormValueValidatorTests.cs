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
