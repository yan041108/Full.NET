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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流实例暂停与普通恢复保留原待办、拒绝终态并遵守精确权限。</summary>
[TestClass]
public sealed class WorkflowInstanceManagementServiceTests
{
    /// <summary>暂停只改实例状态，必须保留原活动待办且不得写入通知。</summary>
    [TestMethod]
    public async Task Pause_keeps_the_original_todo_and_does_not_publish_notifications()
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var outbox = Substitute.For<IOutboxWriter>();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(
            CreateQuery(instanceId, actorId, "active", 3, todoId, receipt: null),
            command,
            actorId,
            outbox);

        var result = await service.PauseAsync(
            instanceId, actorId, new PauseWorkflowInstanceRequest(3, "等待材料", "pause-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("suspended", result.Value!.StatusKey);
        Assert.AreEqual(todoId, result.Value.ActiveTodoId);
        Assert.AreEqual(4, result.Value.Revision);
        await command.Received().ExecuteAsync(
            WorkflowSql.SuspendInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(
            WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(
            WorkflowSql.InsertStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, outbox.ReceivedCalls().Count());
    }

    /// <summary>同一操作人、幂等键和摘要重放必须返回当前快照且不再写入。</summary>
    [TestMethod]
    public async Task Pause_same_idempotency_semantics_returns_the_original_todo()
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var command = Substitute.For<ICommandExecutor>();
        var receipt = new WorkflowActionReceiptRecord(
            "pause", actorId, 4, "pause-001", HashLifecycle(3, "等待材料"), null);
        var service = CreateService(
            CreateQuery(instanceId, actorId, "suspended", 4, todoId, receipt),
            command,
            actorId);

        var result = await service.PauseAsync(
            instanceId, actorId, new PauseWorkflowInstanceRequest(3, "等待材料", "pause-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(todoId, result.Value!.ActiveTodoId);
        Assert.AreEqual("suspended", result.Value.StatusKey);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    /// <summary>终态实例拒绝暂停，且不得进入写入路径。</summary>
    [TestMethod]
    public async Task Pause_terminal_instance_returns_instance_terminal()
    {
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            CreateQuery(instanceId, actorId, "completed", 3, Guid.CreateVersion7(), receipt: null),
            command,
            actorId);

        var result = await service.PauseAsync(
            instanceId, actorId, new PauseWorkflowInstanceRequest(3, null, "pause-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.InstanceTerminal, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    /// <summary>已暂停实例再次暂停属于无效转换，而不是终态错误。</summary>
    [TestMethod]
    public async Task Pause_already_suspended_returns_invalid_transition()
    {
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            CreateQuery(instanceId, actorId, "suspended", 3, Guid.CreateVersion7(), receipt: null),
            command,
            actorId);

        var result = await service.PauseAsync(
            instanceId, actorId, new PauseWorkflowInstanceRequest(3, null, "pause-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.InvalidTransition, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    /// <summary>过期修订号必须在写入前失败关闭。</summary>
    [TestMethod]
    public async Task Pause_stale_revision_returns_revision_conflict()
    {
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            CreateQuery(instanceId, actorId, "active", 4, Guid.CreateVersion7(), receipt: null),
            command,
            actorId);

        var result = await service.PauseAsync(
            instanceId, actorId, new PauseWorkflowInstanceRequest(3, null, "pause-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    /// <summary>受影响行数为 0 时必须按修订冲突回滚，避免部分提交。</summary>
    [TestMethod]
    public async Task Pause_zero_affected_rows_returns_revision_conflict()
    {
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var service = CreateService(
            CreateQuery(instanceId, actorId, "active", 3, Guid.CreateVersion7(), receipt: null),
            command,
            actorId);

        var result = await service.PauseAsync(
            instanceId, actorId, new PauseWorkflowInstanceRequest(3, null, "pause-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
    }

    /// <summary>普通恢复必须回到运行状态并保留原待办，不得新建步骤或待办。</summary>
    [TestMethod]
    public async Task Resume_restores_the_original_active_todo_without_creating_work()
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(
            CreateQuery(instanceId, actorId, "suspended", 4, todoId, receipt: null),
            command,
            actorId);

        var result = await service.ResumeAsync(
            instanceId, actorId, new ResumeWorkflowInstanceRequest(4, null, "resume-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(todoId, result.Value.ActiveTodoId);
        Assert.AreEqual(5, result.Value.Revision);
        await command.Received().ExecuteAsync(
            WorkflowSql.ResumeInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(
            WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>暂停端点必须绑定独立 pause 权限。</summary>
    [TestMethod]
    public async Task Pause_endpoint_requires_instances_pause_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/instances/{instanceId:guid}/pause",
            WorkflowPermissions.InstancesPause);
    }

    /// <summary>普通恢复端点必须绑定独立 resume 权限。</summary>
    [TestMethod]
    public async Task Resume_endpoint_requires_instances_resume_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/instances/{instanceId:guid}/resume",
            WorkflowPermissions.InstancesResume);
    }

    /// <summary>实例详情必须附带当前活动多人审批步骤的权威票数进度。</summary>
    [TestMethod]
    public async Task GetAsync_includes_active_multi_approval_progress()
    {
        var instanceId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "purchase", "PO-001", "active", 5, actorId, now,
                null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowTodoTimeoutSummaryRecord>(
                WorkflowSql.FindActiveTodoTimeoutByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoTimeoutSummaryRecord(todoId, null, 0, null));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceApprovalProgressRecord>(
                WorkflowSql.FindActiveStepApprovalProgressByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceApprovalProgressRecord("review", "nOfM", 2, 1, 0, 2));
        var service = CreateService(query, Substitute.For<ICommandExecutor>(), actorId);

        var result = await service.GetAsync(instanceId, actorId);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("review", result.Value!.ActiveNodeKey);
        Assert.AreEqual("nOfM", result.Value.ApprovalModeKey);
        Assert.AreEqual(2, result.Value.RequiredApprovalCount);
        Assert.AreEqual(1, result.Value.ApprovedCount);
        Assert.AreEqual(0, result.Value.RejectedCount);
        Assert.AreEqual(2, result.Value.PendingCount);
    }

    /// <summary>构造暂停/恢复服务及其查询替身。</summary>
    /// <param name="query">已配置的查询执行器。</param>
    /// <param name="command">命令执行器。</param>
    /// <param name="actorId">当前操作人标识，用于时钟以外的无关构造。</param>
    /// <param name="outbox">可选 Outbox 写入器。</param>
    /// <returns>可直接调用暂停或恢复的服务实例。</returns>
    private static WorkflowInstanceManagementService CreateService(
        IQueryExecutor query,
        ICommandExecutor command,
        Guid actorId,
        IOutboxWriter? outbox = null)
    {
        _ = actorId;
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        var notificationPublisher = new WorkflowNotificationOutboxPublisher(
            outbox ?? Substitute.For<IOutboxWriter>());
        return new WorkflowInstanceManagementService(
            query,
            command,
            new TrackingTransaction(),
            tenant,
            clock,
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            new WorkflowAutomaticTransitionWriter(command, ids, ccWriter),
            new WorkflowApprovalActivationWriter(command, ids, notificationPublisher),
            WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(),
            notificationPublisher);
    }

    /// <summary>构造暂停/恢复路径需要的实例、回执和活动工作查询。</summary>
    /// <param name="instanceId">工作流实例标识。</param>
    /// <param name="actorId">发起人标识。</param>
    /// <param name="statusKey">当前实例状态键。</param>
    /// <param name="revision">当前修订号。</param>
    /// <param name="todoId">活动待办标识。</param>
    /// <param name="receipt">已提交动作回执；首次调用传空。</param>
    /// <returns>已配置返回值的查询执行器。</returns>
    private static IQueryExecutor CreateQuery(
        Guid instanceId,
        Guid actorId,
        string statusKey,
        long revision,
        Guid todoId,
        WorkflowActionReceiptRecord? receipt)
    {
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "leave", "LEAVE-001", statusKey, revision, actorId, now,
                null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(receipt);
        query.QuerySingleOrDefaultAsync<WorkflowActiveWorkRecord>(
                WorkflowSql.FindActiveWorkByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActiveWorkRecord(todoId, 2, Guid.CreateVersion7(), 1));
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                todoId, instanceId, Guid.CreateVersion7(), actorId, "active", now, null, null, 2));
        return query;
    }

    /// <summary>断言指定实例生命周期端点绑定精确权限策略。</summary>
    /// <param name="route">端点路由模板。</param>
    /// <param name="permission">期望权限码。</param>
    private static async Task AssertEndpointPermissionAsync(string route, string permission)
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
            .Single(candidate => candidate.RoutePattern.RawText == route);
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(FullNetPermissionPolicies.For(permission), authorization[0].Policy);
    }

    /// <summary>生成与服务端一致的暂停或恢复请求摘要。</summary>
    /// <param name="expectedRevision">期望修订号。</param>
    /// <param name="reason">可选原因。</param>
    /// <returns>小写十六进制 SHA-256 摘要。</returns>
    private static string HashLifecycle(long expectedRevision, string? reason)
    {
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{expectedRevision}\n{normalized}")));
    }

    /// <summary>记录事务是否启动，并把回调直接执行以便单元测试观察写入。</summary>
    private sealed class TrackingTransaction : ICommandTransaction
    {
        /// <summary>在调用方事务回调中执行工作，不引入真实数据库会话。</summary>
        /// <typeparam name="T">回调返回值类型。</typeparam>
        /// <param name="action">事务内操作。</param>
        /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
        /// <returns>回调结果。</returns>
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
