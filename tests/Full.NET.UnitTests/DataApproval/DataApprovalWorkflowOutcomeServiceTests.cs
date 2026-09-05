using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalWorkflowOutcomeServiceTests
{
    /// <summary>工作流批准后若业务应用冲突，不得把请求直接标记为 approved。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task Approved_workflow_with_version_conflict_does_not_mark_request_as_approved()
    {
        var requestId = Guid.NewGuid();
        var row = CreateInReviewRow(requestId);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var applier = Substitute.For<ISerialRuleChangeApprovalApplier>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestByBusinessId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(row);
        commandExecutor.ExecuteAsync(
                DataApprovalSql.MarkApplicationPending,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(row with
            {
                ApplicationStatusKey = DataApprovalApplicationStatusKeys.PendingApply,
                Version = row.Version + 1
            });
        applier.ApplyApprovedUpdateAsync(
                row.TargetEntityId,
                row.AfterSnapshotJson,
                row.SubmittedByUserId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SerialNumberRuleResponse>.Failure(new Error(
                SerialNumberErrorCodes.RuleVersionConflict,
                "conflict",
                ErrorType.Conflict)));
        commandExecutor.ExecuteAsync(
                DataApprovalSql.RecordApplicationFailure,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var applicationService = new DataApprovalRequestApplicationService(
            queryExecutor,
            commandExecutor,
            clock,
            applier);
        var service = new DataApprovalWorkflowOutcomeService(
            queryExecutor,
            commandExecutor,
            clock,
            applicationService);

        await service.HandleTerminalWorkflowAsync(
            Guid.Empty,
            DataApprovalWorkflowBusinessTypes.SerialRuleUpdate,
            requestId.ToString("D"),
            "completed",
            row.SubmittedByUserId,
            "message-id");

        await commandExecutor.DidNotReceive().ExecuteAsync(
            DataApprovalSql.CompleteApprovedApplication,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await commandExecutor.DidNotReceive().ExecuteAsync(
            DataApprovalSql.UpdateStatus,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static DataApprovalRequestRecord CreateInReviewRow(Guid requestId)
    {
        var now = DateTimeOffset.UtcNow;
        return new DataApprovalRequestRecord(
            requestId,
            null,
            "host",
            "host",
            DataApprovalScenarioKeys.SerialRuleHostUpdate,
            Guid.NewGuid(),
            DataApprovalStatusKeys.InReview,
            null,
            "{}",
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            null,
            "approval-key",
            DataApprovalRecoveryStatusKeys.None,
            null,
            null,
            null,
            0,
            DataApprovalApplicationStatusKeys.None,
            null,
            null,
            null,
            0,
            now,
            now,
            2);
    }
}
