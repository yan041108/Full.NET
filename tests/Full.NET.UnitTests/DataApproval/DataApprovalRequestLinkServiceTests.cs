using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Features.ManageRequests;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.Workflow.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalRequestLinkServiceTests
{
    /// <summary>已关联工作流时应直接返回当前行，不重复启动。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task TryLinkWorkflowAsync_returns_existing_row_when_already_linked()
    {
        var workflowInstanceId = Guid.NewGuid();
        var row = CreatePendingRow(workflowInstanceId: workflowInstanceId, statusKey: DataApprovalStatusKeys.InReview);
        var workflowStarter = Substitute.For<IWorkflowInstanceStarter>();
        var service = CreateService(workflowStarter);

        var result = await service.TryLinkWorkflowAsync(row);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(workflowInstanceId, result.Value!.WorkflowInstanceId);
        await workflowStarter.DidNotReceiveWithAnyArgs().StartAsync(default, default!, default);
    }

    /// <summary>工作流启动失败时应持久化可重试失败并返回成功响应。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task TryLinkWorkflowAsync_records_retryable_failure_when_workflow_start_fails()
    {
        var row = CreatePendingRow();
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var workflowStarter = Substitute.For<IWorkflowInstanceStarter>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        workflowStarter.StartAsync(
                row.SubmittedByUserId,
                Arg.Any<StartWorkflowInstanceCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<WorkflowInstanceLifecycleResult>.Failure(new Error(
                "workflow.start_failed",
                "start failed",
                ErrorType.Conflict)));
        commandExecutor.ExecuteAsync(
                DataApprovalSql.RecordRecoveryFailure,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var failedRow = row with
        {
            RecoveryStatusKey = DataApprovalRecoveryStatusKeys.FailedRetryable,
            LastFailureCode = "workflow.start_failed",
            LastFailureMessage = "start failed",
            LastRecoveryAttemptAtUtc = now,
            RecoveryAttemptCount = 1,
            Version = row.Version + 1
        };
        queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(failedRow);
        var service = new DataApprovalRequestLinkService(
            queryExecutor,
            commandExecutor,
            clock,
            workflowStarter);

        var result = await service.TryLinkWorkflowAsync(row);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(DataApprovalRecoveryStatusKeys.FailedRetryable, result.Value!.RecoveryStatusKey);
        await commandExecutor.Received(1).ExecuteAsync(
            DataApprovalSql.RecordRecoveryFailure,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>终态恢复状态不得再次尝试关联。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task TryLinkWorkflowAsync_rejects_terminal_recovery_state()
    {
        var row = CreatePendingRow(recoveryStatusKey: DataApprovalRecoveryStatusKeys.FailedTerminal);
        var service = CreateService(Substitute.For<IWorkflowInstanceStarter>());

        var result = await service.TryLinkWorkflowAsync(row);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(DataApprovalErrorCodes.RecoveryNotRetryable, result.Error!.Code);
    }

    private static DataApprovalRequestLinkService CreateService(IWorkflowInstanceStarter workflowStarter) =>
        new(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            Substitute.For<IClock>(),
            workflowStarter);

    private static DataApprovalRequestRecord CreatePendingRow(
        Guid? workflowInstanceId = null,
        string? statusKey = null,
        string? recoveryStatusKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new DataApprovalRequestRecord(
            Guid.NewGuid(),
            null,
            "host",
            "host",
            DataApprovalScenarioKeys.SerialRuleHostUpdate,
            Guid.NewGuid(),
            statusKey ?? DataApprovalStatusKeys.Pending,
            null,
            "{}",
            workflowInstanceId,
            workflowInstanceId is null ? null : 1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            null,
            "approval-key",
            recoveryStatusKey ?? DataApprovalRecoveryStatusKeys.PendingLink,
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
            1);
    }
}
