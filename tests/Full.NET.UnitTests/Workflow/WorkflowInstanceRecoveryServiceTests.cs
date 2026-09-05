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
    public async Task Reassign_validates_target_before_transaction_and_atomically_writes_notification()
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var stepId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var clock = Substitute.For<IClock>();
        var ids = Substitute.For<IIdGenerator>();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        var outbox = Substitute.For<IOutboxWriter>();
        var transaction = new TrackingTransaction();
        tenant.IsHost.Returns(true);
        clock.UtcNow.Returns(now);
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        hostUsers.FindActiveHostUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(transaction.HasStarted, "Identity 目标用户校验必须发生在 Workflow 本地事务之外。");
                return new Dictionary<Guid, HostUserDirectoryEntry>
                {
                    [assigneeId] = new(assigneeId, "next", "下一办理人"),
                };
            });
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "leave", "LEAVE-001", "active", 3, actorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                todoId, instanceId, stepId, Guid.CreateVersion7(), "active", now, null, null, 2));
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var service = new WorkflowInstanceRecoveryService(
            query, command, transaction, tenant, clock, ids, hostUsers, tenantUsers,
            new WorkflowNotificationOutboxPublisher(outbox));

        var result = await service.ReassignAsync(
            instanceId, actorId, new ReassignWorkflowInstanceRequest(assigneeId, 3, "交接", "reassign-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(4, result.Value!.Revision);
        Assert.AreEqual(todoId, result.Value.ActiveTodoId);
        Assert.IsTrue(transaction.HasStarted);
        await hostUsers.Received(1).FindActiveHostUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.SequenceEqual(new[] { assigneeId })),
            Arg.Any<CancellationToken>());
        await tenantUsers.DidNotReceiveWithAnyArgs().FindActiveTenantUsersAsync(default!, default);
        Assert.AreEqual(5, command.ReceivedCalls().Count());
        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoAssigned,
            1,
            Arg.Is<WorkflowTodoAssignedIntegrationEvent>(message => message != null &&
                message.InstanceId == instanceId &&
                message.TodoId == todoId &&
                message.RecipientUserId == assigneeId),
            Arg.Any<IntegrationEventMetadata>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Reassign_same_idempotency_semantics_returns_the_original_active_todo()
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        var request = new ReassignWorkflowInstanceRequest(assigneeId, 3, "交接", "reassign-001");
        tenant.IsHost.Returns(true);
        hostUsers.FindActiveHostUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>
            {
                [assigneeId] = new(assigneeId, "next", "下一办理人"),
            });
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "leave", "LEAVE-001", "active", 4, actorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActionReceiptRecord(
                "reassign", actorId, 4, request.IdempotencyKey, HashRequest(request)));
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                todoId, instanceId, Guid.CreateVersion7(), assigneeId, "active", now, null, null, 3));
        var service = new WorkflowInstanceRecoveryService(
            query, command, new TrackingTransaction(), tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), hostUsers, tenantUsers,
            new WorkflowNotificationOutboxPublisher(Substitute.For<IOutboxWriter>()));

        var result = await service.ReassignAsync(instanceId, actorId, request);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(todoId, result.Value!.ActiveTodoId);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Reassign_rejects_inactive_target_before_starting_transaction()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        var transaction = new TrackingTransaction();
        tenant.IsHost.Returns(true);
        hostUsers.FindActiveHostUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        var service = new WorkflowInstanceRecoveryService(
            query, command, transaction, tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), hostUsers,
            Substitute.For<ITenantUserSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(Substitute.For<IOutboxWriter>()));

        var result = await service.ReassignAsync(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            new ReassignWorkflowInstanceRequest(Guid.CreateVersion7(), 1, null, "inactive-target"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.TodoAssigneeNotFound, result.Error!.Code);
        Assert.IsFalse(transaction.HasStarted);
        Assert.AreEqual(0, query.ReceivedCalls().Count());
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Reassign_rejects_current_assignee_without_persistent_writes()
    {
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        tenant.IsHost.Returns(true);
        hostUsers.FindActiveHostUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>
            {
                [assigneeId] = new(assigneeId, "same", "当前办理人"),
            });
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "leave", "LEAVE-002", "active", 2, actorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), assigneeId,
                "active", now, null, null, 1));
        var service = new WorkflowInstanceRecoveryService(
            query, command, new TrackingTransaction(), tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), hostUsers,
            Substitute.For<ITenantUserSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(Substitute.For<IOutboxWriter>()));

        var result = await service.ReassignAsync(
            instanceId, actorId,
            new ReassignWorkflowInstanceRequest(assigneeId, 2, null, "same-assignee"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.TodoAssigneeUnchanged, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Reassign_rejects_stale_instance_revision_without_loading_or_updating_todo()
    {
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        tenant.IsHost.Returns(true);
        hostUsers.FindActiveHostUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>
            {
                [assigneeId] = new(assigneeId, "next", "下一办理人"),
            });
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "leave", "LEAVE-003", "active", 4, actorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null);
        var service = new WorkflowInstanceRecoveryService(
            query, command, new TrackingTransaction(), tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), hostUsers,
            Substitute.For<ITenantUserSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(Substitute.For<IOutboxWriter>()));

        var result = await service.ReassignAsync(
            instanceId, actorId,
            new ReassignWorkflowInstanceRequest(assigneeId, 3, null, "stale-revision"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
        await query.DidNotReceive().QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
            WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, command.ReceivedCalls().Count());
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
                "leave", "LEAVE-001", "suspended", 3, actorId, now, null, null, null, null, null, null));
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
        var service = new WorkflowInstanceRecoveryService(
            query, command, new TrackingTransaction(), tenant, clock, ids,
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            Substitute.For<ITenantUserSelectionDirectory>(),
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
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        tenant.IsHost.Returns(true);
        hostUsers.FindActiveHostUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>
            {
                [assigneeId] = new(assigneeId, "next", "下一办理人"),
            });
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "leave", "LEAVE-001", "suspended", 3, actorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null);
        var service = new WorkflowInstanceRecoveryService(
            query, command, new TrackingTransaction(), tenant, Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(), hostUsers,
            Substitute.For<ITenantUserSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(Substitute.For<IOutboxWriter>()));

        var result = await service.ReassignAsync(
            instanceId, actorId, new ReassignWorkflowInstanceRequest(assigneeId, 3, null, "reassign-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.InvalidTransition, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
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

    private static string HashRequest(ReassignWorkflowInstanceRequest request)
    {
        var value = $"{request.AssigneeUserId:D}\n{request.ExpectedRevision}\n{request.Reason?.Trim()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed class TrackingTransaction : ICommandTransaction
    {
        public bool HasStarted { get; private set; }

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            HasStarted = true;
            return action(cancellationToken);
        }
    }
}
