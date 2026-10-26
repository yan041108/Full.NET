using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>多实例规则仍未接运行；先固定来源、分母与固化集合的可验证语义。</summary>
[TestClass]
public sealed class WorkflowMultiInstancePolicyTests
{
    private static WorkflowMultiInstancePolicy Read(object source, int percentage = 67, string empty = "reject")
    {
        Assert.IsTrue(WorkflowMultiInstancePolicy.TryRead(JsonSerializer.SerializeToElement(new { modeKey = "parallel", source,
            requiredPercentage = percentage, emptyModeKey = empty, outputReducers = new { result = "first" } }), null, out var policy));
        return policy!;
    }
    [TestMethod]
    [DataRow(3, 67, 3)]
    [DataRow(3, 66, 2)]
    [DataRow(32, 1, 1)]
    [DataRow(32, 100, 32)]
    public void Required_successes_use_frozen_denominator_and_round_up(int count, int percentage, int expected) =>
        Assert.AreEqual(expected, Read(new { kindKey = "fixed_count", count }, percentage).RequiredSuccesses(count));

    [TestMethod]
    public void Numeric_count_never_truncates_or_converts_text_and_zero_requires_explicit_skip()
    {
        var policy = Read(new { kindKey = "number_field", fieldKey = "count", maximumCount = 3 });
        foreach (var value in new object[] { "2", 2.5, 4, -1, 0 })
            Assert.IsFalse(policy.TryFreeze(new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement(value) }, null, out _));
        Assert.IsTrue(policy.TryFreeze(new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement(3) }, null, out var items));
        CollectionAssert.AreEqual(new[] { "item-0001", "item-0002", "item-0003" }, items.Select(item => item.ItemKey).ToArray());
        var skip = Read(new { kindKey = "number_field", fieldKey = "count", maximumCount = 3 }, empty: "skip");
        Assert.IsTrue(skip.TryFreeze(new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement(0) }, null, out var empty)); Assert.AreEqual(0, empty.Count);
    }
    [TestMethod]
    public void Collection_order_is_fixed_and_duplicate_members_are_rejected()
    {
        var policy = Read(new { kindKey = "collection_field", fieldKey = "members", itemFieldKey = "member", maximumCount = 3 });
        Assert.IsTrue(policy.TryFreeze(new Dictionary<string, JsonElement> { ["members"] = JsonSerializer.SerializeToElement(new[] { "b", "a" }) }, null, out var items));
        CollectionAssert.AreEqual(new[] { "\"b\"", "\"a\"" }, items.Select(item => item.ValueJson).ToArray());
        Assert.IsFalse(policy.TryFreeze(new Dictionary<string, JsonElement> { ["members"] = JsonSerializer.SerializeToElement(new[] { "a", "a" }) }, null, out _));
    }
    [TestMethod]
    public void Role_members_require_trusted_snapshot_and_excess_is_not_truncated()
    {
        var policy = Read(new { kindKey = "role_members", roleId = Guid.CreateVersion7(), itemFieldKey = "member", maximumCount = 2 });
        var members = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        Assert.IsFalse(policy.TryFreeze(new Dictionary<string, JsonElement>(), null, out _));
        Assert.IsTrue(policy.TryFreeze(new Dictionary<string, JsonElement>(), members, out var first));
        Assert.IsTrue(policy.TryFreeze(new Dictionary<string, JsonElement>(), members.Reverse().ToArray(), out var second));
        CollectionAssert.AreEqual(first.Select(item => item.ValueJson).ToArray(), second.Select(item => item.ValueJson).ToArray());
        Assert.IsFalse(policy.TryFreeze(new Dictionary<string, JsonElement>(), [..members, Guid.CreateVersion7()], out _));
    }
    [TestMethod]
    public void Unknown_policy_properties_and_wrong_numeric_kinds_fail_closed()
    {
        foreach (var value in new[] {
            "{\"modeKey\":\"parallel\",\"source\":{\"kindKey\":\"fixed_count\",\"count\":\"2\"},\"requiredPercentage\":100,\"emptyModeKey\":\"reject\",\"outputReducers\":{}}",
            "{\"modeKey\":\"parallel\",\"source\":{\"kindKey\":\"fixed_count\",\"count\":2},\"requiredPercentage\":\"100\",\"emptyModeKey\":\"reject\",\"outputReducers\":{}}" })
            Assert.IsFalse(WorkflowMultiInstancePolicy.TryRead(JsonDocument.Parse(value).RootElement, null, out _));
    }
    [TestMethod]
    public void Threshold_seals_current_successes_and_late_success_does_not_change_selection()
    {
        var policy = Read(new { kindKey = "fixed_count", count = 3 }, 66);
        var first = new WorkflowMultiInstanceFact(Guid.CreateVersion7(), "item-0001", 1, "succeeded");
        var second = new WorkflowMultiInstanceFact(Guid.CreateVersion7(), "item-0002", 2, "succeeded");
        var third = new WorkflowMultiInstanceFact(Guid.CreateVersion7(), "item-0003", 3, "running");
        var selected = WorkflowMultiInstanceDecision.Evaluate(policy, [third, second, first]);
        Assert.AreEqual("select", selected.StateKey); CollectionAssert.AreEqual(new[] { first.Id, second.Id }, selected.SelectedIds.ToArray());
        CollectionAssert.AreEqual(new[] { third.Id }, selected.OutstandingIds.ToArray());
        Assert.IsTrue(WorkflowMultiInstanceDecision.TryReadSealedSelection(policy, selected.SelectedIds, [first, second, third with { StatusKey = "succeeded" }], out var stable));
        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, stable.Select(item => item.Id).ToArray());
        Assert.IsFalse(WorkflowMultiInstanceDecision.TryReadSealedSelection(policy, [], [first, second, third], out _));
    }
    [TestMethod]
    public void Failure_does_not_count_as_success_and_unknown_outcome_cannot_be_assumed_failed()
    {
        var policy = Read(new { kindKey = "fixed_count", count = 2 }, 100);
        var first = new WorkflowMultiInstanceFact(Guid.CreateVersion7(), "item-0001", 1, "succeeded");
        var second = new WorkflowMultiInstanceFact(Guid.CreateVersion7(), "item-0002", 2, "running");
        Assert.AreEqual("wait", WorkflowMultiInstanceDecision.Evaluate(policy, [first, second]).StateKey);
        Assert.AreEqual("impossible", WorkflowMultiInstanceDecision.Evaluate(policy, [first, second with { StatusKey = "rejected" }]).StateKey);
        Assert.AreEqual("unknown", WorkflowMultiInstanceDecision.Evaluate(policy, [first, second with { StatusKey = "unknown" }]).StateKey);
    }
}
