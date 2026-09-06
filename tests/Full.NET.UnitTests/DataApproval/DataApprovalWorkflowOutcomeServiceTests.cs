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
            applier,
            Substitute.For<ISerialRuleDisableApprovalApplier>());
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

    /// <summary>流水号规则禁用业务类型应走同一终态处理路径。</summary>
    [TestMethod]
    public async Task Serial_rule_disable_business_type_routes_to_application_service()
    {
        var requestId = Guid.NewGuid();
        var row = CreateInReviewRow(requestId) with
        {
            ScenarioKey = DataApprovalScenarioKeys.SerialRuleHostDisable,
        };
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var updateApplier = Substitute.For<ISerialRuleChangeApprovalApplier>();
        var disableApplier = Substitute.For<ISerialRuleDisableApprovalApplier>();
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
        disableApplier.ApplyApprovedDisableAsync(
                row.TargetEntityId,
                row.AfterSnapshotJson,
                row.SubmittedByUserId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SerialNumberRuleResponse>.Success(new SerialNumberRuleResponse(
                row.TargetEntityId,
                "rule.key",
                "Rule",
                null,
                SerialNumberRuleScope.Host,
                SerialNumberResetInterval.Day,
                "P-{sequence:3}",
                1,
                999,
                1,
                false,
                now,
                row.SubmittedByUserId,
                now,
                row.SubmittedByUserId,
                2)));
        commandExecutor.ExecuteAsync(
                DataApprovalSql.CompleteApprovedApplication,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(row with
            {
                StatusKey = DataApprovalStatusKeys.Approved,
                ApplicationStatusKey = DataApprovalApplicationStatusKeys.Applied,
                Version = row.Version + 2
            });

        var applicationService = new DataApprovalRequestApplicationService(
            queryExecutor,
            commandExecutor,
            clock,
            updateApplier,
            disableApplier);
        var service = new DataApprovalWorkflowOutcomeService(
            queryExecutor,
            commandExecutor,
            clock,
            applicationService);

        await service.HandleTerminalWorkflowAsync(
            Guid.Empty,
            DataApprovalWorkflowBusinessTypes.SerialRuleDisable,
            requestId.ToString("D"),
            "completed",
            row.SubmittedByUserId,
            "message-id");

        await disableApplier.Received(1).ApplyApprovedDisableAsync(
            row.TargetEntityId,
            row.AfterSnapshotJson,
            row.SubmittedByUserId,
            "message-id",
            Arg.Any<CancellationToken>());
        await updateApplier.DidNotReceive().ApplyApprovedUpdateAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
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
