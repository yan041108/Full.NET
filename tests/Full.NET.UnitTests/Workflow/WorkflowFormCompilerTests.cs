using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

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
