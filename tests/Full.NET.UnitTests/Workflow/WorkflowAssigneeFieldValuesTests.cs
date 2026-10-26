using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>表单中的人员或部门引用只承载有界稳定标识，不能接收显示文本或任意对象。</summary>
[TestClass]
public sealed class WorkflowAssigneeFieldValuesTests
{
    [TestMethod]
    public void Single_and_multiple_reference_fields_preserve_explicit_order()
    {
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7();
        Assert.IsTrue(WorkflowAssigneeFieldValues.TryRead(JsonSerializer.SerializeToElement(first), out var single));
        CollectionAssert.AreEqual(new[] { first }, single.ToArray());
        Assert.IsTrue(WorkflowAssigneeFieldValues.TryRead(JsonSerializer.SerializeToElement(new[] { second, first }), out var many));
        CollectionAssert.AreEqual(new[] { second, first }, many.ToArray());
    }
    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("\"username\"")]
    [DataRow("{\"userId\":\"01912345-6789-7abc-8def-0123456789ab\"}")]
    [DataRow("[\"00000000-0000-0000-0000-000000000000\"]")]
    public void Invalid_or_empty_references_do_not_become_the_initiator(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowAssigneeFieldValues.TryRead(document.RootElement, out _));
    }
    [TestMethod]
    public void Duplicate_and_oversized_references_fail_without_truncation()
    {
        var id = Guid.CreateVersion7();
        Assert.IsFalse(WorkflowAssigneeFieldValues.TryRead(JsonSerializer.SerializeToElement(new[] { id, id }), out _));
        Assert.IsFalse(WorkflowAssigneeFieldValues.TryRead(JsonSerializer.SerializeToElement(Enumerable.Range(0, 21).Select(_ => Guid.CreateVersion7()).ToArray()), out _));
    }
    [TestMethod]
    public void Field_binding_requires_typed_closed_reference_options()
    {
        var options = new Dictionary<string, JsonElement> { ["options"] = JsonSerializer.SerializeToElement(new[] { Guid.CreateVersion7().ToString("D") }) };
        Assert.IsTrue(WorkflowAssigneeFieldValues.Supports(new("person", "select", false, options)));
        Assert.IsTrue(WorkflowAssigneeFieldValues.Supports(new("departments", "checkbox", false, options)));
        Assert.IsFalse(WorkflowAssigneeFieldValues.Supports(new("note", "text", false, options)));
        options["options"] = JsonSerializer.SerializeToElement(new[] { "display-name" });
        Assert.IsFalse(WorkflowAssigneeFieldValues.Supports(new("person", "select", false, options)));
    }
}
