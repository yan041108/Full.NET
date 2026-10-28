using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>分支差量保持外层独立更新，冲突不凭最后写入者取胜。</summary>
[TestClass]
public sealed class WorkflowReturnInputCoordinatorTests
{
    [TestMethod]
    public void A_return_merges_only_source_changes_and_preserves_unrelated_parent_fields()
    {
        Assert.IsTrue(WorkflowReturnInputCoordinator.TryMerge("{\"a\":1,\"b\":2}", "{\"a\":3,\"b\":2}", "{\"a\":1,\"b\":4,\"new\":true}", out var merged, out var delta));
        using var json = JsonDocument.Parse(merged!);
        Assert.AreEqual(3, json.RootElement.GetProperty("a").GetInt32());
        Assert.AreEqual(4, json.RootElement.GetProperty("b").GetInt32()); Assert.IsTrue(json.RootElement.GetProperty("new").GetBoolean());
        Assert.AreEqual(1, delta.EnumerateObject().Count());
    }

    [TestMethod]
    [DataRow("{\"a\":4}")]
    [DataRow("{}")]
    public void A_conflicting_parent_change_or_deletion_rejects_the_projection(string parent) =>
        Assert.IsFalse(WorkflowReturnInputCoordinator.TryMerge("{\"a\":1}", "{\"a\":3}", parent, out _, out _));

    [TestMethod]
    public void Equal_concurrent_values_and_structurally_equal_objects_are_not_false_conflicts()
    {
        Assert.IsTrue(WorkflowReturnInputCoordinator.TryMerge("{\"a\":1,\"obj\":{\"x\":1,\"y\":2}}", "{\"a\":3,\"obj\":{\"y\":2,\"x\":1}}", "{\"a\":3,\"obj\":{\"x\":1,\"y\":2}}", out _, out var delta));
        Assert.AreEqual(1, delta.EnumerateObject().Count());
    }

    [TestMethod]
    public void New_source_fields_cannot_overwrite_a_different_new_parent_value() =>
        Assert.IsFalse(WorkflowReturnInputCoordinator.TryMerge("{}", "{\"a\":3}", "{\"a\":4}", out _, out _));
}
