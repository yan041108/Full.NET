using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>固定子版本和最小字段映射先验证纯规则，目录开放以真实父子运行及双库验收为前提。</summary>
[TestClass]
public sealed class WorkflowSubprocessPolicyTests
{
    private static readonly Guid Version = Guid.CreateVersion7();
    private static WorkflowFormSchema Parent() => new(1, 1, [new("main", [new("title", "text", true, new Dictionary<string, JsonElement>()), new("result", "integer", false, new Dictionary<string, JsonElement>())])]);
    private static WorkflowFormSchema Child() => new(1, 1, [new("main", [new("subject", "text", true, new Dictionary<string, JsonElement>()), new("score", "integer", false, new Dictionary<string, JsonElement>())])]);
    private static JsonElement Config() => JsonSerializer.SerializeToElement(new { subprocessPolicy = new { definitionVersionId = Version,
        inputFields = new { subject = "title" }, outputFields = new { result = "score" } }, fieldPolicies = new { title = "readOnly", result = "editable" } });

    [TestMethod]
    public void Only_declared_fields_cross_the_child_boundary_and_values_are_copied()
    {
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(Config(), Parent(), out var policy)); Assert.IsTrue(policy!.MatchesSchemas(Parent(), Child()));
        var values = new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("hello"), ["secret"] = JsonSerializer.SerializeToElement("private") };
        Assert.IsTrue(policy.TryCreateInput(values, Child(), out var input));
        Assert.AreEqual("{\"subject\":\"hello\"}", input);
        Assert.IsTrue(policy.TryCreateOutput(new Dictionary<string, JsonElement> { ["score"] = JsonSerializer.SerializeToElement(5), ["secret"] = JsonSerializer.SerializeToElement("private") }, Parent(), out var patch));
        Assert.AreEqual(1, patch.Count); Assert.AreEqual(5, patch["result"].GetInt32());
    }

    [TestMethod]
    public void Missing_required_input_and_wrong_output_type_fail_without_default_values()
    {
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(Config(), Parent(), out var policy));
        Assert.IsFalse(policy!.TryCreateInput(new Dictionary<string, JsonElement>(), Child(), out _));
        Assert.IsFalse(policy.TryCreateOutput(new Dictionary<string, JsonElement>(), Parent(), out _));
        Assert.IsFalse(policy.TryCreateOutput(new Dictionary<string, JsonElement> { ["score"] = JsonSerializer.SerializeToElement("5") }, Parent(), out _));
    }

    [TestMethod]
    public void Unknown_configuration_and_nonwritable_parent_output_are_rejected()
    {
        var invalid = Config().GetRawText().Replace("\"definitionVersionId\":", "\"sql\":\"select\",\"definitionVersionId\":", StringComparison.Ordinal);
        Assert.IsFalse(WorkflowSubprocessPolicy.TryRead(JsonDocument.Parse(invalid).RootElement, Parent(), out _));
        var readOnly = Config().GetRawText().Replace("\"result\":\"editable\"", "\"result\":\"readOnly\"", StringComparison.Ordinal);
        Assert.IsFalse(WorkflowSubprocessPolicy.TryRead(JsonDocument.Parse(readOnly).RootElement, Parent(), out _));
    }

    [TestMethod]
    public void Cross_type_mapping_and_unmapped_required_child_fields_are_rejected()
    {
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(Config(), Parent(), out var policy));
        var child = Child(); var fields = child.Sections[0].Fields;
        Assert.IsFalse(policy!.MatchesSchemas(Parent(), child with { Sections = [new("main", [fields[0] with { FieldTypeKey = "integer" }, fields[1]])] }));
        Assert.IsFalse(policy.MatchesSchemas(Parent(), child with { Sections = [new("main", [fields[0], fields[1] with { Required = true }])] }));
    }

    [TestMethod]
    public void File_and_detail_mapping_require_valid_closed_field_constraints()
    {
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(Config(), Parent(), out var policy));
        var parent = Parent(); var child = Child();
        foreach (var type in new[] { "attachment", "subtable" })
            Assert.IsFalse(policy!.MatchesSchemas(parent with { Sections = [new("main", [parent.Sections[0].Fields[0] with { FieldTypeKey = type }, parent.Sections[0].Fields[1]])] },
                child with { Sections = [new("main", [child.Sections[0].Fields[0] with { FieldTypeKey = type }, child.Sections[0].Fields[1]])] }));
    }

    [TestMethod]
    public void Bound_version_and_hashes_are_closed_and_cannot_select_another_child()
    {
        var form = Guid.CreateVersion7(); var bound = JsonSerializer.SerializeToElement(new { definitionVersionId = Version, formVersionId = form, definitionHash = new string('a',64), formHash = new string('b',64), activityBudget = 4, maximumDepth = 1, hasHumanApproval = false });
        Assert.IsTrue(WorkflowBoundSubprocess.TryRead(bound, Version, out var parsed)); Assert.AreEqual(form, parsed!.FormVersionId);
        Assert.IsFalse(WorkflowBoundSubprocess.TryRead(bound, Guid.CreateVersion7(), out _));
        Assert.IsFalse(WorkflowBoundSubprocess.TryRead(JsonDocument.Parse(bound.GetRawText().Replace(new string('a',64), new string('A',64), StringComparison.Ordinal)).RootElement, Version, out _));
    }
}
