using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流实例强制恢复与改派控制面的权限、原因、并发和待办保留边界。</summary>
[TestClass]
public sealed class WorkflowInstanceRecoveryServiceTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Reassign_validates_target_before_transaction_and_atomically_writes_notification(bool tenantScope, bool task)
    {
        var f = ReassignFixture.Create(tenantScope, task);
        var result = await f.Service.ReassignAsync(f.Instance.Id, f.Actor, f.Request);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(4, result.Value!.Revision);
        Assert.IsNotNull(result.Value.ActiveTodoId); Assert.AreNotEqual(f.Todo.Id, result.Value.ActiveTodoId);
        Assert.IsTrue(f.Transaction.HasStarted); Assert.IsTrue(f.DirectoryInside.All(value => !value));
        await f.Command.Received(1).ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
            Arg.Is<object?>(value => HasValue(value, "Id", f.Todo.Id) && HasValue(value, "ResultActionKey", "reassign")), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowTodoTransferSql.Insert,
            Arg.Is<object?>(value => HasValue(value, "FromAssigneeUserId", f.Todo.AssigneeUserId) && HasValue(value, "ActorUserId", f.Actor) &&
                HasValue(value, "AuthorizationKey", WorkflowPermissions.InstancesRecover)), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(task ? WorkflowTaskSql.TransferMember : WorkflowTodoTransferSql.RebindSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Outbox.Received(1).AddAsync(WorkflowNotificationIntegrationEventTypes.TodoAssigned, 1,
            Arg.Is<WorkflowTodoAssignedIntegrationEvent>(message => message != null && message.TodoId == result.Value.ActiveTodoId && message.RecipientUserId == f.Request.AssigneeUserId),
            Arg.Any<IntegrationEventMetadata>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Reassign_same_idempotency_semantics_returns_the_original_active_todo()
    {
        var f = ReassignFixture.Create(); var next = Guid.CreateVersion7();
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActionReceiptRecord("reassign", f.Actor, 4, f.Request.IdempotencyKey, HashRequest(f.Request),
                ResultTodoId: next, ResultStatusKey: "active", TodoId: f.Todo.Id));
        f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Instance with { StatusKey = "completed", Revision = 9 });
        f.Host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        var result = await f.Service.ReassignAsync(f.Instance.Id, f.Actor, f.Request);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(next, result.Value!.ActiveTodoId); Assert.AreEqual(4, result.Value.Revision);
        Assert.AreEqual("active", result.Value.StatusKey); Assert.IsFalse(f.Transaction.HasStarted);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count()); Assert.AreEqual(0, f.Host.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Reassign_rejects_inactive_target_before_starting_transaction()
    {
        var f = ReassignFixture.Create();
        f.Host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        var result = await f.Service.ReassignAsync(f.Instance.Id, f.Actor, f.Request);
        Assert.AreEqual(WorkflowErrorCodes.TodoAssigneeNotFound, result.Error?.Code);
        Assert.IsFalse(f.Transaction.HasStarted); Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Reassign_rejects_current_assignee_without_persistent_writes()
    {
        var f = ReassignFixture.Create();
        f.Host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(
            new Dictionary<Guid, HostUserDirectoryEntry> { [f.Todo.AssigneeUserId] = new(f.Todo.AssigneeUserId, "source", "Source") });
        var result = await f.Service.ReassignAsync(f.Instance.Id, f.Actor, f.Request with { AssigneeUserId = f.Todo.AssigneeUserId });
        Assert.AreEqual(WorkflowErrorCodes.TodoAssigneeUnchanged, result.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Reassign_rejects_stale_instance_revision_without_loading_or_updating_todo()
    {
        var f = ReassignFixture.Create();
        var result = await f.Service.ReassignAsync(f.Instance.Id, f.Actor, f.Request with { ExpectedRevision = 2 });
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        await f.Query.DidNotReceive().QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    /// <summary>
    /// 强制恢复必须填写原因，并在同一事务内把暂停实例恢复到原活动待办。
    /// </summary>
    [TestMethod]
    public async Task Recover_requires_reason_and_restores_the_original_active_todo()
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var stepId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var clock = Substitute.For<IClock>();
        var ids = Substitute.For<IIdGenerator>();
        tenant.IsHost.Returns(true);
        clock.UtcNow.Returns(now);
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "leave", "LEAVE-001", null, "suspended", 3, actorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null);
        query.QuerySingleOrDefaultAsync<WorkflowActiveWorkRecord>(
                WorkflowSql.FindActiveWorkByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActiveWorkRecord(todoId, 2, stepId, 1));
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var outbox = Substitute.For<IOutboxWriter>();
        var service = CreateService(
            query, command, new TrackingTransaction(), tenant, clock, ids,
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantMemberSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(outbox));

        var missingReason = await service.RecoverAsync(
            instanceId, actorId, new RecoverWorkflowInstanceRequest(3, "  ", "recover-001"));
        var recovered = await service.RecoverAsync(
            instanceId, actorId, new RecoverWorkflowInstanceRequest(3, "卡住后强制恢复", "recover-001"));

        Assert.IsFalse(missingReason.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, missingReason.Error!.Code);
        Assert.IsTrue(recovered.IsSuccess);
        Assert.AreEqual("active", recovered.Value!.StatusKey);
        Assert.AreEqual(todoId, recovered.Value.ActiveTodoId);
        Assert.AreEqual(4, recovered.Value.Revision);
        await command.Received().ExecuteAsync(
            WorkflowSql.ResumeInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(
            WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, outbox.ReceivedCalls().Count());
    }

    /// <summary>
    /// 强制恢复端点必须绑定 recover 权限，且与改派路径分离。
    /// </summary>
    [TestMethod]
    public async Task Recover_endpoint_requires_instances_recover_permission()
    {
        var builder = WebApplication.CreateBuilder();
        var module = new WorkflowModule();
        module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build();
        module.MapEndpoints(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText ==
                "/api/v1/workflow/instances/{instanceId:guid}/recover");
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(
            FullNetPermissionPolicies.For(WorkflowPermissions.InstancesRecover),
            authorization[0].Policy);
    }

    /// <summary>暂停实例上的改派必须失败关闭，且不得改写待办。</summary>
    [TestMethod]
    public async Task Reassign_suspended_instance_returns_invalid_transition()
    {
        var f = ReassignFixture.Create();
        f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(f.Instance with { StatusKey = "suspended" });
        var result = await f.Service.ReassignAsync(f.Instance.Id, f.Actor, f.Request);
        Assert.AreEqual(WorkflowErrorCodes.InvalidTransition, result.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Reassign_endpoint_requires_instances_recover_permission()
    {
        var builder = WebApplication.CreateBuilder();
        var module = new WorkflowModule();
        module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build();
        module.MapEndpoints(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText ==
                "/api/v1/workflow/instances/{instanceId:guid}/reassign");
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(
            FullNetPermissionPolicies.For(WorkflowPermissions.InstancesRecover),
            authorization[0].Policy);
    }

    private static WorkflowInstanceRecoveryService CreateService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
        ICurrentTenant tenant, IClock clock, IIdGenerator ids, IHostUserBatchSelectionDirectory host, ITenantMemberSelectionDirectory members,
        WorkflowNotificationOutboxPublisher notifications) => new(query, command, transaction, tenant, clock, ids, host, members,
            new WorkflowTodoHandoffWriter(query, command, ids, new(command, ids, notifications), notifications), new(query, command));

    private sealed record ReassignFixture(WorkflowInstanceRecoveryService Service, IQueryExecutor Query, ICommandExecutor Command,
        IOutboxWriter Outbox, IHostUserBatchSelectionDirectory Host, TrackingTransaction Transaction, WorkflowInstanceRecord Instance,
        WorkflowTodoRuntimeRecord Todo, Guid Actor, ReassignWorkflowInstanceRequest Request, List<bool> DirectoryInside)
    {
        public static ReassignFixture Create(bool tenantScope = false, bool task = false)
        {
            var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            var transaction = new TrackingTransaction(); var tenant = new CurrentTenantAccessor();
            if (tenantScope) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "handoff", "Handoff")); else tenant.SetHost();
            var actor = Guid.CreateVersion7(); var target = Guid.CreateVersion7(); var source = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
            var scopeKey = tenantScope ? $"tenant:{tenant.Id:N}" : "host";
            var instance = new WorkflowInstanceRecord(Guid.CreateVersion7(), tenant.Id, tenantScope ? "tenant" : "host", scopeKey,
                Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "reassign", null, "active", 3, source, now, null, null, null, null, null, null);
            var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instance.Id, Guid.CreateVersion7(), source, "active", now, null, null, 2,
                "review", 1, task ? null : "all", task ? null : 2, task ? null : 2, null, null, null, task ? "human.task" : "human.approval");
            query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
            query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(todo);
            query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), instance.Id, instance.FormVersionId!.Value, "{}", "none", 4, source, now));
            query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotForReassignment, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowApprovalSlotRecord(Guid.CreateVersion7(), 1));
            query.QuerySingleOrDefaultAsync<WorkflowTaskContextRecord>(WorkflowTaskSql.FindForTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowTaskContextRecord(Guid.CreateVersion7(), todo.StepId, 1, 1, "all", 2, 2, 1));
            var host = Substitute.For<IHostUserBatchSelectionDirectory>(); var members = Substitute.For<ITenantMemberSelectionDirectory>(); var calls = new List<bool>();
            host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ =>
                { calls.Add(transaction.Inside); return new Dictionary<Guid, HostUserDirectoryEntry> { [target] = new(target, "target", "Target") }; });
            members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ =>
                { calls.Add(transaction.Inside); return new Dictionary<Guid, TenantUserDirectoryEntry> { [target] = new(target, "target", "Target") }; });
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            var outbox = Substitute.For<IOutboxWriter>(); var service = CreateService(query, command, transaction, tenant, clock, ids, host, members, new(outbox));
            return new(service, query, command, outbox, host, transaction, instance, todo, actor, new(target, 3, "交接", "reassign-001", todo.Id, 2), calls);
        }
    }

    private static string HashRequest(ReassignWorkflowInstanceRequest request)
    {
        var value = $"{request.TodoId:D}\n{request.AssigneeUserId:D}\n{request.ExpectedRevision}\n{request.ExpectedTodoRevision}\n{request.Reason?.Trim()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>判断 SQL 参数字典是否包含期望键值。</summary>
    /// <param name="parameters">SQL 参数字典。</param>
    /// <param name="name">参数名称。</param>
    /// <param name="expected">期望参数值。</param>
    /// <returns>参数存在且值相等时返回 <see langword="true"/>。</returns>
    private static bool HasValue(object? parameters, string name, object expected) =>
        parameters is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && actual?.Equals(expected) == true;

    private sealed class TrackingTransaction : ICommandTransaction
    {
        public bool HasStarted { get; private set; }
        public bool Inside { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            HasStarted = true; Inside = true;
            try { return await action(cancellationToken); } finally { Inside = false; }
        }
    }
}
