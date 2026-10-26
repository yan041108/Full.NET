using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>发布动作配置只收紧平台权限，意见和签名指针不能被客户端按钮绕过。</summary>
[TestClass]
public sealed class WorkflowHumanActionPolicyTests
{
    [TestMethod]
    [DataRow("human.approval")]
    [DataRow("human.group_approval")]
    [DataRow("human.task")]
    [DataRow("human.vote")]
    [DataRow("human.claim")]
    public void Delegation_is_a_separate_published_action_and_cannot_grant_business_decisions(string kind)
    {
        Assert.IsTrue(WorkflowHumanActionPolicy.TryRead(JsonSerializer.SerializeToElement(new {
            operationPermissions = new { @delegate = true, transfer = false }, signRequired = true, commentRequired = true
        }), out var policy, kind));
        Assert.IsTrue(policy!.Actions.SetEquals(["delegate"]));
        Assert.AreEqual(WorkflowErrorCodes.TodoActionForbidden, policy.Validate("transfer", "原因", Guid.CreateVersion7()).Error?.Code);
        Assert.AreEqual(WorkflowErrorCodes.TodoCommentRequired, policy.Validate("delegate", null, Guid.CreateVersion7()).Error?.Code);
        Assert.AreEqual(WorkflowErrorCodes.TodoSignatureRequired, policy.Validate("delegate", "委托", null).Error?.Code);
        Assert.IsTrue(policy.Validate("delegate", "委托", Guid.CreateVersion7()).IsSuccess);
    }

    [TestMethod]
    public void Published_action_and_required_evidence_are_independent_checks()
    {
        Assert.IsTrue(WorkflowHumanActionPolicy.TryRead(JsonSerializer.SerializeToElement(new {
            operationPermissions = new { approve = true, reject = false }, signRequired = true, commentRequired = true
        }), out var policy));
        Assert.AreEqual(WorkflowErrorCodes.TodoActionForbidden, policy!.Validate("reject", "原因", Guid.CreateVersion7()).Error?.Code);
        Assert.AreEqual(WorkflowErrorCodes.TodoCommentRequired, policy.Validate("approve", " ", Guid.CreateVersion7()).Error?.Code);
        Assert.AreEqual(WorkflowErrorCodes.TodoSignatureRequired, policy.Validate("approve", "同意", null).Error?.Code);
        Assert.AreEqual(WorkflowErrorCodes.TodoSignatureRequired, policy.Validate("approve", "同意", Guid.Empty).Error?.Code);
        Assert.IsTrue(policy.Validate("approve", "同意", Guid.CreateVersion7()).IsSuccess);
    }

    [TestMethod]
    [DataRow("{\"signRequired\":\"true\"}")]
    [DataRow("{\"operationPermissions\":{\"admin\":true}}")]
    [DataRow("{\"operationPermissions\":{\"approve\":1}}")]
    [DataRow("{\"operationPermissions\":{\"approve\":true,\"approve\":false}}")]
    public void Unknown_or_malformed_policy_is_rejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowHumanActionPolicy.TryRead(document.RootElement, out _));
    }
}
