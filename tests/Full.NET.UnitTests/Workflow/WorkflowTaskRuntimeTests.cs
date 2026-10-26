using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>办理可以作为自动类流程等待点，但不能伪装成审批类的人审通过屏障。</summary>
[TestClass]
public sealed class WorkflowTaskRuntimeTests
{
    [TestMethod]
    [DataRow("all")]
    [DataRow("any")]
    [DataRow("sequential")]
    public void Task_runtime_preserves_completion_policy_and_never_satisfies_approval_barrier(string mode)
    {
        var draft = new WorkflowDefinitionDraft(1,
        [
            new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "task" } })),
            new("task", "human.task", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = mode } })),
            new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))
        ], "automation");
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(draft).IsSuccess);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, out var plan));
        Assert.IsTrue(plan!.TryResolveStart(out var transition)); Assert.AreEqual("task", transition.NextHumanNodeKey);
        Assert.AreEqual(mode, transition.TaskPolicy!.CompletionModeKey); Assert.IsNull(transition.ApprovalPolicy);
        Assert.IsTrue(plan.TryResolveAfterHuman("task", out var end)); Assert.IsTrue(end.CompletesInstance);
        Assert.IsFalse(WorkflowDefinitionCompiler.Compile(draft with { WorkflowKindKey = "approval" }).IsSuccess);
    }
}
