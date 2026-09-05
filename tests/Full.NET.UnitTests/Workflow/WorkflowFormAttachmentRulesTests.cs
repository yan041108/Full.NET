using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowFormAttachmentRulesTests
{
    [TestMethod]
    public void Validate_accepts_attachment_file_id_arrays_within_max_count()
    {
        var fileId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var schema = Schema(Field(
            "evidence",
            "attachment",
            true,
            $$"""{"maxCount":2,"maxSizeBytes":1048576,"allowedExtensions":["pdf"]}"""));
        var values = ParseElement($$"""{"evidence":["{{fileId:D}}"]}""");

        Assert.IsTrue(WorkflowFormValueValidator.Validate(schema, values));
    }

    [TestMethod]
    public void Validate_rejects_attachment_arrays_exceeding_max_count_or_with_duplicates()
    {
        var fileId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var schema = Schema(Field(
            "evidence",
            "attachment",
            false,
            $$"""{"maxCount":1,"maxSizeBytes":1048576,"allowedExtensions":["pdf"]}"""));
        var tooMany = ParseElement($$"""{"evidence":["{{fileId:D}}","6ba7b810-9dad-11d1-80b4-00c04fd430c8"]}""");
        var duplicate = ParseElement($$"""{"evidence":["{{fileId:D}}","{{fileId:D}}"]}""");

        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, tooMany));
        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, duplicate));
    }

    [TestMethod]
    public void MatchesAllowedExtension_uses_declared_extension_list()
    {
        Assert.IsTrue(WorkflowFormAttachmentValueRules.MatchesAllowedExtension(
            "report.PDF",
            ["pdf", "png"]));
        Assert.IsFalse(WorkflowFormAttachmentValueRules.MatchesAllowedExtension(
            "report.exe",
            ["pdf", "png"]));
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
