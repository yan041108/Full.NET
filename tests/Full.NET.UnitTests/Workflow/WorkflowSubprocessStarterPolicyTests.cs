using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>发起人来源不可用任意数组或空UUID冒充，集合项只有角色身份来源可用于逐项委托。</summary>
[TestClass]
public sealed class WorkflowSubprocessStarterPolicyTests
{
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
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("starter", "select", false, new Dictionary<string, JsonElement>())])]);
        var config = JsonSerializer.SerializeToElement(new { source = new { kindKey = "field_user", fieldKey = "starter" } });
        Assert.IsTrue(WorkflowSubprocessStarterPolicy.TryRead(config, schema, null, out var policy));
        var user = Guid.CreateVersion7(); var input = new Dictionary<string, JsonElement> { ["starter"] = JsonSerializer.SerializeToElement(user.ToString("D")) };
        Assert.IsTrue(policy!.TryResolve(Guid.CreateVersion7(), input, new("single", 1, "1"), out var starter)); Assert.AreEqual(user, starter);
        input["starter"] = JsonSerializer.SerializeToElement(new[] { user }); Assert.IsFalse(policy.TryResolve(user, input, new("single", 1, "1"), out _));
        Assert.IsFalse(WorkflowSubprocessStarterPolicy.TryRead(JsonSerializer.SerializeToElement(new { source = new { kindKey = "parent", userId = user } }), null, null, out _));
    }
}
