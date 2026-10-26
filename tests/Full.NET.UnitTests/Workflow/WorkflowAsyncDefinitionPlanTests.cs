using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.UnitTests.Workflow;

/// <summary>异步声明不能读取未就绪字段，独立后继保持前进并由结构边界收敛。</summary>
[TestClass]
public sealed class WorkflowAsyncDefinitionPlanTests
{
    [TestMethod]
    public void Publication_rejects_dependency_metadata_without_a_supported_preceding_async_activity()
    {
        var draft = new WorkflowDefinitionDraft(1, [Node("start", "start", new { nextNodeKeys = new[] { "empty" } }),
            Node("empty", "control.empty", new { nextNodeKeys = new[] { "end" } }) with { AwaitActivityNodeKeys = ["missing"] }, Node("end", "end", new { })], "automation");
        Assert.AreEqual(WorkflowErrorCodes.DefinitionAsyncDependencyInvalid, WorkflowDefinitionCompiler.Compile(draft).ErrorCode);
    }
    [TestMethod]
    public void A_result_consumer_requires_an_explicit_causal_dependency()
    {
        var nodes = new[] { Producer("call", "consumer"), Mutation("consumer", "count", "end"), Node("end", "end", new { }) };
        Assert.IsFalse(Compile(nodes, out _));
        nodes[1] = nodes[1] with { AwaitActivityNodeKeys = ["call"] };
        Assert.IsTrue(Compile(nodes, out var waits));
        CollectionAssert.AreEqual(new[] { "call" }, waits["consumer"].ProducerNodeKeys.ToArray());
        Assert.IsFalse(waits["consumer"].IsImplicitBoundary); Assert.IsFalse(waits.ContainsKey("end"));
    }

    [TestMethod]
    public void Independent_mutation_proceeds_and_end_waits_even_without_a_result_consumer()
    {
        Assert.IsTrue(Compile([Producer("call", "independent"), Mutation("independent", "title", "end"), Node("end", "end", new { })], out var waits));
        Assert.IsFalse(waits.ContainsKey("independent")); Assert.IsTrue(waits["end"].IsImplicitBoundary);
        CollectionAssert.AreEqual(new[] { "call" }, waits["end"].ProducerNodeKeys.ToArray());
        Assert.IsTrue(Compile([Producer("call", "end", noOutput: true), Node("end", "end", new { })], out waits));
        Assert.HasCount(1, waits["end"].ProducerNodeKeys);
    }

    [TestMethod]
    public void Human_hidden_fields_allow_independent_work_but_assignee_sources_still_consume_results()
    {
        var human = Node("human", "human.task", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { count = "hidden" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "initiator" } } } });
        Assert.IsTrue(Compile([Producer("call", "human"), human, Node("end", "end", new { })], out var waits)); Assert.IsFalse(waits.ContainsKey("human"));
        human = Node("human", "human.task", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { count = "hidden" }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "form_users", fieldKey = "count" } } } });
        Assert.IsFalse(Compile([Producer("call", "human"), human, Node("end", "end", new { })], out _));
    }

    [TestMethod]
    public void Overlapping_async_writes_are_serialized_by_an_explicit_wait()
    {
        var second = Producer("second", "end");
        Assert.IsFalse(Compile([Producer("first", "second"), second, Node("end", "end", new { })], out _));
        Assert.IsTrue(Compile([Producer("first", "second"), second with { AwaitActivityNodeKeys = ["first"] }, Node("end", "end", new { })], out var waits));
        CollectionAssert.AreEqual(new[] { "first" }, waits["second"].ProducerNodeKeys.ToArray());
        CollectionAssert.AreEqual(new[] { "second" }, waits["end"].ProducerNodeKeys.ToArray());
    }

    [TestMethod]
    public void Conditional_activation_does_not_become_a_guaranteed_dependency_after_merge()
    {
        var nodes = new[] { Node("choose", "gateway.exclusive", new { nextNodeKeys = new[] { "call", "skip" } }), Producer("call", "consumer"),
            Node("skip", "control.empty", new { nextNodeKeys = new[] { "consumer" } }), Mutation("consumer", "count", "end") with { AwaitActivityNodeKeys = ["call"] }, Node("end", "end", new { }) };
        Assert.IsFalse(Compile(nodes, out _));
    }

    [TestMethod]
    [DataRow("call")]
    [DataRow("future")]
    [DataRow("missing")]
    public void Self_future_and_unknown_dependencies_fail_closed(string source)
    {
        Assert.IsFalse(Compile([Producer("call", "future") with { AwaitActivityNodeKeys = [source] }, Producer("future", "end"), Node("end", "end", new { })], out _));
    }

    [TestMethod]
    public void Data_constants_do_not_create_fake_field_reads()
    {
        var independent = Node("independent", "data.modify", new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { title = "editable" },
            mutationPolicy = new { assignments = new[] { new { fieldKey = "title", value = new { fieldKey = "count" } } } } });
        Assert.IsTrue(Compile([Producer("call", "independent"), independent, Node("end", "end", new { })], out var waits)); Assert.IsFalse(waits.ContainsKey("independent"));
    }

    [TestMethod]
    public void Structural_fork_and_join_settle_their_actual_pending_sources()
    {
        var fork = Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] {
            new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } });
        Assert.IsTrue(Compile([Producer("root", "fork"), fork, Node("left", "control.empty", new { nextNodeKeys = new[] { "join" } }),
            Producer("right", "join", noOutput: true), Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })], out var waits));
        CollectionAssert.AreEqual(new[] { "root" }, waits["fork"].ProducerNodeKeys.ToArray());
        CollectionAssert.AreEqual(new[] { "right" }, waits["join"].ProducerNodeKeys.ToArray()); Assert.IsFalse(waits.ContainsKey("end"));
    }

    [TestMethod]
    public void Declared_dependencies_are_unique_and_bounded_but_terminal_waits_cover_all_required_calls()
    {
        Assert.IsFalse(Compile([Producer("call", "consumer", noOutput: true), Node("consumer", "control.empty", new { nextNodeKeys = new[] { "end" } }) with { AwaitActivityNodeKeys = ["call", "call"] }, Node("end", "end", new { })], out _));
        var sources = Enumerable.Range(0, 17).Select(index => "call" + index).ToArray();
        var nodes = sources.Select((key, index) => Producer(key, index == sources.Length - 1 ? "end" : sources[index + 1], noOutput: true)).Append(Node("end", "end", new { })).ToArray();
        Assert.IsTrue(Compile(nodes, out var waits)); Assert.HasCount(17, waits["end"].ProducerNodeKeys);
        nodes[^1] = nodes[^1] with { AwaitActivityNodeKeys = sources }; Assert.IsFalse(Compile(nodes, out _));
    }

    private static bool Compile(WorkflowNodeDraft[] nodes, out IReadOnlyDictionary<string, WorkflowAsyncWaitDefinition> waits) =>
        WorkflowAsyncDefinitionPlan.TryCompile(new(1, nodes, "automation"), nodes.ToDictionary(node => node.NodeKey, ReadEdges, StringComparer.Ordinal), out waits);
    private static string[] ReadEdges(WorkflowNodeDraft node) => node.Config.TryGetProperty("gatewayRoleKey", out var role) && role.GetString() == "fork"
        ? node.Config.GetProperty("branches").EnumerateArray().Select(branch => branch.GetProperty("nextNodeKey").GetString()!).ToArray()
        : node.Config.TryGetProperty("nextNodeKeys", out var next) ? next.EnumerateArray().Select(value => value.GetString()!).ToArray() : [];
    private static WorkflowNodeDraft Producer(string key, string next, bool noOutput = false) => Node(key, "integration.async_trigger", new { nextNodeKeys = new[] { next },
        fieldPolicies = new { title = "readOnly", count = "editable" }, connectorPolicy = new { connectorKey = "credit.check", connectorVersion = 1,
            inputFields = new { subject = "title" }, outputFields = noOutput ? new Dictionary<string, string>() : new() { ["count"] = "score" } } });
    private static WorkflowNodeDraft Mutation(string key, string target, string next) => Node(key, "data.modify", new { nextNodeKeys = new[] { next },
        fieldPolicies = new Dictionary<string, string> { [target] = "editable" }, mutationPolicy = new { assignments = new[] { new { fieldKey = target, value = "constant" } } } });
    private static WorkflowNodeDraft Node(string key, string type, object config) => new(key, type, 1, JsonSerializer.SerializeToElement(config));
}
