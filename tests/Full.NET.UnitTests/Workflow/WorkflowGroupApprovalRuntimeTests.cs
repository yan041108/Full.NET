using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>分组活动是独立人审等待点，根门槛及字段策略进入不可变发布版本。</summary>
[TestClass]
public sealed class WorkflowGroupApprovalRuntimeTests
{
    [TestMethod]
    [DataRow("all")]
    [DataRow("any")]
    [DataRow("percent")]
    public void Group_node_compiles_and_resolves_as_real_human_approval(string mode)
    {
        var draft = Draft(mode);
        var compiled = WorkflowDefinitionCompiler.Compile(draft);
        Assert.IsTrue(compiled.IsSuccess, compiled.ErrorCode);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        Assert.IsTrue(plan!.TryResolveStart(out var transition));
        Assert.AreEqual("review", transition.NextHumanNodeKey);
        Assert.AreEqual("group", transition.ApprovalPolicy!.ModeKey);
        Assert.HasCount(2, transition.ApprovalPolicy.GroupConfiguration!.Groups);
        Assert.IsTrue(plan.ContainsHumanNode("review"));
        Assert.IsTrue(plan.TryResolveAfterHuman("review", out var completion));
        Assert.IsTrue(completion.CompletesInstance);
        Assert.IsTrue(plan.TryGetHumanTimeoutPolicy("review", out _));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"nextNodeKeys\":[\"end\"],\"approvalPolicy\":{\"modeKey\":\"all\",\"approverUserIds\":[]}}")]
    public void Missing_group_definition_never_falls_back_to_initiator(string json)
    {
        var draft = Draft("all");
        draft = draft with { Nodes = draft.Nodes.Select(node => node.NodeKey == "review"
            ? node with { Config = JsonSerializer.Deserialize<JsonElement>(json) } : node).ToArray() };
        Assert.IsFalse(WorkflowDefinitionCompiler.Compile(draft).IsSuccess);
        Assert.IsFalse(WorkflowRuntimePlan.TryCreate(draft, out _));
    }

    [TestMethod]
    public void Group_field_and_action_policies_use_the_same_node_boundary()
    {
        var draft = Draft("all");
        var json = JsonSerializer.Serialize(draft, Full.NET.Modules.Workflow.Serialization.WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        Assert.IsTrue(WorkflowHumanActionPolicy.TryResolve(json, "review", out var policy));
        Assert.IsTrue(policy!.CommentRequired);
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("title", "text", false, new Dictionary<string, JsonElement>())])]);
        Assert.IsTrue(WorkflowNodeFieldPolicy.TryResolve(json, "review", schema, out _));
    }

    private static WorkflowDefinitionDraft Draft(string mode) => new(1,
    [
        new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "review" } })),
        new("review", "human.group_approval", 1, JsonSerializer.SerializeToElement(new
        {
            nextNodeKeys = new[] { "end" }, commentRequired = true,
            groups = new object[] { new { groupKey = "first", assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } } } },
                new { groupKey = "second", assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { Guid.CreateVersion7() } } } } } },
            groupApprovalPolicy = mode == "percent" ? new Dictionary<string, object> { ["modeKey"] = mode, ["requiredPercentage"] = 67 }
                : new Dictionary<string, object> { ["modeKey"] = mode }
        })),
        new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))
    ]);
}
