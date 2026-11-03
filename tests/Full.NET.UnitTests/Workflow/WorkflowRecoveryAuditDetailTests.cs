using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>系统身份必须由真实任务/租约与闭合种类表达，不能伪造人工操作人。</summary>
[TestClass]
public sealed class WorkflowRecoveryAuditDetailTests
{
    [TestMethod]
    public void Detail_binds_task_instance_generation_and_attempt_and_keeps_error_as_a_string()
    {
        var task = Task(); using var json = JsonDocument.Parse(WorkflowRecoveryAuditDetail.Create(task, 2, "\"error\":true"));
        var value = json.RootElement; Assert.AreEqual("recovery_worker", value.GetProperty("sourceKey").GetString());
        Assert.AreEqual(task.Id, value.GetProperty("taskId").GetGuid()); Assert.AreEqual(task.InstanceId, value.GetProperty("instanceId").GetGuid());
        Assert.AreEqual(task.LeaseGeneration, value.GetProperty("leaseGeneration").GetInt32()); Assert.AreEqual(2, value.GetProperty("attempt").GetInt32());
        Assert.AreEqual(task.KindKey, value.GetProperty("kindKey").GetString()); Assert.AreEqual("\"error\":true", value.GetProperty("error").GetString());
    }
    [TestMethod]
    [DataRow("task")][DataRow("instance")][DataRow("lease")][DataRow("kind")][DataRow("attempt")]
    public void Missing_causal_facts_cannot_claim_a_system_audit(string defect)
    {
        var task = Task(); task = defect switch { "task" => task with { Id = Guid.Empty }, "instance" => task with { InstanceId = Guid.Empty },
            "lease" => task with { LeaseGeneration = 0 }, "kind" => task with { KindKey = "manual" }, _ => task };
        Assert.Throws<InvalidOperationException>(() => WorkflowRecoveryAuditDetail.Create(task, defect == "attempt" ? 0 : 1, null));
    }
    private static WorkflowRecoveryTaskRecord Task() => new(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), null,
        WorkflowRecoveryKinds.StuckInstance, "pending", 0, 1, "owner", DateTimeOffset.UtcNow.AddMinutes(1), 3, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
