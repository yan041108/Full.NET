using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowRecoveryTaskIdentityTests
{
    [TestMethod]
    public async Task Retry_rejects_receipt_bound_to_another_task_in_same_instance()
        => await AssertReceiptAsync(false, false, false);

    [TestMethod]
    public async Task Reconcile_rejects_receipt_bound_to_another_task_in_same_instance()
        => await AssertReceiptAsync(true, false, false);

    [TestMethod]
    public async Task Retry_replays_matching_task_receipt_and_returns_later_worker_snapshot()
        => await AssertReceiptAsync(false, true, false);

    [TestMethod]
    public async Task Reconcile_replays_matching_task_receipt_without_executing_commands()
        => await AssertReceiptAsync(true, true, false);

    [TestMethod]
    public async Task Retry_rejects_ambiguous_legacy_receipt_without_writes()
        => await AssertReceiptAsync(false, true, true);

    [TestMethod]
    public async Task Reconcile_rejects_ambiguous_legacy_receipt_without_writes()
        => await AssertReceiptAsync(true, true, true);

    private static async Task AssertReceiptAsync(bool reconcile, bool sameTask, bool legacy)
    {
        var taskId = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        var action = reconcile ? "recovery.reconcile" : "recovery.retry";
        var now = DateTimeOffset.UtcNow;
        var task = new WorkflowRecoveryTaskRecord(taskId, null, "host", "host", Guid.CreateVersion7(), null,
            "stuck_instance", reconcile ? "succeeded" : "failed", 4, 5, null, null, 2, null, null, now, now);
        // 回执作用域仍为实例；请求摘要必须额外绑定目标任务，旧摘要不能证明资源归属。
        var payload = legacy ? $"{action}\n2\nreason"
            : $"recovery:v2\n{(sameTask ? taskId : Guid.CreateVersion7()):D}\n{action}\n2\nreason";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var receipt = new WorkflowActionReceiptRecord(action, actor, 2, "recovery-key", hash, null);
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<WorkflowRecoveryTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(task);
        queries.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(receipt);
        var commands = Substitute.For<ICommandExecutor>();
        var commandCount = 0;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { commandCount++; return 1; });
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var coordinator = new RecordingDbTransactionCoordinator();
        var service = new WorkflowRecoveryTaskService(queries, commands, new DapperCommandTransaction(coordinator), tenant, clock,
            Substitute.For<IIdGenerator>(), Options.Create(new DatabaseOptions()));

        var result = reconcile
            ? await service.ReconcileAsync(taskId, actor, new(2, " reason ", "recovery-key"))
            : await service.RetryAsync(taskId, actor, new(2, " reason ", "recovery-key"));

        Assert.AreEqual(sameTask && !legacy, result.IsSuccess);
        if (result.IsSuccess)
        {
            var value = result.Value!;
            Assert.AreEqual(taskId, value.Id);
            Assert.AreEqual(5L, value.Revision);
            Assert.AreEqual(task.StatusKey, value.StatusKey);
        }
        else Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
        Assert.AreEqual(0, commandCount);
        Assert.IsFalse(coordinator.HasTransaction);
    }
}
