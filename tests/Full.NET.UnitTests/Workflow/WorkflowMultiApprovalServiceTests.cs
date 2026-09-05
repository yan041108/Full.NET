using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证多人审批服务按持久化票数等待、收敛，并确定性回放并发请求。</summary>
[TestClass]
public sealed class WorkflowMultiApprovalServiceTests
{
    /// <summary>N-of-M 首票未达到门槛时只推进步骤与实例修订号，不关闭其他席位。</summary>
    [TestMethod]
    public async Task Approve_before_threshold_keeps_step_active()
    {
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2));

        var result = await fixture.Service.ApproveAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), null, "approve-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(8, result.Value.Revision);
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowSql.LockInstanceForMultiApproval, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowSql.AdvanceApprovalStepWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowSql.AdvanceInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.DidNotReceive().ExecuteAsync(
            WorkflowSql.CancelPendingApprovalTodosByStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.DidNotReceive().ExecuteAsync(
            WorkflowSql.CancelPendingApprovalSlotsByStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>N-of-M 达到门槛时必须原子完成步骤和实例，并取消剩余待办与席位。</summary>
    [TestMethod]
    public async Task Approve_at_threshold_completes_and_cancels_remaining_slots()
    {
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(2, 0, 1));

        var result = await fixture.Service.ApproveAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), "同意", "approve-002"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("completed", result.Value!.StatusKey);
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowSql.CompleteStepWithRevision,
            Arg.Is<object?>(value => HasValue(value, "StatusKey", "completed")),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowSql.CompleteInstanceWithRevision,
            Arg.Is<object?>(value => HasValue(value, "StatusKey", "completed")),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowSql.CancelPendingApprovalTodosByStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowSql.CancelPendingApprovalSlotsByStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>相同幂等请求竞争实例锁失败时必须读取胜方回执并返回首次结果。</summary>
    [TestMethod]
    public async Task Concurrent_same_request_replays_winner_after_instance_lock_loss()
    {
        var request = new ActWorkflowTodoRequest(
            3, JsonSerializer.SerializeToElement(new { }), "同意", "approve-concurrent");
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"approve\n{request.ExpectedRevision}\n{request.FieldPatch.GetRawText()}\n{request.Comment}")));
        var receipt = new WorkflowActionReceiptRecord(
            "approve", Guid.Empty, 8, request.IdempotencyKey, requestHash, null, "active");
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2), receipt);
        receipt = receipt with { ActorUserId = fixture.ActorId };
        fixture.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null, receipt);
        fixture.Command.ExecuteAsync(
                WorkflowSql.LockInstanceForMultiApproval, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await fixture.Service.ApproveAsync(fixture.TodoId, fixture.ActorId, request);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(8, result.Value.Revision);
        Assert.AreEqual(1, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>创建包含一个三人 N-of-M 审批节点的服务夹具。</summary>
    /// <param name="tally">当前投票完成后的权威票数。</param>
    /// <param name="concurrentReceipt">可选的并发胜方回执。</param>
    /// <returns>包含服务和可观察替身的测试夹具。</returns>
    private static ApprovalFixture CreateFixture(
        WorkflowApprovalTallyRecord tally,
        WorkflowActionReceiptRecord? concurrentReceipt = null)
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var stepId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var definitionVersionId = Guid.CreateVersion7();
        var formVersionId = Guid.CreateVersion7();
        var slotId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var otherUsers = new[] { actorId, Guid.CreateVersion7(), Guid.CreateVersion7() };
        var canonicalJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            nodes = new object[]
            {
                new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "review" } } },
                new
                {
                    nodeKey = "review",
                    nodeTypeKey = "human.approval",
                    nodeSchemaVersion = 1,
                    config = new
                    {
                        nextNodeKeys = new[] { "end" },
                        approvalPolicy = new
                        {
                            modeKey = "nOfM",
                            approverUserIds = otherUsers,
                            requiredApprovals = 2,
                        },
                    },
                },
                new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { nextNodeKeys = Array.Empty<string>() } },
            },
        });
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
                WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRuntimeRecord(
                todoId, instanceId, stepId, actorId, "active", now, null, null, 3,
                "review", 5, "nOfM", 2, 3));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", definitionVersionId, formVersionId,
                "purchase", "PO-001", "active", 7, actorId, now,
                null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null, concurrentReceipt);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
                WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(
                definitionVersionId, formVersionId, canonicalJson,
                "{\"schemaVersion\":1,\"adapterVersion\":1,\"sections\":[]}"));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
                WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(
                Guid.CreateVersion7(), instanceId, formVersionId, "{}", "none", 2, actorId, now));
        query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(
                WorkflowSql.FindApprovalSlotByTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowApprovalSlotRecord(slotId, 1));
        query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(
                WorkflowSql.FindApprovalTallyByStep, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(tally);

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var outbox = Substitute.For<IOutboxWriter>();
        var notificationPublisher = new WorkflowNotificationOutboxPublisher(outbox);
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        var service = new WorkflowTodoManagementService(
            query, command, new TrackingTransaction(), tenant, clock, ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            new WorkflowAutomaticTransitionWriter(command, ids, ccWriter),
            new WorkflowApprovalActivationWriter(command, ids, notificationPublisher),
            notificationPublisher,
            WorkflowTodoManagementTestDependencies.CreateCountersignService(query, command, tenant));
        return new ApprovalFixture(service, query, command, outbox, todoId, actorId);
    }

    /// <summary>判断 SQL 参数对象是否携带指定名称和值。</summary>
    /// <param name="parameters">待检查参数对象。</param>
    /// <param name="name">属性名。</param>
    /// <param name="expected">期望值。</param>
    /// <returns>属性存在且值相等时返回 <see langword="true"/>。</returns>
    private static bool HasValue(object? parameters, string name, object expected) =>
        parameters is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && actual?.Equals(expected) == true;

    /// <summary>保存多人审批服务测试使用的替身与标识。</summary>
    /// <param name="Service">待测服务。</param>
    /// <param name="Query">可观察查询执行器。</param>
    /// <param name="Command">可观察命令执行器。</param>
    /// <param name="Outbox">可观察 Outbox 写入器。</param>
    /// <param name="TodoId">当前待办标识。</param>
    /// <param name="ActorId">当前办理人标识。</param>
    private sealed record ApprovalFixture(
        WorkflowTodoManagementService Service,
        IQueryExecutor Query,
        ICommandExecutor Command,
        IOutboxWriter Outbox,
        Guid TodoId,
        Guid ActorId);

    /// <summary>直接执行事务回调，使测试可以观察同一事务内的所有命令。</summary>
    private sealed class TrackingTransaction : ICommandTransaction
    {
        /// <summary>执行调用方事务回调。</summary>
        /// <typeparam name="T">结果类型。</typeparam>
        /// <param name="action">事务内操作。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>事务回调结果。</returns>
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
