using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>转办保留一票与不可变历史，准备在事务外，首次回执优先于后来的活动状态。</summary>
[TestClass]
public sealed class WorkflowTodoTransferServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Delegation_preserves_one_seat_and_records_its_own_authority(bool tenant)
    {
        var f = Create(tenant);
        var result = await f.Service.DelegateAsync(f.Todo.Id, f.Actor, new(f.Target, 3, 2, null, "delegate-test"));
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.IsTrue(f.DirectoryCalls.All(inside => !inside));
        await f.Command.Received(1).ExecuteAsync(WorkflowTodoTransferSql.Insert,
            Arg.Is<object?>(p => Has(p, "AuthorizationKey", WorkflowPermissions.TodosDelegate) && Has(p, "OriginalAssigneeUserId", f.Actor)), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
            Arg.Is<object?>(p => Has(p, "ResultActionKey", "delegate")), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Proxy_cannot_transfer_or_delegate_its_authority_again(bool evidenceRequired)
    {
        var f = Create(config: evidenceRequired ? "{\"signRequired\":true,\"commentRequired\":true}" : "{}");
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowTodoDelegationRecord(Guid.CreateVersion7(), f.Todo.Id, Guid.CreateVersion7(), f.Actor, DateTimeOffset.UtcNow));
        Assert.AreEqual(WorkflowErrorCodes.TodoActionForbidden, (await f.Service.TransferAsync(f.Todo.Id, f.Actor, f.Request)).Error?.Code);
        Assert.AreEqual(WorkflowErrorCodes.TodoActionForbidden, (await f.Service.DelegateAsync(f.Todo.Id, f.Actor,
            new(f.Target, 3, 2, null, "delegate-test"))).Error?.Code);
        Assert.AreEqual(0, f.DirectoryCalls.Count);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Recall_closes_only_proxy_and_restores_same_seat_without_decision()
    {
        var f = Create();
        var origin = f.Todo with { StatusKey = "completed", ResultActionKey = "delegate", Revision = 4 };
        var proxy = f.Todo with { Id = Guid.CreateVersion7(), AssigneeUserId = f.Target, Revision = 1 };
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => Has(call[1], "Id", origin.Id) ? origin : proxy);
        var grant = new WorkflowTodoDelegationRecord(origin.Id, proxy.Id, f.Actor, f.Target, DateTimeOffset.UtcNow);
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForOrigin, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(grant);
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(grant);
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoTransferLineageRecord>(WorkflowTodoTransferSql.FindLineage, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoTransferLineageRecord(origin.Id, f.Actor, 1));
        f.Host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [f.Actor] = new(f.Actor, "actor", "Actor") });
        var result = await f.Delegations.RecallAsync(origin.Id, f.Actor, new(1, 2, null, "recall-test"), default);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        await f.Command.Received(1).ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
            Arg.Is<object?>(p => Has(p, "Id", proxy.Id) && Has(p, "AssigneeUserId", f.Target) && Has(p, "ResultActionKey", "delegate_recall")), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowTodoTransferSql.Insert,
            Arg.Is<object?>(p => Has(p, "AuthorizationKey", WorkflowPermissions.TodosRecallDelegation) && Has(p, "ActorUserId", f.Actor) && Has(p, "ToAssigneeUserId", f.Actor)), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Recall_receipt_survives_proxy_completion_and_target_directory_changes()
    {
        var f = Create(); var next = Guid.CreateVersion7(); var request = new RecallWorkflowTodoDelegationRequest(1, 2, null, "recall-test");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,
            WorkflowJsonSerializerContext.Default.RecallWorkflowTodoDelegationRequest))));
        f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(f.Instance with { StatusKey = "completed", Revision = 99 });
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActionReceiptRecord("delegate_recall", f.Actor, 8, request.IdempotencyKey, hash, next, "active", f.Todo.Id));
        var result = await f.Delegations.RecallAsync(f.Todo.Id, f.Actor, request, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(next, result.Value!.ActiveTodoId); Assert.AreEqual(8, result.Value.Revision);
        Assert.AreEqual(0, f.DirectoryCalls.Count); Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Concurrent_recall_receipt_wins_when_preparation_observes_an_already_closed_proxy()
    {
        var f = Create(); var next = Guid.CreateVersion7(); var proxy = Guid.CreateVersion7();
        var request = new RecallWorkflowTodoDelegationRequest(1, 2, null, "concurrent-recall");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,
            WorkflowJsonSerializerContext.Default.RecallWorkflowTodoDelegationRequest))));
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => Has(call[1], "Id", f.Todo.Id) ? f.Todo with { StatusKey = "completed", ResultActionKey = "delegate" }
                : f.Todo with { Id = proxy, AssigneeUserId = f.Target, StatusKey = "completed", ResultActionKey = "delegate_recall" });
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForOrigin, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoDelegationRecord(f.Todo.Id, proxy, f.Actor, f.Target, DateTimeOffset.UtcNow));
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null, new WorkflowActionReceiptRecord("delegate_recall", f.Actor, 8, request.IdempotencyKey, hash, next, "active", f.Todo.Id));
        var result = await f.Delegations.RecallAsync(f.Todo.Id, f.Actor, request, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(next, result.Value!.ActiveTodoId);
        Assert.AreEqual(0, f.DirectoryCalls.Count); Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Delegation_keeps_a_depth_slot_available_for_recall()
    {
        var f = Create();
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoTransferLineageRecord>(WorkflowTodoTransferSql.FindLineage, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoTransferLineageRecord(Guid.CreateVersion7(), f.Actor, 31));
        Assert.AreEqual(WorkflowErrorCodes.InvalidTransition, (await f.Service.DelegateAsync(f.Todo.Id, f.Actor,
            new(f.Target, 3, 2, null, "depth-limit"))).Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    [DataRow(false, "single")]
    [DataRow(false, "all")]
    [DataRow(false, "sequential")]
    [DataRow(true, "single")]
    [DataRow(true, "all")]
    [DataRow(true, "sequential")]
    public async Task Transfer_creates_new_input_and_rebinds_existing_vote_without_deciding_it(bool isTenant, string mode)
    {
        var f = Create(isTenant, mode);
        var result = await f.Service.TransferAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreNotEqual(f.Todo.Id, result.Value!.ActiveTodoId);
        Assert.AreEqual(8, result.Value.Revision);
        await f.Command.Received(1).ExecuteAsync(WorkflowTodoTransferSql.RebindSlot,
            Arg.Is<object?>(p => Has(p, "Id", f.SlotId) && Has(p, "TodoId", f.Todo.Id) && Has(p, "AssigneeUserId", f.Target)), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowTodoViewSql.Capture,
            Arg.Is<object?>(p => Has(p, "InputJson", "{\"title\":\"current\"}")), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowTodoTransferSql.Insert,
            Arg.Is<object?>(p => Has(p, "OriginalAssigneeUserId", f.Actor) && Has(p, "ActorUserId", f.Actor) &&
                Has(p, "AuthorizationKey", WorkflowPermissions.TodosTransfer) && Has(p, "FormRevision", 2L)), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Outbox.ReceivedCalls().Count());
        Assert.IsTrue(f.DirectoryCalls.All(inside => !inside));
        Assert.AreEqual(1, f.DirectoryCalls.Count);
    }

    [TestMethod]
    public async Task Terminal_original_todo_replays_first_result_without_enumerating_users_or_writing()
    {
        var f = Create(); var next = Guid.CreateVersion7();
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Todo with { StatusKey = "completed", Revision = 4 });
        f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Instance with { StatusKey = "cancelled", Revision = 99 });
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActionReceiptRecord("transfer", f.Actor, 8, f.Request.IdempotencyKey, Hash(f.Request), next, "active", f.Todo.Id));
        var result = await f.Service.TransferAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(8, result.Value.Revision); Assert.AreEqual(next, result.Value.ActiveTodoId);
        Assert.AreEqual(0, f.DirectoryCalls.Count); Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Receipt_for_another_todo_is_a_conflict_even_when_request_hash_matches()
    {
        var f = Create();
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActionReceiptRecord("transfer", f.Actor, 8, f.Request.IdempotencyKey, Hash(f.Request), Guid.CreateVersion7(), "active", Guid.CreateVersion7()));
        var result = await f.Service.TransferAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    [DataRow("{\"operationPermissions\":{\"transfer\":false}}", WorkflowErrorCodes.TodoActionForbidden)]
    [DataRow("{\"commentRequired\":true}", WorkflowErrorCodes.TodoCommentRequired)]
    [DataRow("{\"signRequired\":true}", WorkflowErrorCodes.TodoSignatureRequired)]
    public async Task Published_action_evidence_requirements_cannot_be_bypassed(string config, string error)
    {
        var f = Create(config: config);
        var result = await f.Service.TransferAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.AreEqual(error, result.Error?.Code); Assert.AreEqual(0, f.DirectoryCalls.Count);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    [DataRow("workflow.todo_transfer.insert_todo")]
    [DataRow("workflow.todo_transfer.rebind_slot")]
    [DataRow("workflow.todo_transfer.advance_step")]
    [DataRow("workflow.instance.advance_with_revision")]
    public async Task Failed_handoff_cas_returns_failure_for_transaction_rollback_and_never_notifies(string statement)
    {
        var f = Create();
        f.Command.ExecuteAsync(Arg.Is<SqlStatement>(s => s != null && s.Name == statement), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await f.Service.TransferAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalActionRecord, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("depth")]
    [DataRow("cycle")]
    [DataRow("occupied")]
    [DataRow("stale_form")]
    [DataRow("inactive_target")]
    [DataRow("foreign")]
    public async Task Invalid_resource_or_target_never_starts_handoff(string scenario)
    {
        var f = Create();
        if (scenario == "depth") f.Query.QuerySingleOrDefaultAsync<WorkflowTodoTransferLineageRecord>(WorkflowTodoTransferSql.FindLineage,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowTodoTransferLineageRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 8));
        if (scenario == "cycle") f.Query.QuerySingleOrDefaultAsync<int>(WorkflowTodoTransferSql.CountPreviousActor, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        if (scenario == "occupied") f.Query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotByStepAssignee,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowApprovalSlotRecord(Guid.CreateVersion7(), 1));
        if (scenario == "inactive_target") f.Host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        var request = scenario == "stale_form" ? f.Request with { ExpectedFormRevision = 1 } : f.Request;
        var result = await f.Service.TransferAsync(f.Todo.Id, scenario == "foreign" ? Guid.CreateVersion7() : f.Actor, request);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }

    private static Fixture Create(bool isTenant = false, string mode = "all", string config = "{}")
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var actor = Guid.CreateVersion7(); var target = Guid.CreateVersion7(); var slot = Guid.CreateVersion7();
        var scope = new CurrentTenantAccessor(); if (isTenant) scope.SetTenant(new TenantContext(Guid.CreateVersion7(), "transfer", "Transfer")); else scope.SetHost();
        var now = DateTimeOffset.UtcNow; var tenantScope = scope.Id is { } id ? $"tenant:{id:N}" : "host";
        var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), scope.Id, isTenant ? "tenant" : "host", tenantScope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "transfer", null, "active", 7, actor, now, null, null, null, null, null, null);
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instance.Id, Guid.CreateVersion7(), actor, "active", now,
            null, null, 3, "review", 1, mode, 3, 3, null, null, null);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(todo);
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value,
                "{\"schemaVersion\":1,\"nodes\":[{\"nodeKey\":\"review\",\"nodeTypeKey\":\"human.approval\",\"nodeSchemaVersion\":1,\"config\":" + config + "}]}", "{}", null, "active"));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), instance.Id, instance.FormVersionId.Value, "{\"title\":\"current\"}", "none", 2, actor, now));
        query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotForReassignment, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowApprovalSlotRecord(slot, 1));
        var tx = new Transaction(); var calls = new List<bool>();
        var host = Substitute.For<IHostUserBatchSelectionDirectory>(); var members = Substitute.For<ITenantMemberSelectionDirectory>();
        host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { calls.Add(tx.Inside); return new Dictionary<Guid, HostUserDirectoryEntry> { [target] = new(target, "target", "Target") }; });
        members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { calls.Add(tx.Inside); return new Dictionary<Guid, TenantUserDirectoryEntry> { [target] = new(target, "target", "Target") }; });
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var outbox = Substitute.For<IOutboxWriter>(); var notifications = new WorkflowNotificationOutboxPublisher(outbox);
        var service = new WorkflowTodoTransferService(query, command, tx, scope, clock, ids, host, members,
            new(query, command), new WorkflowTodoHandoffWriter(query, command, ids, new(command, ids, notifications), notifications),
            WorkflowTodoManagementTestDependencies.CreateSignatureCoordinator(query, command));
        var delegations = new WorkflowTodoDelegationService(query, command, tx, scope, clock, ids, host, members,
            new(query, command), new WorkflowTodoHandoffWriter(query, command, ids, new(command, ids, notifications), notifications));
        return new(service, query, command, outbox, host, todo, instance, actor, target, slot, new(target, 3, 2, null, "transfer-test"), calls, delegations);
    }

    private static string Hash(TransferWorkflowTodoRequest request) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WorkflowJsonSerializerContext.Default.TransferWorkflowTodoRequest))));
    private static bool Has(object? p, string key, object expected) => p is IReadOnlyDictionary<string, object?> values && values.TryGetValue(key, out var value) && Equals(expected, value);
    private sealed record Fixture(WorkflowTodoTransferService Service, IQueryExecutor Query, ICommandExecutor Command,
        IOutboxWriter Outbox, IHostUserBatchSelectionDirectory Host, WorkflowTodoRuntimeRecord Todo, WorkflowInstanceRecord Instance,
        Guid Actor, Guid Target, Guid SlotId, TransferWorkflowTodoRequest Request, List<bool> DirectoryCalls, WorkflowTodoDelegationService Delegations);
    private sealed class Transaction : ICommandTransaction
    {
        public bool Inside { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        { Inside = true; try { return await action(cancellationToken); } finally { Inside = false; } }
    }
}
