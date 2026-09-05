using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Features;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalWorkflowOutcomeServiceTests
{
    /// <summary>验证规则版本冲突时不得把审批请求错误收敛为已批准。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task Approved_serial_rule_version_conflict_does_not_resolve_request_as_approved()
    {
        var requestId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var applier = Substitute.For<ISerialRuleChangeApprovalApplier>();
        var row = new DataApprovalRequestRecord(
            requestId,
            null,
            "host",
            "host",
            DataApprovalScenarioKeys.SerialRuleHostUpdate,
            targetId,
            DataApprovalStatusKeys.InReview,
            null,
            "{}",
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            actorUserId,
            DateTimeOffset.UtcNow,
            null,
            "approval-key",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            2);
        queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(row);
        applier.ApplyApprovedUpdateAsync(
                targetId,
                row.AfterSnapshotJson,
                actorUserId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SerialNumberRuleResponse>.Failure(new Error(
                SerialNumberErrorCodes.RuleVersionConflict,
                "conflict",
                ErrorType.Conflict)));

        var service = new DataApprovalWorkflowOutcomeService(
            queryExecutor,
            commandExecutor,
            clock,
            applier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.HandleTerminalWorkflowAsync(
                Guid.Empty,
                DataApprovalWorkflowBusinessTypes.SerialRuleUpdate,
                requestId.ToString("D"),
                "completed",
                actorUserId,
                "message-id"));

        StringAssert.Contains(exception.Message, SerialNumberErrorCodes.RuleVersionConflict);
        await commandExecutor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }
}
