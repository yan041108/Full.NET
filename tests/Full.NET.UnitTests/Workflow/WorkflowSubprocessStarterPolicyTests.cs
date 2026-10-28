using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.UnitTests.Workflow;

/// <summary>发起人来源不可用任意数组或空UUID冒充，集合项只有角色身份来源可用于逐项委托。</summary>
[TestClass]
public sealed class WorkflowSubprocessStarterPolicyTests
{
    [TestMethod]
    public void Multiple_starter_result_requires_canonical_uuid_choices()
    {
        var id = Guid.Parse("11111111-aaaa-4111-8111-111111111111");
        var multi = new WorkflowMultiInstancePolicy("parallel", "fixed_count", 2, 2, null, null, null, 100, "reject", new Dictionary<string, string>());
        WorkflowFormSchema Schema(string option) => new(1, 1, [new("main", [new("actors", "checkbox", false, new Dictionary<string, JsonElement> { ["options"] = JsonSerializer.SerializeToElement(new[] { option }) })])]);
        var value = JsonSerializer.SerializeToElement(new { source = new { kindKey = "parent" }, resultFieldKey = "actors" });
        Assert.IsTrue(WorkflowSubprocessStarterPolicy.TryRead(value, Schema(id.ToString("D")), multi, out _));
        Assert.IsFalse(WorkflowSubprocessStarterPolicy.TryRead(value, Schema("a display label"), multi, out _));
        Assert.IsFalse(WorkflowSubprocessStarterPolicy.TryRead(value, Schema(id.ToString("D").ToUpperInvariant()), multi, out _));
    }
    [TestMethod]
    public void Item_user_requires_role_items_and_rejects_untrusted_shapes()
    {
        var json = JsonSerializer.SerializeToElement(new { source = new { kindKey = "item_user" } });
        Assert.IsFalse(WorkflowSubprocessStarterPolicy.TryRead(json, null, null, out _));
        var multi = new WorkflowMultiInstancePolicy("serial", "role_members", 2, null, null, Guid.CreateVersion7(), "member", 100, "reject", new Dictionary<string, string>());
        Assert.IsTrue(WorkflowSubprocessStarterPolicy.TryRead(json, null, multi, out var policy));
        var user = Guid.CreateVersion7(); Assert.IsTrue(policy!.TryResolve(Guid.CreateVersion7(), new Dictionary<string, JsonElement>(), new("item-0001", 1, JsonSerializer.Serialize(user)), out var starter)); Assert.AreEqual(user, starter);
        Assert.IsFalse(policy.TryResolve(user, new Dictionary<string, JsonElement>(), new("item-0001", 1, "[\"" + user.ToString("D") + "\"]"), out _));
        Assert.IsFalse(policy.TryResolve(user, new Dictionary<string, JsonElement>(), new("item-0001", 1, "\"00000000-0000-0000-0000-000000000000\""), out _));
    }
    [TestMethod]
    public void Field_user_is_a_single_identity_from_the_frozen_input()
    {
        var user = Guid.CreateVersion7();
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("starter", "select", false, new Dictionary<string, JsonElement> { ["options"] = JsonSerializer.SerializeToElement(new[] { user.ToString("D") }) })])]);
        var config = JsonSerializer.SerializeToElement(new { source = new { kindKey = "field_user", fieldKey = "starter" } });
        Assert.IsTrue(WorkflowSubprocessStarterPolicy.TryRead(config, schema, null, out var policy));
        var input = new Dictionary<string, JsonElement> { ["starter"] = JsonSerializer.SerializeToElement(user.ToString("D")) };
        Assert.IsTrue(policy!.TryResolve(Guid.CreateVersion7(), input, new("single", 1, "1"), out var starter)); Assert.AreEqual(user, starter);
        input["starter"] = JsonSerializer.SerializeToElement(new[] { user }); Assert.IsFalse(policy.TryResolve(user, input, new("single", 1, "1"), out _));
        Assert.IsFalse(WorkflowSubprocessStarterPolicy.TryRead(JsonSerializer.SerializeToElement(new { source = new { kindKey = "parent", userId = user } }), null, null, out _));
    }
    [TestMethod]
    public void Delegated_single_item_uses_durable_preparation_and_preserves_the_authoritative_starter_snapshot()
    {
        var parent = Guid.CreateVersion7(); var user = Guid.CreateVersion7();
        var policy = new WorkflowSubprocessPolicy(Guid.CreateVersion7(), new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
            Starter: new("specified_user", user, null, null));
        Assert.IsNotNull(policy.ExecutionSet); Assert.AreEqual(1, policy.ExecutionSet.MaximumCount);
        var source = new WorkflowMultiInstancePreparedSource([new("item-0001", 1, "1")], null, []);
        var snapshot = new WorkflowMultiInstanceSnapshot(source, DateTimeOffset.UtcNow, new(new Dictionary<string, Guid> { ["item-0001"] = user }));
        var json = JsonSerializer.Serialize(snapshot, WorkflowJsonSerializerContext.Default.WorkflowMultiInstanceSnapshot);
        var copy = JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.WorkflowMultiInstanceSnapshot)!;
        var now = DateTimeOffset.UtcNow; var item = new WorkflowSubprocessItemRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "host", "item-0001", 1,
            user, "{}", WorkflowSubprocessBindings.Hash("{}"), null, "pending", null, null, null, 1, now, now);
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("note", "text", false, new Dictionary<string, JsonElement>())])]);
        Assert.IsTrue(WorkflowMultiInstanceSnapshotRules.MatchesItems(policy, schema, new Dictionary<string, JsonElement>(), copy, [item], parent));
        Assert.IsFalse(WorkflowMultiInstanceSnapshotRules.MatchesItems(policy, schema, new Dictionary<string, JsonElement>(), copy with { Starters = null }, [item], parent));
        Assert.IsFalse(WorkflowMultiInstanceSnapshotRules.MatchesItems(policy, schema, new Dictionary<string, JsonElement>(), copy, [item with { StarterUserId = parent }], parent));
    }
    [TestMethod]
    public void Starter_backfill_uses_only_real_successes_and_cannot_overwrite_a_child_mapping()
    {
        var user = Guid.CreateVersion7(); var schema = new WorkflowFormSchema(1, 1, [new("main", [new("starterResult", "text", false, new Dictionary<string, JsonElement>())])]);
        var version = Guid.CreateVersion7(); var config = JsonSerializer.SerializeToElement(new { subprocessPolicy = new { definitionVersionId = version, inputFields = new { }, outputFields = new { } },
            fieldPolicies = new { starterResult = "editable" }, starterPolicy = new { source = new { kindKey = "parent" }, resultFieldKey = "starterResult" } });
        Assert.IsTrue(WorkflowSubprocessPolicy.TryRead(config, schema, out var policy));
        var now = DateTimeOffset.UtcNow; var item = new WorkflowSubprocessItemRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "host", "single", 1,
            user, "{}", WorkflowSubprocessBindings.Hash("{}"), Guid.CreateVersion7(), "succeeded", "{}", WorkflowSubprocessBindings.Hash("{}"), 2, 2, now, now);
        Assert.IsTrue(policy!.TryAddStarterOutput(schema, new Dictionary<string, JsonElement>(), [item], out var patch)); Assert.AreEqual(user.ToString("D"), patch["starterResult"].GetString());
        Assert.IsFalse(policy.TryAddStarterOutput(schema, new Dictionary<string, JsonElement>(), [item with { StatusKey = "running" }], out _));
        var collision = JsonSerializer.SerializeToElement(new { subprocessPolicy = new { definitionVersionId = version, inputFields = new { }, outputFields = new { starterResult = "child" } },
            fieldPolicies = new { starterResult = "editable" }, starterPolicy = new { source = new { kindKey = "parent" }, resultFieldKey = "starterResult" } });
        Assert.IsFalse(WorkflowSubprocessPolicy.TryRead(collision, schema, out _));
    }
    [TestMethod]
    public void Source_and_backfill_fields_participate_in_the_same_async_field_boundary()
    {
        var policy = new WorkflowSubprocessPolicy(Guid.CreateVersion7(), new Dictionary<string, string> { ["note"] = "title" }, new Dictionary<string, string>(),
            new Dictionary<string, string>(), new("parallel", "number_field", 2, null, "count", null, null, 100, "reject", new Dictionary<string, string>()), new("field_user", null, "owner", "actors"));
        CollectionAssert.AreEquivalent(new[] { "title", "count", "owner" }, policy.ReadFields.ToArray());
        CollectionAssert.AreEqual(new[] { "actors" }, policy.WrittenFields.ToArray());
    }
}
