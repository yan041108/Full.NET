using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>来源监听器必须映射为闭合动作，未知脚本、动作和歧义配置明确拒绝。</summary>
[TestClass]
public sealed class WorkflowActivityListenerPolicyTests
{
    [TestMethod]
    [DataRow("human.approval")]
    [DataRow("human.group_approval")]
    [DataRow("human.task")]
    [DataRow("human.vote")]
    [DataRow("human.claim")]
    public void Human_activity_accepts_only_versioned_static_final_events(string kind)
    {
        var config = JsonSerializer.SerializeToElement(new { listeners = new[] {
            new { listenerKey = "passed", eventKey = "completed", actionKey = "notify.initiator", actionVersion = 1 },
            new { listenerKey = "failed", eventKey = "rejected", actionKey = "notify.initiator", actionVersion = 1 } } });
        Assert.IsTrue(WorkflowActivityListenerPolicy.TryRead(kind, config, out var listeners));
        Assert.HasCount(2, listeners);
        Assert.IsFalse(WorkflowActivityListenerPolicy.TryRead("control.empty", config, out _));
    }
    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[{\"listenerKey\":\"a\",\"eventKey\":\"started\",\"actionKey\":\"notify.initiator\",\"actionVersion\":1}]")]
    [DataRow("[{\"listenerKey\":\"a\",\"eventKey\":\"completed\",\"actionKey\":\"eval\",\"actionVersion\":1}]")]
    [DataRow("[{\"listenerKey\":\"a\",\"eventKey\":\"completed\",\"actionKey\":\"notify.initiator\",\"actionVersion\":\"1\"}]")]
    [DataRow("[{\"listenerKey\":\"a\",\"eventKey\":\"completed\",\"actionKey\":\"notify.initiator\",\"actionVersion\":2}]")]
    [DataRow("[{\"listenerKey\":\"a\",\"eventKey\":\"completed\",\"actionKey\":\"notify.initiator\",\"actionVersion\":1,\"script\":\"run()\"}]")]
    [DataRow("[{\"listenerKey\":\"a\",\"listenerKey\":\"b\",\"eventKey\":\"completed\",\"actionKey\":\"notify.initiator\",\"actionVersion\":1}]")]
    public void Unsafe_or_unsupported_listener_is_rejected(string json)
    {
        using var document = JsonDocument.Parse("{\"listeners\":" + json + "}");
        Assert.IsFalse(WorkflowActivityListenerPolicy.TryRead("human.approval", document.RootElement, out _));
    }
    [TestMethod]
    public void Duplicate_listener_identity_or_collection_is_rejected()
    {
        const string item = "{\"listenerKey\":\"a\",\"eventKey\":\"completed\",\"actionKey\":\"notify.initiator\",\"actionVersion\":1}";
        foreach (var json in new[] { "{\"listeners\":[" + item + "," + item + "]}", "{\"listeners\":[],\"listeners\":[]}" })
        {
            using var document = JsonDocument.Parse(json);
            Assert.IsFalse(WorkflowActivityListenerPolicy.TryRead("human.approval", document.RootElement, out _));
        }
    }
}
