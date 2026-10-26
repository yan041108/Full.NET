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
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>催办验证发起人、真实待办、固定回执及独立预算，不借通知推进流程。</summary>
[TestClass]
public sealed class WorkflowManualReminderServiceTests
{
    [TestMethod]
    public async Task Foreign_actor_cannot_enumerate_or_remind()
    {
        var f = Create();
        Assert.AreEqual(WorkflowErrorCodes.InstanceForbidden, (await f.Service.ListAsync(f.Instance.Id, Guid.CreateVersion7())).Error?.Code);
        Assert.AreEqual(WorkflowErrorCodes.InstanceForbidden, (await f.Service.RequestAsync(f.Instance.Id, Guid.CreateVersion7(), f.Request)).Error?.Code);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Accepted_request_records_advisory_only_and_publishes_real_todo()
    {
        var f = Create(); var result = await f.Service.RequestAsync(f.Instance.Id, f.Instance.StartedById, f.Request);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1, result.Value!.ManualSequence); Assert.AreEqual(1, result.Value.RecipientCount);
        Assert.AreEqual(1, result.Value.InstanceRevision); Assert.AreEqual(1, result.Value.StepRevision);
        Assert.AreEqual(0L, result.Value.AcceptedAtUtc.Ticks % 10);
        await f.Command.Received(1).ExecuteAsync(WorkflowManualReminderSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.CompleteInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Outbox.Received(1).AddAsync(WorkflowNotificationIntegrationEventTypes.TodoManualReminderRequested, 1,
            Arg.Is<WorkflowTodoManualReminderRequestedIntegrationEvent>(x => x != null && x.TodoId == f.Target.TodoId && x.RecipientUserId == f.Target.AssigneeUserId),
            Arg.Any<Full.NET.Messaging.Abstractions.IntegrationEventMetadata>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    [DataRow(1, -299)]
    [DataRow(20, -3600)]
    public async Task Rate_limit_does_not_append_action_or_notification(int count, int age)
    {
        var f = Create(); f.Query.QuerySingleOrDefaultAsync<WorkflowManualReminderBudgetRecord>(WorkflowManualReminderSql.ReadBudget, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowManualReminderBudgetRecord(count, f.Now.AddSeconds(age)));
        var result = await f.Service.RequestAsync(f.Instance.Id, f.Instance.StartedById, f.Request);
        Assert.AreEqual("workflow.instance.reminder_rate_limited", result.Error?.Code);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalActionRecord, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Changed_todo_tuple_after_identity_preparation_is_rejected()
    {
        var f = Create(); f.Query.QueryAsync<WorkflowManualReminderTargetRecord>(WorkflowManualReminderSql.ListTargets, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { f.Target }, new[] { f.Target with { AssigneeUserId = Guid.CreateVersion7(), TodoRevision = 2 } });
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, (await f.Service.RequestAsync(f.Instance.Id, f.Instance.StartedById, f.Request)).Error?.Code);
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task First_receipt_survives_later_terminal_status_identity_and_rate_changes()
    {
        var f = Create(); var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(f.Request,
            WorkflowJsonSerializerContext.Default.RequestWorkflowManualReminder))));
        f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Instance with { StatusKey = "cancelled", Revision = 9 });
        f.Query.QuerySingleOrDefaultAsync<WorkflowManualReminderReceiptRecord>(WorkflowManualReminderSql.FindReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowManualReminderReceiptRecord(Guid.CreateVersion7(), f.Request.StepId, f.Instance.StartedById, 1, 1, 1, 1, hash, f.Now));
        var result = await f.Service.RequestAsync(f.Instance.Id, f.Instance.StartedById, f.Request);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1, result.Value!.InstanceRevision);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count()); Assert.AreEqual(0, f.Hosts.ReceivedCalls().Count());
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, (await f.Service.RequestAsync(f.Instance.Id, f.Instance.StartedById, f.Request with { ExpectedStepRevision = 2 })).Error?.Code);
    }
    [TestMethod]
    public async Task Policy_and_missing_identity_fail_closed()
    {
        var f = Create(false);
        Assert.AreEqual(WorkflowErrorCodes.TodoActionForbidden, (await f.Service.RequestAsync(f.Instance.Id, f.Instance.StartedById, f.Request)).Error?.Code);
        f = Create(); f.Hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, (await f.Service.RequestAsync(f.Instance.Id, f.Instance.StartedById, f.Request)).Error?.Code);
    }
    private static Fixture Create(bool allowed = true)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var outbox = Substitute.For<IOutboxWriter>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true); var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.Parse("2026-10-25T04:00:00.0000007Z"); clock.UtcNow.Returns(now);
        var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "manual", "one", null,
            "active", 1, Guid.CreateVersion7(), now, null, null, null, null, null, null);
        var target = new WorkflowManualReminderTargetRecord(Guid.CreateVersion7(), "review", "human.task", 1, Guid.CreateVersion7(), Guid.CreateVersion7(), 1);
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        query.QueryAsync<WorkflowManualReminderTargetRecord>(WorkflowManualReminderSql.ListTargets, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { target });
        var draft = new WorkflowDefinitionDraft(1, [new("review", "human.task", 1,
            JsonSerializer.SerializeToElement(new { operationPermissions = new { remind = allowed } }))], "automation");
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value,
                JsonSerializer.Serialize(draft, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft), "{}", null, "active"));
        var hosts = Substitute.For<IHostUserBatchSelectionDirectory>();
        hosts.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [target.AssigneeUserId] = new(target.AssigneeUserId, "actor", "Actor") });
        var service = new WorkflowManualReminderService(query, command, new Transaction(), tenant, clock, ids, hosts,
            Substitute.For<ITenantMemberSelectionDirectory>(), new(outbox));
        return new(service, query, command, outbox, hosts, instance, target, new(target.StepId, 1, 1, "manual-first"), now);
    }
    private sealed record Fixture(WorkflowManualReminderService Service, IQueryExecutor Query, ICommandExecutor Command, IOutboxWriter Outbox,
        IHostUserBatchSelectionDirectory Hosts, WorkflowInstanceRecord Instance, WorkflowManualReminderTargetRecord Target, RequestWorkflowManualReminder Request, DateTimeOffset Now);
    private sealed class Transaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }
}
