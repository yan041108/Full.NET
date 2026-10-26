using System.Text.Json;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Features.WorkflowCommands;

namespace Full.NET.UnitTests.SerialNumbers;

/// <summary>同一所有者授权输入规范化后摘要稳定，不能重用调用身份替换目标、主体或快照。</summary>
[TestClass]
public sealed class SerialRuleCommandSnapshotTests
{
    [TestMethod]
    public void Formatting_does_not_change_input_identity_but_business_inputs_do()
    {
        var command = new DataApprovalScenarioCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), DataApprovalScenarioKeys.SerialRuleHostDisable,
            Guid.CreateVersion7(), "{\"version\":7}", Guid.CreateVersion7());
        Assert.IsTrue(SerialRuleCommandSnapshot.TryCreate(command, out var first));
        Assert.IsTrue(SerialRuleCommandSnapshot.TryCreate(command with { SnapshotJson = "{ \"version\" : 7 }" }, out var same));
        Assert.AreEqual(first!.InputHash, same!.InputHash);
        foreach (var changed in new[] { command with { TargetEntityId = Guid.CreateVersion7() }, command with { ApprovalRequestId = Guid.CreateVersion7() },
            command with { RequestedByUserId = Guid.CreateVersion7() }, command with { SnapshotJson = "{\"version\":8}" } }) {
            Assert.IsTrue(SerialRuleCommandSnapshot.TryCreate(changed, out var parsed)); Assert.AreNotEqual(first.InputHash, parsed!.InputHash);
        }
    }
    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{\"version\":1,\"version\":2}")]
    [DataRow("{\"version\":1,\"table\":\"other\"}")]
    [DataRow("{\"version\":0}")]
    [DataRow("{\"version\":\"1\"}")]
    [DataRow("{\"version\":9223372036854775806}")]
    public void Invalid_or_ambiguous_disable_snapshot_is_rejected(string json)
    {
        var command = new DataApprovalScenarioCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), DataApprovalScenarioKeys.SerialRuleHostDisable,
            Guid.CreateVersion7(), json, Guid.CreateVersion7());
        Assert.IsFalse(SerialRuleCommandSnapshot.TryCreate(command, out _));
    }
}
