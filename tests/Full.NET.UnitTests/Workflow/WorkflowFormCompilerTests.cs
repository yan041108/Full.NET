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
