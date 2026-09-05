using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证扫描去重入队、租约处理后的死信暂停以及管理 API 精确权限。</summary>
[TestClass]
public sealed class WorkflowRecoveryWorkerTests
{
    /// <summary>三类扫描都必须写入未关闭任务，重复扫描仍走占用去重 SQL。</summary>
    [TestMethod]
    public async Task Scanner_enqueues_three_kinds_with_occupancy_insert()
    {
        var candidate = new WorkflowRecoveryScanCandidate(
            null, "host", "host", Guid.CreateVersion7(), null);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowRecoveryScanCandidate>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryScanCandidate>>([candidate]));
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var scanner = new WorkflowRecoveryScanner(query, command, clock, ids);

        await scanner.ScanAsync(CancellationToken.None);
        await scanner.ScanAsync(CancellationToken.None);

        await command.Received(6).ExecuteAsync(
            WorkflowRecoverySql.InsertOpenTask, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>耗尽尝试必须暂停仍活动的实例并完成死信任务。</summary>
    [TestMethod]
    public async Task Processor_dead_letters_and_suspends_stuck_active_instance()
    {
        var task = CreateTask(WorkflowRecoveryKinds.StuckInstance, attemptCount: 7);
        var instance = CreateInstance(task.InstanceId, "active", 3);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowRecoveryTaskRecord>(
                WorkflowRecoverySql.ClaimTasksSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryTaskRecord>>([task]));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowTodoRecord?)null);
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var processor = CreateProcessor(query, command, maxAttempts: 8);

        Assert.AreEqual(1, await processor.ProcessPendingAsync(CancellationToken.None));
        await command.Received().ExecuteAsync(
            WorkflowSql.SuspendInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received().ExecuteAsync(
            WorkflowRecoverySql.CompleteTask, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>实例已暂停时必须直接成功关闭任务，不得再次暂停。</summary>
    [TestMethod]
    public async Task Processor_succeeds_when_instance_is_already_suspended()
    {
        var task = CreateTask(WorkflowRecoveryKinds.ExpiredLease, attemptCount: 0);
        var instance = CreateInstance(task.InstanceId, "suspended", 4);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowRecoveryTaskRecord>(
                WorkflowRecoverySql.ClaimTasksSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryTaskRecord>>([task]));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var processor = CreateProcessor(query, command, maxAttempts: 8);

        Assert.AreEqual(1, await processor.ProcessPendingAsync(CancellationToken.None));
        await command.DidNotReceive().ExecuteAsync(
            WorkflowSql.SuspendInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received().ExecuteAsync(
            WorkflowRecoverySql.CompleteTask, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>过期租约但已有活动待办时只清理实例租约并成功关闭任务。</summary>
    [TestMethod]
    public async Task Processor_clears_expired_instance_lease_when_active_todo_exists()
    {
        var task = CreateTask(WorkflowRecoveryKinds.ExpiredLease, attemptCount: 0);
        var instance = CreateInstance(task.InstanceId, "active", 2);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowRecoveryTaskRecord>(
                WorkflowRecoverySql.ClaimTasksSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryTaskRecord>>([task]));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                Guid.CreateVersion7(), task.InstanceId, Guid.CreateVersion7(), Guid.CreateVersion7(),
                "active", DateTimeOffset.UtcNow, null, null, 1));
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var processor = CreateProcessor(query, command, maxAttempts: 8);

        Assert.AreEqual(1, await processor.ProcessPendingAsync(CancellationToken.None));
        await command.Received().ExecuteAsync(
            WorkflowRecoverySql.ClearInstanceLease, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(
            WorkflowSql.SuspendInstanceWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>丢失租约世代时不得提交死信暂停。</summary>
    [TestMethod]
    public async Task Processor_aborts_when_complete_loses_the_lease()
    {
        var task = CreateTask(WorkflowRecoveryKinds.StuckInstance, attemptCount: 7);
        var instance = CreateInstance(task.InstanceId, "active", 3);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowRecoveryTaskRecord>(
                WorkflowRecoverySql.ClaimTasksSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryTaskRecord>>([task]));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowTodoRecord?)null);
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(WorkflowRecoverySql.CompleteTask, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var processor = CreateProcessor(query, command, maxAttempts: 8);

        Assert.AreEqual(1, await processor.ProcessPendingAsync(CancellationToken.None));
        await command.DidNotReceive().ExecuteAsync(
            WorkflowSql.InsertExecutionLog, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>人工重试只能把失败或死信任务重新入队。</summary>
    [TestMethod]
    public async Task Retry_requeues_failed_task_and_rejects_pending()
    {
        var failed = CreateTask(WorkflowRecoveryKinds.StuckInstance, attemptCount: 3) with
        {
            StatusKey = WorkflowRecoveryStatuses.Failed,
            Revision = 4,
        };
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var failedService = CreateService(CreateTaskQuery(failed), command);
        var retry = await failedService.RetryAsync(
            failed.Id, Guid.CreateVersion7(), new RetryWorkflowRecoveryTaskRequest(4, "卡住", "retry-1"));
        Assert.IsTrue(retry.IsSuccess);
        await command.Received().ExecuteAsync(
            WorkflowRecoverySql.RequeueTask, Arg.Any<object?>(), Arg.Any<CancellationToken>());

        var pending = failed with { StatusKey = WorkflowRecoveryStatuses.Pending };
        var pendingService = CreateService(CreateTaskQuery(pending), Substitute.For<ICommandExecutor>());
        var rejected = await pendingService.RetryAsync(
            pending.Id, Guid.CreateVersion7(), new RetryWorkflowRecoveryTaskRequest(4, "卡住", "retry-2"));
        Assert.IsFalse(rejected.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.RecoveryRetryInvalid, rejected.Error!.Code);
    }

    /// <summary>对账在源条件仍在时必须拒绝；活动待办已补齐则可关闭。</summary>
    [TestMethod]
    public async Task Reconcile_rejects_still_stuck_and_closes_when_todo_exists()
    {
        var task = CreateTask(WorkflowRecoveryKinds.StuckInstance, attemptCount: 3) with
        {
            StatusKey = WorkflowRecoveryStatuses.Failed,
            Revision = 2,
        };
        var stuckQuery = CreateTaskQuery(task);
        stuckQuery.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(CreateInstance(task.InstanceId, "active", 5));
        stuckQuery.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowTodoRecord?)null);
        var stuck = await CreateService(stuckQuery, Substitute.For<ICommandExecutor>()).ReconcileAsync(
            task.Id, Guid.CreateVersion7(), new ReconcileWorkflowRecoveryTaskRequest(2, null, "rec-1"));
        Assert.IsFalse(stuck.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.RecoveryReconcileInvalid, stuck.Error!.Code);

        var healedQuery = CreateTaskQuery(task);
        healedQuery.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(CreateInstance(task.InstanceId, "active", 5));
        healedQuery.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                Guid.CreateVersion7(), task.InstanceId, Guid.CreateVersion7(), Guid.CreateVersion7(),
                "active", DateTimeOffset.UtcNow, null, null, 1));
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var closed = await CreateService(healedQuery, command).ReconcileAsync(
            task.Id, Guid.CreateVersion7(), new ReconcileWorkflowRecoveryTaskRequest(2, "已补待办", "rec-2"));
        Assert.IsTrue(closed.IsSuccess);
        await command.Received().ExecuteAsync(
            WorkflowRecoverySql.MarkTaskSucceeded, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>过期修订号必须返回冲突，便于客户端携带最新 revision 重试。</summary>
    [TestMethod]
    public async Task Retry_stale_revision_returns_conflict()
    {
        var failed = CreateTask(WorkflowRecoveryKinds.ExpiredLease, attemptCount: 1) with
        {
            StatusKey = WorkflowRecoveryStatuses.Failed,
            Revision = 6,
        };
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(WorkflowRecoverySql.RequeueTask, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var result = await CreateService(CreateTaskQuery(failed), command).RetryAsync(
            failed.Id, Guid.CreateVersion7(), new RetryWorkflowRecoveryTaskRequest(5, "重试", "retry-stale"));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
    }

    /// <summary>查询、重试和对账端点必须绑定独立精确权限。</summary>
    [TestMethod]
    public async Task Recovery_endpoints_require_exact_permissions()
    {
        await AssertEndpointPermissionAsync("/api/v1/workflow/recovery-tasks/", WorkflowPermissions.RecoveryTasksRead);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/recovery-tasks/{taskId:guid}", WorkflowPermissions.RecoveryTasksRead);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/recovery-tasks/{taskId:guid}/retry", WorkflowPermissions.RecoveryTasksRetry);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/recovery-tasks/{taskId:guid}/reconcile", WorkflowPermissions.RecoveryTasksReconcile);
    }

    private static WorkflowRecoveryBatchProcessor CreateProcessor(
        IQueryExecutor query,
        ICommandExecutor command,
        int maxAttempts)
    {
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return new WorkflowRecoveryBatchProcessor(
            query,
            command,
            new ImmediateTransaction(),
            clock,
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Options.Create(new WorkflowRecoveryWorkerOptions { MaxAttempts = maxAttempts }),
            NullLogger<WorkflowRecoveryBatchProcessor>.Instance);
    }

    private static WorkflowRecoveryTaskService CreateService(IQueryExecutor query, ICommandExecutor command)
    {
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return new WorkflowRecoveryTaskService(
            query,
            command,
            new ImmediateTransaction(),
            tenant,
            clock,
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
    }

    private static IQueryExecutor CreateTaskQuery(WorkflowRecoveryTaskRecord task)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowRecoveryTaskRecord>(
                WorkflowRecoverySql.FindTaskById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(task);
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowActionReceiptRecord?)null);
        query.QuerySingleOrDefaultAsync<long>(
                WorkflowRecoverySql.CountTasksForScope, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        query.QueryAsync<WorkflowRecoveryTaskRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRecoveryTaskRecord>>([task]));
        return query;
    }

    private static WorkflowRecoveryTaskRecord CreateTask(string kindKey, int attemptCount) =>
        new(
            Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), null, kindKey,
            WorkflowRecoveryStatuses.Pending, attemptCount, 1, "owner",
            DateTimeOffset.UtcNow.AddMinutes(2), 1, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static WorkflowInstanceRecord CreateInstance(Guid instanceId, string statusKey, long revision) =>
        new(
            instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "leave", "LEAVE-001", statusKey, revision, Guid.CreateVersion7(), DateTimeOffset.UtcNow,
            null, null, null, null, null, null);

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

    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
