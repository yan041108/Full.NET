using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.SerialNumbers.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalRequestApplicationServiceTests
{
    /// <summary>版本冲突时不得把审批请求标记为 approved，并应持久化可重试应用失败。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task Version_conflict_records_retryable_application_failure_without_approving_request()
    {
        var row = CreateInReviewRow();
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var applier = Substitute.For<ISerialRuleChangeApprovalApplier>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
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
        var failedRow = row with
        {
            ApplicationStatusKey = DataApprovalApplicationStatusKeys.FailedRetryable,
            LastApplicationFailureCode = SerialNumberErrorCodes.RuleVersionConflict,
            LastApplicationFailureMessage = "conflict",
            ApplicationAttemptCount = 1,
            Version = row.Version + 2
        };
        queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(failedRow);

        var service = new DataApprovalRequestApplicationService(
            queryExecutor,
            commandExecutor,
            clock,
            applier);

        var result = await service.TryApplyApprovedChangeAsync(
            row,
            row.SubmittedByUserId,
            "message-id");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(DataApprovalStatusKeys.InReview, result.Value!.StatusKey);
        Assert.AreEqual(DataApprovalApplicationStatusKeys.FailedRetryable, result.Value.ApplicationStatusKey);
        await commandExecutor.DidNotReceive().ExecuteAsync(
            DataApprovalSql.CompleteApprovedApplication,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static DataApprovalRequestRecord CreateInReviewRow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DataApprovalRequestRecord(
            Guid.NewGuid(),
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
