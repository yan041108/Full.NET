using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>办理完成门槛和审批同意票是独立协议，不允许未知规则或隐式数字转换。</summary>
[TestClass]
public sealed class WorkflowTaskPolicyTests
{
    [TestMethod]
    [DataRow("all", 3, 3)]
    [DataRow("any", 3, 1)]
    [DataRow("sequential", 3, 3)]
    [DataRow("all", 1, 1)]
    [DataRow("unknown", 3, 0)]
    [DataRow("all", 21, 0)]
    [DataRow("all", 0, 0)]
    public void Required_submissions_are_frozen_from_actual_members(string mode, int count, int required) =>
        Assert.AreEqual(required, new WorkflowTaskPolicy(mode).RequiredCompletions(count));

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"taskPolicy\":{\"completionModeKey\":1}}")]
    [DataRow("{\"taskPolicy\":{\"completionModeKey\":\"approve\"}}")]
    [DataRow("{\"taskPolicy\":{\"completionModeKey\":\"all\",\"script\":true}}")]
    [DataRow("{\"taskPolicy\":{\"completionModeKey\":\"all\",\"completionModeKey\":\"any\"}}")]
    public void Malformed_task_configuration_fails_closed(string json) =>
        Assert.IsFalse(WorkflowTaskPolicy.TryRead(JsonSerializer.Deserialize<JsonElement>(json), out _));

    [TestMethod]
    public void Task_policy_allows_completion_and_denies_approval()
    {
        Assert.IsTrue(WorkflowHumanActionPolicy.TryRead(JsonSerializer.SerializeToElement(new { taskPolicy = new { completionModeKey = "all" } }),
            out var actions, WorkflowHumanActivityKinds.Task));
        Assert.IsTrue(actions!.Validate("complete", null, null).IsSuccess);
        Assert.IsFalse(actions.Validate("approve", null, null).IsSuccess);
        Assert.IsFalse(WorkflowHumanActionPolicy.TryRead(JsonSerializer.SerializeToElement(new { operationPermissions = new { approve = true } }),
            out _, WorkflowHumanActivityKinds.Task));
    }
}
