using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowMultiInstanceSubprocessPolicyTests
{
    private static readonly Guid Version = Guid.CreateVersion7();
    private static WorkflowFormSchema Parent => new(1, 1, [new("main", [new("count", "integer", false, new Dictionary<string, JsonElement>()), new("total", "integer", false, new Dictionary<string, JsonElement>())])]);
    private static WorkflowFormSchema Child => new(1, 1, [new("main", [new("member", "text", true, new Dictionary<string, JsonElement>()), new("score", "integer", false, new Dictionary<string, JsonElement>())])]);
    private static JsonElement Config(string reducer = "sum", string grant = "readOnly", object? input = null) => JsonSerializer.SerializeToElement(new {
        fieldPolicies = new { count = grant, total = "editable" }, subprocessPolicy = new { definitionVersionId = Version, inputFields = input ?? new { }, outputFields = new { total = "score" } },
        multiInstancePolicy = new { modeKey = "serial", source = new { kindKey = "collection_field", fieldKey = "members", itemFieldKey = "member", maximumCount = 2 },
            requiredPercentage = 100, emptyModeKey = "reject", outputReducers = new { total = reducer } } });

    [TestMethod]
    public void Source_fields_need_read_access_and_item_field_cannot_also_be_mapped()
    {
        var json = Config().GetRawText().Replace("collection_field", "number_field").Replace(",\"itemFieldKey\":\"member\"", "").Replace("\"fieldKey\":\"members\"", "\"fieldKey\":\"count\"");
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(JsonDocument.Parse(json).RootElement, Parent, out _));
        Assert.IsFalse(WorkflowSubprocessPolicy.TryRead(JsonDocument.Parse(json.Replace("readOnly", "hidden")).RootElement, Parent, out _));
        var withItem = Config(input: new { member = "count" }).GetRawText().Replace("\"fieldKey\":\"members\"", "\"fieldKey\":\"count\"");
        Assert.IsFalse(WorkflowSubprocessPolicy.TryRead(JsonDocument.Parse(withItem).RootElement, null, out _));
    }
    [TestMethod]
    public void Item_input_must_be_a_real_frozen_seed_and_reducer_must_match_output_type()
    {
        var config = Config().GetRawText().Replace("\"count\":\"readOnly\"", "\"members\":\"readOnly\"");
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(JsonDocument.Parse(config).RootElement, null, out var policy));
        var parent = new WorkflowFormSchema(1, 1, [new("main", [new("members", "checkbox", false, new Dictionary<string, JsonElement>()), new("total", "integer", false, new Dictionary<string, JsonElement>())])]);
        Assert.IsTrue(policy!.MatchesSchemas(parent, Child));
        Assert.IsFalse(policy.TryCreateInput(new Dictionary<string, JsonElement>(), Child, out _));
        Assert.IsTrue(policy.TryCreateInput(new Dictionary<string, JsonElement>(), Child, out var input, new("item-0001", 1, "\"alice\"")));
        Assert.AreEqual("{\"member\":\"alice\"}", input);
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(JsonDocument.Parse(config.Replace("sum", "all_true")).RootElement, null, out var invalid));
        Assert.IsFalse(invalid!.MatchesSchemas(parent, Child));
    }
    [TestMethod]
    public void Repeated_child_budget_is_reserved_for_the_declared_maximum()
    {
        var bound = new WorkflowBoundSubprocess(Version, Guid.CreateVersion7(), new string('a', 64), new string('b', 64), 3000, 1, false);
        var config = JsonSerializer.SerializeToElement(new { fieldPolicies = new { }, subprocessPolicy = new { definitionVersionId = Version, inputFields = new { }, outputFields = new { } },
            boundSubprocess = JsonSerializer.SerializeToElement(bound, WorkflowJsonSerializerContext.Default.WorkflowBoundSubprocess),
            multiInstancePolicy = new { modeKey = "parallel", source = new { kindKey = "fixed_count", count = 2 }, requiredPercentage = 100, emptyModeKey = "reject", outputReducers = new { } } });
        var draft = new WorkflowDefinitionDraft(1, [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "child" } })),
            new("child", "process.subprocess", 1, config), new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        Assert.IsFalse(WorkflowSubprocessBindings.TryReadBudget(draft, out _, out _));
    }
}
