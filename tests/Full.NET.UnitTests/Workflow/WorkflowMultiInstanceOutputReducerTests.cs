using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>确定性归约不能按完成时刻覆盖字段，也不能丢失定点精度或以默认值掩盖冲突。</summary>
[TestClass]
public sealed class WorkflowMultiInstanceOutputReducerTests
{
    private static WorkflowFormSchema Schema(string type, Dictionary<string, JsonElement>? constraints = null) =>
        new(1, 1, [new("main", [new("result", type, false, constraints ?? [])])]);
    private static WorkflowMultiInstanceOutput Output(string key, object? value) => new(Guid.CreateVersion7(), key,
        new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(value) });
    private static bool Merge(WorkflowFormSchema schema, string rule, WorkflowMultiInstanceOutput[] outputs, out IReadOnlyDictionary<string, JsonElement> patch) =>
        WorkflowMultiInstanceOutputReducer.TryMerge(schema, ["result"], new Dictionary<string, string> { ["result"] = rule }, outputs, out patch);
    [TestMethod]
    public void First_and_last_use_fixed_item_keys_and_do_not_share_values()
    {
        var outputs = new[] { Output("item-0002", "second"), Output("item-0001", "first") };
        Assert.IsTrue(Merge(Schema("text"), "first", outputs, out var first)); Assert.AreEqual("first", first["result"].GetString());
        Assert.IsTrue(Merge(Schema("text"), "last", outputs, out var last)); Assert.AreEqual("second", last["result"].GetString());
    }
    [TestMethod]
    public void Integer_overflow_and_merged_range_violation_fail_without_default_zero()
    {
        Assert.IsFalse(Merge(Schema("integer"), "sum", [Output("a", long.MaxValue), Output("b", 1L)], out _));
        var schema = Schema("integer", new Dictionary<string, JsonElement> { ["maximum"] = JsonSerializer.SerializeToElement(10) });
        Assert.IsFalse(Merge(schema, "sum", [Output("a", 6L), Output("b", 6L)], out _));
        Assert.IsTrue(Merge(schema, "min", [Output("a", 6L), Output("b", 3L)], out var minimum)); Assert.AreEqual(3L, minimum["result"].GetInt64());
    }
    [TestMethod]
    public void Money_remains_a_canonical_decimal_string_with_exact_arithmetic()
    {
        var schema = Schema("money", new Dictionary<string, JsonElement> { ["scale"] = JsonSerializer.SerializeToElement(2) });
        Assert.IsTrue(Merge(schema, "sum", [Output("a", "0.10"), Output("b", "0.20")], out var sum));
        Assert.AreEqual(JsonValueKind.String, sum["result"].ValueKind); Assert.AreEqual("0.3", sum["result"].GetString());
        Assert.IsFalse(Merge(schema, "sum", [Output("a", 0.10m), Output("b", "0.20")], out _));
    }
    [TestMethod]
    public void Boolean_reducers_do_not_treat_null_as_false()
    {
        Assert.IsTrue(Merge(Schema("switch"), "all_true", [Output("a", true), Output("b", false)], out var all)); Assert.IsFalse(all["result"].GetBoolean());
        Assert.IsTrue(Merge(Schema("switch"), "any_true", [Output("a", true), Output("b", false)], out var any)); Assert.IsTrue(any["result"].GetBoolean());
        Assert.IsFalse(Merge(Schema("switch"), "any_true", [Output("a", true), Output("b", null)], out _));
    }
    [TestMethod]
    public void Missing_reducer_duplicate_item_and_undeclared_patch_are_rejected()
    {
        var output = Output("a", "value");
        Assert.IsFalse(WorkflowMultiInstanceOutputReducer.TryMerge(Schema("text"), ["result"], new Dictionary<string, string>(), [output], out _));
        Assert.IsFalse(Merge(Schema("text"), "first", [output, output], out _));
        var unexpected = output with { Values = new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement("value"), ["secret"] = JsonSerializer.SerializeToElement("private") } };
        Assert.IsFalse(Merge(Schema("text"), "first", [unexpected], out _));
        Assert.IsFalse(Merge(Schema("text"), "first", [], out _));
    }
    [TestMethod]
    public void File_and_subtable_identifiers_cannot_be_copied_by_scalar_reducers()
    {
        Assert.IsFalse(Merge(Schema("attachment"), "first", [Output("a", Array.Empty<string>())], out _));
        Assert.IsFalse(Merge(Schema("subtable"), "last", [Output("a", Array.Empty<string>())], out _));
    }
}
