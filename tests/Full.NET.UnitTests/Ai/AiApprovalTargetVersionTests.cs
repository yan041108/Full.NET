using Full.NET.AI.Abstractions.Tools;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiApprovalCurrentAdmission。</summary>
public sealed partial class AiApprovalCurrentAdmissionTests
{
    [TestMethod]
    [DataRow("restore", 2)]
    [DataRow("restore", 3)]
    [DataRow("dispatch", 2)]
    [DataRow("dispatch", 3)]
    [DataRow("consume", 2)]
    [DataRow("consume", 3)]
    public async Task Approval_rejects_same_title_after_target_revision_changes(string boundary, int revision)
    {
        using var f = new Fixture();
        f.Target = new() { Id = f.TargetId, OwnerUserId = f.Actor.UserId, Title = "old", Version = revision };
        await AssertRejectedAsync(f, boundary);
    }

    [TestMethod]
    [DataRow("restore")]
    [DataRow("dispatch")]
    [DataRow("consume")]
    public async Task Approval_rejects_legacy_snapshot_without_inventing_target_revision(string boundary)
    {
        using var f = new Fixture();
        f.Record.PresentationJson = """{"action":"Rename","target":"old","change":"new","scope":"owned","costCeiling":null,"currency":null}""";
        var original = f.Record.PresentationJson;
        await AssertRejectedAsync(f, boundary);
        Assert.AreEqual(original, f.Record.PresentationJson);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("null")]
    [DataRow("2147483648")]
    [DataRow("\"1\"")]
    [DataRow("1.5")]
    [DataRow("1,\"targetVersion\":1")]
    public async Task Approval_rejects_invalid_or_duplicate_target_revision(string revision)
    {
        using var f = new Fixture();
        f.Record.PresentationJson = "{\"action\":\"Rename\",\"target\":\"old\",\"change\":\"new\",\"scope\":\"owned\",\"costCeiling\":null,\"currency\":null,\"targetVersion\":" + revision + "}";
        await AssertRejectedAsync(f, "restore");
        await AssertRejectedAsync(f, "dispatch");
        await AssertRejectedAsync(f, "consume");
    }

    private static async Task AssertRejectedAsync(Fixture f, string boundary)
    {
        if (boundary == "restore") Assert.IsFalse(await f.Gate.HasApprovedUnconsumedForRunAsync(f.RunId, f.Actor.UserId));
        if (boundary == "dispatch") Assert.AreEqual(AgentApprovalExecutionStatus.Denied,
            await f.Consumption.ValidateForToolExecutionAsync(f.Invocation, f.Actor, default));
        if (boundary == "consume") Assert.IsFalse(await f.Consumption.TryConsumeForToolExecutionAsync(f.Invocation, f.Actor, default));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
}
