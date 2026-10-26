using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>撤回不能借管理员取消或旧回执绕过发起人、并发版本和已发布副作用边界。</summary>
[TestClass]
public sealed class WorkflowWithdrawalServiceTests
{
    [TestMethod]
    public async Task Foreign_actor_is_rejected_before_lock_or_writes()
    {
        var f = Create(); var result = await f.Service.WithdrawAsync(f.Instance.Id, Guid.CreateVersion7(), new(1, "withdraw"));
        Assert.AreEqual(WorkflowErrorCodes.InstanceForbidden, result.Error?.Code);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count()); Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }
    [TestMethod]
    [DataRow("disabled", false)]
    [DataRow("before_any_human_action", true)]
    public async Task Disabled_or_already_handled_boundaries_do_not_cancel(string mode, bool acted)
    {
        var f = Create(mode); f.Query.QuerySingleOrDefaultAsync<int>(WorkflowWithdrawalSql.CountHumanActions, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(acted ? 1 : 0);
        var result = await f.Service.WithdrawAsync(f.Instance.Id, f.Instance.StartedById, new(1, "withdraw"));
        Assert.AreEqual(WorkflowErrorCodes.InstanceWithdrawalBoundaryInvalid, result.Error?.Code);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.CancelInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Unknown_reached_effect_blocks_withdrawal_even_before_a_human_action()
    {
        var f = Create(); f.Query.QueryAsync<WorkflowWithdrawalActivityBoundary>(WorkflowWithdrawalSql.ListReached, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowWithdrawalActivityBoundary("external", "service.future", "completed") });
        var result = await f.Service.WithdrawAsync(f.Instance.Id, f.Instance.StartedById, new(1, "withdraw"));
        Assert.AreEqual(WorkflowErrorCodes.InstanceWithdrawalBoundaryInvalid, result.Error?.Code);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowActivitySql.CancelWork, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Valid_withdrawal_closes_durable_and_human_work_and_records_its_own_action()
    {
        var f = Create(); var result = await f.Service.WithdrawAsync(f.Instance.Id, f.Instance.StartedById, new(1, "withdraw"));
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("cancelled", result.Value!.StatusKey); Assert.AreEqual(2, result.Value.Revision);
        await f.Command.Received(1).ExecuteAsync(WorkflowClaimSql.CancelPools, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowVoteSql.CancelRoundsByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowActivitySql.CancelWork, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowSql.InsertApprovalActionRecord,
            Arg.Is<object?>(parameters => Has(parameters, "ActionKey", "withdraw") && Has(parameters, "ActorUserId", f.Instance.StartedById) && Has(parameters, "TodoId", null)), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Outbox.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task First_receipt_is_replayed_before_later_status_or_policy_checks()
    {
        var f = Create("disabled"); var request = new WithdrawWorkflowInstanceRequest(1, "withdraw");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WorkflowJsonSerializerContext.Default.WithdrawWorkflowInstanceRequest))));
        f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(f.Instance with { StatusKey="cancelled", Revision=9 });
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActionReceiptRecord("withdraw", f.Instance.StartedById, 2, "withdraw", hash, null, "cancelled", null));
        var result = await f.Service.WithdrawAsync(f.Instance.Id, f.Instance.StartedById, request);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(2, result.Value!.Revision);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.CancelInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }
    private static bool Has(object? parameters, string key, object? expected) => parameters is IReadOnlyDictionary<string, object?> values && values.TryGetValue(key, out var value) && Equals(expected, value);
    private static Fixture Create(string mode = "before_any_human_action")
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var outbox = Substitute.For<IOutboxWriter>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "request", "one", null,
            "active", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        var draft = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "review" }, withdrawalPolicy = new { modeKey = mode } })),
             new("review", "human.task", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, taskPolicy = new { completionModeKey = "all" } })),
             new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value,
                JsonSerializer.Serialize(draft, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft), "{}", null, "active"));
        query.QueryAsync<WorkflowWithdrawalActivityBoundary>(WorkflowWithdrawalSql.ListReached, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowWithdrawalActivityBoundary("review", "human.task", "active") });
        var publisher = new WorkflowNotificationOutboxPublisher(outbox);
        var service = new WorkflowInstanceManagementService(query, command, new Transaction(), tenant, clock, ids,
            Options.Create(new DatabaseOptions { Provider=DatabaseProvider.SqlServer }), publisher,
            WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateParallelJoinCoordinator(query, command, ids),
            WorkflowTodoManagementTestDependencies.CreateAttachmentCoordinator(), Substitute.For<ITenantFeatureEntitlementPort>(),
            new(WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(), query, WorkflowTodoManagementTestDependencies.CreateStarterSelectionPreparation()));
        return new(service, query, command, outbox, instance);
    }
    private sealed record Fixture(WorkflowInstanceManagementService Service, IQueryExecutor Query, ICommandExecutor Command, IOutboxWriter Outbox, WorkflowInstanceRecord Instance);
    private sealed class Transaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }
}
