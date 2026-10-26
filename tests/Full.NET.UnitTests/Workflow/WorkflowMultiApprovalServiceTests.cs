using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证多人审批服务按持久化票数等待、收敛，并确定性回放并发请求。</summary>
[TestClass]
public sealed class WorkflowMultiApprovalServiceTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Sequential_vote_binds_one_frozen_next_slot_or_returns_failure_for_rollback(int affected)
    {
        var f = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2), approvalMode: "sequential");
        var next = new WorkflowPendingApprovalSlotRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 1);
        f.Query.QuerySingleOrDefaultAsync<WorkflowPendingApprovalSlotRecord>(WorkflowSequentialApprovalSql.FindNextSqlServer,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(next);
        f.Command.ExecuteAsync(WorkflowSequentialApprovalSql.BindTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(affected);
        var result = await f.Service.ApproveAsync(f.TodoId, f.ActorId,
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), null, "seq-next", 2));
        Assert.AreEqual(affected == 1, result.IsSuccess);
        await f.Command.Received(1).ExecuteAsync(WorkflowSql.InsertTodo,
            Arg.Is<object?>(p => HasValue(p, "AssigneeUserId", next.AssigneeUserId)), Arg.Any<CancellationToken>());
        await f.Command.Received(affected).ExecuteAsync(WorkflowTodoViewSql.Capture, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(affected, f.Outbox.ReceivedCalls().Count());
        if (affected == 0)
        {
            Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
            await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalActionRecord, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        }
        else Assert.IsNotNull(result.Value!.ActiveTodoId);
    }

    [TestMethod]
    public async Task Sequential_missing_frozen_slot_does_not_silently_finish_or_notify()
    {
        var f = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2), approvalMode: "sequential");
        var result = await f.Service.ApproveAsync(f.TodoId, f.ActorId,
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), null, "seq-missing", 2));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, result.Error!.Code);
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Identical_request_for_another_todo_must_not_replay_its_receipt()
    {
        var f = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2));
        var request = new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), "同意", "other-todo", 2);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("approve\n3\n2\n{}\n同意\nsignature:")));
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowActionReceiptRecord(
                "approve", f.ActorId, 8, request.IdempotencyKey, hash, null, "active", Guid.CreateVersion7()));
        var result = await f.Service.ApproveAsync(f.TodoId, f.ActorId, request);
        Assert.AreEqual(Full.NET.Modules.Workflow.Contracts.WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Action_locks_instance_before_reading_todo_inside_transaction()
    {
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2));
        await fixture.Service.ApproveAsync(fixture.TodoId, fixture.ActorId,
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), null, "lock-order", 2));
        Received.InOrder(() =>
        {
            fixture.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
                Arg.Any<object?>(), Arg.Any<CancellationToken>());
            fixture.Command.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, Arg.Any<object?>(), Arg.Any<CancellationToken>());
            fixture.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
                Arg.Any<object?>(), Arg.Any<CancellationToken>());
            fixture.Command.ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        });
        Assert.AreEqual(WorkflowSql.LockInstanceForTodoAction, fixture.Command.ReceivedCalls().First().GetArguments()[0]);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Partial_write_after_form_cas_must_fail_for_rollback_instead_of_committing_replay(bool failSlot)
    {
        var request = new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), "同意", "partial-cas", 2);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("approve\n3\n2\n{}\n同意\nsignature:")));
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2));
        var receipt = new WorkflowActionReceiptRecord("approve", fixture.ActorId, 8, request.IdempotencyKey, hash, null, "active");
        fixture.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((WorkflowActionReceiptRecord?)null, receipt);
        fixture.Command.ExecuteAsync(failSlot ? WorkflowSql.DecideApprovalSlotWithRevision : WorkflowSql.CompleteTodoWithRevision,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await fixture.Service.ApproveAsync(fixture.TodoId, fixture.ActorId, request);
        Assert.AreEqual(Full.NET.Modules.Workflow.Contracts.WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        await fixture.Command.DidNotReceive().ExecuteAsync(WorkflowTodoViewSql.SetOutput, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Query.Received(1).QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Stale_form_revision_rejects_a_patch_before_todo_or_instance_writes()
    {
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2));
        var result = await fixture.Service.ApproveAsync(fixture.TodoId, fixture.ActorId,
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), null, "stale-form", 1));
        Assert.AreEqual(Full.NET.Modules.Workflow.Contracts.WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
    }

    /// <summary>N-of-M 首票未达到门槛时只推进步骤与实例修订号，不关闭其他席位。</summary>
    [TestMethod]
    public async Task Approve_before_threshold_keeps_step_active()
    {
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2));

        var result = await fixture.Service.ApproveAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), null, "approve-001", ExpectedFormRevision: 2));

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
            new ActWorkflowTodoRequest(3, JsonSerializer.SerializeToElement(new { }), "同意", "approve-002", ExpectedFormRevision: 2));

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
    [DataRow(false)]
    [DataRow(true)]
    public async Task Concurrent_same_request_replays_only_the_original_todo_after_instance_lock_loss(bool foreignTodo)
    {
        var request = new ActWorkflowTodoRequest(
            3, JsonSerializer.SerializeToElement(new { }), "同意", "approve-concurrent", ExpectedFormRevision: 2);
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"approve\n{request.ExpectedRevision}\n{request.ExpectedFormRevision}\n{request.FieldPatch.GetRawText()}\n{request.Comment}\nsignature:")));
        var receipt = new WorkflowActionReceiptRecord(
            "approve", Guid.Empty, 8, request.IdempotencyKey, requestHash, null, "active");
        var fixture = CreateFixture(new WorkflowApprovalTallyRecord(1, 0, 2), receipt);
        receipt = receipt with { ActorUserId = fixture.ActorId, TodoId = foreignTodo ? Guid.CreateVersion7() : fixture.TodoId };
        fixture.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null, receipt);
        fixture.Command.ExecuteAsync(
                WorkflowSql.LockInstanceForMultiApproval, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await fixture.Service.ApproveAsync(fixture.TodoId, fixture.ActorId, request);

        Assert.AreEqual(!foreignTodo, result.IsSuccess);
        if (foreignTodo) Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
        else
        {
            Assert.AreEqual("active", result.Value!.StatusKey);
            Assert.AreEqual(8, result.Value.Revision);
        }
        Assert.AreEqual(1, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
        Assert.AreEqual(0, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>创建包含一个三人 N-of-M 审批节点的服务夹具。</summary>
    /// <param name="tally">当前投票完成后的权威票数。</param>
    /// <param name="concurrentReceipt">可选的并发胜方回执。</param>
    /// <returns>包含服务和可观察替身的测试夹具。</returns>
    private static ApprovalFixture CreateFixture(
        WorkflowApprovalTallyRecord tally,
        WorkflowActionReceiptRecord? concurrentReceipt = null,
        string approvalMode = "nOfM")
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        if (concurrentReceipt is not null) concurrentReceipt = concurrentReceipt with { TodoId = todoId };
        var stepId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var definitionVersionId = Guid.CreateVersion7();
        var formVersionId = Guid.CreateVersion7();
        var slotId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var otherUsers = new[] { actorId, Guid.CreateVersion7(), Guid.CreateVersion7() };
        var approvalPolicy = new Dictionary<string, object> { ["modeKey"] = approvalMode, ["approverUserIds"] = otherUsers };
        if (approvalMode == "nOfM") approvalPolicy["requiredApprovals"] = 2;
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
                        approvalPolicy,
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
                "review", 5, approvalMode, approvalMode == "sequential" ? 3 : 2, 3, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", definitionVersionId, formVersionId,
                "purchase", "PO-001", null, "active", 7, actorId, now,
                null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null, concurrentReceipt);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
                WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(
                definitionVersionId, formVersionId, canonicalJson,
                "{\"schemaVersion\":1,\"adapterVersion\":1,\"sections\":[]}", null, "active"));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
                WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(
                Guid.CreateVersion7(), instanceId, formVersionId, "{}", "none", 2, actorId, now));
        query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(
                WorkflowSql.FindApprovalSlotByTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowApprovalSlotRecord(slotId, 1));
        query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(
                WorkflowSql.FindApprovalTallyByStepSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(tally);

        query.QuerySingleOrDefaultAsync<string>(WorkflowDesignerAssetSql.FindBindings,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns("""{"schemaVersion":1,"sections":[]}""");
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
            new WorkflowHumanActivityPreparation(WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(), query, WorkflowTodoManagementTestDependencies.CreateStarterSelectionPreparation()),
            notificationPublisher,
            WorkflowTodoManagementTestDependencies.CreateCountersignService(query, command, tenant),
            WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(
                query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateParallelJoinCoordinator(query, command, ids),
            WorkflowTodoManagementTestDependencies.CreateAttachmentCoordinator(),
            new WorkflowBranchStateCoordinator(query, command),
            WorkflowTodoManagementTestDependencies.CreateSignatureCoordinator(query, command),
            WorkflowTodoManagementTestDependencies.CreateStepResolution(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateTaskDecisions(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateResultCallbacks(query, command, ids));
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
