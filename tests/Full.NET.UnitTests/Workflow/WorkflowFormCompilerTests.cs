using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowFormCompilerTests
{
    [TestMethod]
    public void Compile_accepts_safe_fields_and_produces_stable_hash()
    {
        var first = Compile(Field("amount", "money", "{\"scale\":2,\"maximum\":1000}"));
        var second = Compile(Field("amount", "money", "{\"maximum\":1000,\"scale\":2}"));

        Assert.IsTrue(first.IsSuccess);
        Assert.AreEqual(first.Value!.ContentHash, second.Value!.ContentHash);
    }

    [TestMethod]
    public void Compile_accepts_checkbox_from_the_published_form_contract()
    {
        var result = Compile(Field("reviewers", "checkbox", "{\"options\":[\"owner\",\"finance\"]}"));

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Compile_accepts_schema_at_the_section_and_total_field_limits()
    {
        var sections = Enumerable.Range(0, 32)
            .Select(sectionIndex => new WorkflowFormSection(
                $"section{sectionIndex}",
                Enumerable.Range(0, 8)
                    .Select(fieldIndex => Field($"field{sectionIndex}_{fieldIndex}", "text", "{}"))
                    .ToArray()))
            .ToArray();

        var result = WorkflowFormCompiler.Compile(new WorkflowFormSchema(1, 1, sections));

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Compile_accepts_the_shared_cross_platform_golden_fixture()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Workflow",
            "Fixtures",
            "workflow-form-schema-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var schema = JsonSerializer.Deserialize(
            document.RootElement.GetProperty("formSchema").GetRawText(),
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema)!;

        var result = WorkflowFormCompiler.Compile(schema);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            document.RootElement.GetProperty("contentHash").GetString(),
            result.Value!.ContentHash);
    }

    [TestMethod]
    [DataRow("unknown", WorkflowErrorCodes.FormFieldTypeUnknown)]
    [DataRow("duplicate", WorkflowErrorCodes.FormFieldKeyDuplicate)]
    [DataRow("script", WorkflowErrorCodes.FormExtensionForbidden)]
    [DataRow("css", WorkflowErrorCodes.FormExtensionForbidden)]
    [DataRow("html", WorkflowErrorCodes.FormExtensionForbidden)]
    [DataRow("remote", WorkflowErrorCodes.FormExtensionForbidden)]
    [DataRow("money", WorkflowErrorCodes.FormMoneyScaleInvalid)]
    [DataRow("vform", WorkflowErrorCodes.FormExtensionForbidden)]
    [DataRow("choice-options-missing", WorkflowErrorCodes.FormChoiceOptionsInvalid)]
    [DataRow("choice-options-empty", WorkflowErrorCodes.FormChoiceOptionsInvalid)]
    [DataRow("choice-options-duplicate", WorkflowErrorCodes.FormChoiceOptionsInvalid)]
    [DataRow("choice-options-not-array", WorkflowErrorCodes.FormChoiceOptionsInvalid)]
    [DataRow("text-length-negative", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("text-length-reversed", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("text-length-not-integer", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("integer-range-reversed", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("integer-range-not-integer", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("decimal-scale-missing", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("decimal-scale-too-large", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("decimal-range-reversed", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("money-bound-exceeds-scale", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("date-bound-invalid", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("time-bound-invalid", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("datetime-offset-missing", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("temporal-range-reversed", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("unknown-constraint", WorkflowErrorCodes.FormFieldConstraintsInvalid)]
    [DataRow("null-constraints", WorkflowErrorCodes.FormStructureInvalid)]
    [DataRow("empty-schema", WorkflowErrorCodes.FormStructureInvalid)]
    [DataRow("empty-section", WorkflowErrorCodes.FormStructureInvalid)]
    [DataRow("duplicate-section", WorkflowErrorCodes.FormStructureInvalid)]
    [DataRow("invalid-section-key", WorkflowErrorCodes.FormStructureInvalid)]
    [DataRow("prototype-field-key", WorkflowErrorCodes.FormStructureInvalid)]
    [DataRow("long-field-key", WorkflowErrorCodes.FormStructureInvalid)]
    [DataRow("too-many-sections", WorkflowErrorCodes.FormSizeLimitExceeded)]
    [DataRow("too-many-section-fields", WorkflowErrorCodes.FormSizeLimitExceeded)]
    [DataRow("too-many-total-fields", WorkflowErrorCodes.FormSizeLimitExceeded)]
    public void Compile_rejects_unsafe_or_invalid_forms(string scenario, string expectedCode)
    {
        var schema = scenario switch
        {
            "unknown" => Schema(Field("field", "rich-editor", "{}")),
            "duplicate" => Schema(Field("field", "text", "{}"), Field("field", "text", "{}")),
            "script" => Schema(Field("field", "text", "{\"script\":\"alert(1)\"}")),
            "css" => Schema(Field("field", "text", "{\"cssCode\":\"body{}\"}")),
            "html" => Schema(Field("field", "text", "{\"html\":\"<iframe/>\"}")),
            "remote" => Schema(Field("field", "text", "{\"remoteUrl\":\"https://example.test\"}")),
            "money" => Schema(Field("amount", "money", "{\"scale\":8}")),
            "vform" => Schema(Field("field", "text", "{\"onCreated\":\"evil()\"}")),
            "choice-options-missing" => Schema(Field("choice", "radio", "{}")),
            "choice-options-empty" => Schema(Field("choice", "select", "{\"options\":[]}")),
            "choice-options-duplicate" => Schema(Field("choice", "checkbox", "{\"options\":[\"owner\",\"owner\"]}")),
            "choice-options-not-array" => Schema(Field("choice", "radio", "{\"options\":\"owner\"}")),
            "text-length-negative" => Schema(Field("summary", "text", "{\"minLength\":-1}")),
            "text-length-reversed" => Schema(Field("summary", "textarea", "{\"minLength\":5,\"maxLength\":4}")),
            "text-length-not-integer" => Schema(Field("summary", "text", "{\"maxLength\":2.5}")),
            "integer-range-reversed" => Schema(Field("count", "integer", "{\"minimum\":5,\"maximum\":4}")),
            "integer-range-not-integer" => Schema(Field("count", "integer", "{\"minimum\":1.5}")),
            "decimal-scale-missing" => Schema(Field("ratio", "decimal", "{}")),
            "decimal-scale-too-large" => Schema(Field("ratio", "decimal", "{\"scale\":29}")),
            "decimal-range-reversed" => Schema(Field("ratio", "decimal", "{\"scale\":3,\"minimum\":\"2.000\",\"maximum\":\"1.000\"}")),
            "money-bound-exceeds-scale" => Schema(Field("amount", "money", "{\"scale\":2,\"minimum\":\"0.001\"}")),
            "date-bound-invalid" => Schema(Field("dueDate", "date", "{\"minimum\":\"2026-02-30\"}")),
            "time-bound-invalid" => Schema(Field("cutoff", "time", "{\"maximum\":\"24:00\"}")),
            "datetime-offset-missing" => Schema(Field("deadline", "datetime", "{\"minimum\":\"2026-08-30T10:00:00\"}")),
            "temporal-range-reversed" => Schema(Field("dueDate", "date", "{\"minimum\":\"2026-09-01\",\"maximum\":\"2026-08-31\"}")),
            "unknown-constraint" => Schema(Field("summary", "text", "{\"placeholder\":\"unsafe design metadata\"}")),
            "null-constraints" => Schema(new WorkflowFormField("summary", "text", true, null!)),
            "empty-schema" => new WorkflowFormSchema(1, 1, []),
            "empty-section" => new WorkflowFormSchema(1, 1, [new WorkflowFormSection("main", [])]),
            "duplicate-section" => new WorkflowFormSchema(1, 1,
            [
                new WorkflowFormSection("main", [Field("first", "text", "{}")]),
                new WorkflowFormSection("main", [Field("second", "text", "{}")]),
            ]),
            "invalid-section-key" => new WorkflowFormSchema(1, 1,
                [new WorkflowFormSection("bad key", [Field("field", "text", "{}")])]),
            "prototype-field-key" => Schema(Field("__proto__", "text", "{}")),
            "long-field-key" => Schema(Field(new string('a', 65), "text", "{}")),
            "too-many-sections" => new WorkflowFormSchema(1, 1, Enumerable.Range(0, 33)
                .Select(index => new WorkflowFormSection($"section{index}", [Field($"field{index}", "text", "{}")]))
                .ToArray()),
            "too-many-section-fields" => Schema(Enumerable.Range(0, 65)
                .Select(index => Field($"field{index}", "text", "{}"))
                .ToArray()),
            "too-many-total-fields" => new WorkflowFormSchema(1, 1, Enumerable.Range(0, 5)
                .Select(sectionIndex => new WorkflowFormSection(
                    $"section{sectionIndex}",
                    Enumerable.Range(0, sectionIndex == 4 ? 1 : 64)
                        .Select(fieldIndex => Field($"field{sectionIndex}_{fieldIndex}", "text", "{}"))
                        .ToArray()))
                .ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var result = WorkflowFormCompiler.Compile(schema);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(expectedCode, result.ErrorCode);
    }

    private static WorkflowCompilationResult Compile(WorkflowFormField field) =>
        WorkflowFormCompiler.Compile(Schema(field));

    private static WorkflowFormSchema Schema(params WorkflowFormField[] fields) =>
        new(1, 1, [new WorkflowFormSection("main", fields)]);

    private static WorkflowFormField Field(string key, string type, string constraints) =>
        new(key, type, true, ParseObject(constraints));

    private static IReadOnlyDictionary<string, JsonElement> ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }
}
