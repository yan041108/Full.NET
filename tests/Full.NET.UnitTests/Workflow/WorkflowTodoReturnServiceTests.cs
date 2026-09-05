using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
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
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证审批退回只命中合法历史人工节点，并原子创建目标节点的新待办。</summary>
[TestClass]
public sealed class WorkflowTodoReturnServiceTests
{
    /// <summary>合法目标查询端点与退回动作必须共同绑定独立退回权限。</summary>
    [TestMethod]
    public async Task Return_endpoints_require_todos_return_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/return-targets",
            WorkflowPermissions.TodosReturn);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/return",
            WorkflowPermissions.TodosReturn);
    }

    /// <summary>提交自动节点或其他实例节点时，服务端再次校验必须失败且不产生写入。</summary>
    [TestMethod]
    public async Task Return_missing_legal_target_fails_before_writes()
    {
        var fixture = CreateFixture(returnTarget: null);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ReturnWorkflowTodoRequest(
                Guid.CreateVersion7(), 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.TodoReturnTargetInvalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    /// <summary>成功退回必须关闭来源工作、失效目标后的旧链、推进实例并创建目标审批的新工作。</summary>
    [TestMethod]
    public async Task Return_to_completed_human_step_creates_a_new_target_todo()
    {
        var target = new WorkflowTodoReturnTargetRecord(
            Guid.CreateVersion7(), "manager", Guid.CreateVersion7(),
            4, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
        var fixture = CreateFixture(target);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ReturnWorkflowTodoRequest(
                target.StepId, 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(8, result.Value.Revision);
        Assert.IsNotNull(result.Value.ActiveTodoId);
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.ReturnStepWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.RollBackCompletedStepsFromTarget, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.InsertStep, Arg.Is<object?>(value => HasValue(value, "AssignedUserId", target.AssigneeUserId)),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.InsertTodo, Arg.Is<object?>(value => HasValue(value, "AssigneeUserId", target.AssigneeUserId)),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.InsertActionRecord, Arg.Is<object?>(value => HasValue(value, "ActionKey", "return")),
            Arg.Any<CancellationToken>());
        Assert.AreEqual(1, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>相同操作者、幂等键和目标语义重放时返回首次结果，不受实例后续状态变化影响。</summary>
    [TestMethod]
    public async Task Return_same_idempotency_semantics_replays_without_writes()
    {
        var targetStepId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"return\n{targetStepId:D}\n3\n{{}}\n资料不完整")));
        var originalTodoId = Guid.CreateVersion7();
        var receipt = new WorkflowActionReceiptRecord(
            "return", actorId, 8, "return-001", requestHash, originalTodoId);
        var fixture = CreateFixture(
            returnTarget: null, receipt: receipt, instanceRevision: 12, actorId: actorId);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            actorId,
            new ReturnWorkflowTodoRequest(
                targetStepId, 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(8, result.Value!.Revision);
        Assert.AreEqual(originalTodoId, result.Value.ActiveTodoId);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    /// <summary>两个同语义请求竞争实例 CAS 时，失败方必须读取胜方回执并返回同一个首次结果。</summary>
    [TestMethod]
    public async Task Return_concurrent_same_idempotency_key_replays_winner_after_cas_loss()
    {
        var target = new WorkflowTodoReturnTargetRecord(
            Guid.CreateVersion7(), "manager", Guid.CreateVersion7(), 4,
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
        var actorId = Guid.CreateVersion7();
        var resultTodoId = Guid.CreateVersion7();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"return\n{target.StepId:D}\n3\n{{}}\n资料不完整")));
        var receipt = new WorkflowActionReceiptRecord(
            "return", actorId, 8, "return-001", requestHash, resultTodoId);
        var fixture = CreateFixture(target, concurrentReceipt: receipt, actorId: actorId);
        fixture.Command.ExecuteAsync(
                WorkflowSql.UpdateFormSubmissionWithRevision,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            actorId,
            new ReturnWorkflowTodoRequest(
                target.StepId, 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(8, result.Value!.Revision);
        Assert.AreEqual(resultTodoId, result.Value.ActiveTodoId);
        Assert.AreEqual(1, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>JSON 显式传入空幂等键时必须返回稳定校验错误，不能抛出空引用异常。</summary>
    [TestMethod]
    public async Task Return_null_idempotency_key_returns_validation_error()
    {
        var fixture = CreateFixture(returnTarget: null);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ReturnWorkflowTodoRequest(
                Guid.CreateVersion7(), 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", null!));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    /// <summary>构造退回服务需要的可信实例、活动待办、不可变资产与可选目标。</summary>
    /// <param name="returnTarget">服务端复核得到的合法目标；为空表示非法。</param>
    /// <param name="receipt">可选的已提交动作回执，用于幂等重放。</param>
    /// <param name="instanceRevision">当前实例修订号。</param>
    /// <param name="actorId">可选的固定操作人标识。</param>
    /// <returns>包含服务和可观察替身的测试夹具。</returns>
    private static ReturnFixture CreateFixture(
        WorkflowTodoReturnTargetRecord? returnTarget,
        WorkflowActionReceiptRecord? receipt = null,
        WorkflowActionReceiptRecord? concurrentReceipt = null,
        long instanceRevision = 7,
        Guid? actorId = null)
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        var stepId = Guid.CreateVersion7();
        var resolvedActorId = actorId ?? Guid.CreateVersion7();
        var definitionVersionId = Guid.CreateVersion7();
        var formVersionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
                WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRuntimeRecord(
                todoId, instanceId, stepId, resolvedActorId, "active", now, null, null, 3, "finance", 1,
                null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", definitionVersionId, formVersionId,
                "purchase", "PO-001", "active", instanceRevision, resolvedActorId, now,
                null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(
                receipt,
                concurrentReceipt ?? receipt);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), resolvedActorId,
                "active", now, null, null, 1));
        query.QuerySingleOrDefaultAsync<WorkflowTodoReturnTargetRecord>(
                WorkflowSql.FindTodoReturnTarget, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(returnTarget);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
                WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(
                definitionVersionId, formVersionId,
                "{\"schemaVersion\":1,\"nodes\":[{\"nodeKey\":\"start\",\"nodeTypeKey\":\"start\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[\"manager\"]}},{\"nodeKey\":\"manager\",\"nodeTypeKey\":\"human.approval\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[\"finance\"]}},{\"nodeKey\":\"finance\",\"nodeTypeKey\":\"human.approval\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[\"end\"]}},{\"nodeKey\":\"end\",\"nodeTypeKey\":\"end\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[]}}]}",
                "{\"schemaVersion\":1,\"adapterVersion\":1,\"sections\":[]}"));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
                WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(
                Guid.CreateVersion7(), instanceId, formVersionId, "{}", "none", 2, resolvedActorId, now));

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
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        var notificationPublisher = new WorkflowNotificationOutboxPublisher(outbox);
        var service = new WorkflowTodoManagementService(
            query, command, new TrackingTransaction(), tenant, clock, ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            new WorkflowAutomaticTransitionWriter(command, ids, ccWriter),
            new WorkflowApprovalActivationWriter(command, ids, notificationPublisher),
            notificationPublisher,
            WorkflowTodoManagementTestDependencies.CreateCountersignService(query, command, tenant));
        return new ReturnFixture(service, query, command, outbox, todoId, resolvedActorId);
    }

    /// <summary>判断匿名 SQL 参数对象是否携带指定名称和值。</summary>
    /// <param name="parameters">待检查参数对象。</param>
    /// <param name="name">属性名。</param>
    /// <param name="expected">期望值。</param>
    /// <returns>属性存在且值相等时返回 <see langword="true"/>。</returns>
    private static bool HasValue(object? parameters, string name, object expected) =>
        parameters is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && actual?.Equals(expected) == true;

    /// <summary>断言指定待办端点只绑定退回精确权限。</summary>
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

    /// <summary>保存退回测试需要的服务与替身。</summary>
    /// <param name="Service">待测服务。</param>
    /// <param name="Query">可观察查询执行器。</param>
    /// <param name="Command">可观察命令执行器。</param>
    /// <param name="Outbox">可观察 Outbox 写入器。</param>
    /// <param name="TodoId">当前待办标识。</param>
    /// <param name="ActorId">当前办理人标识。</param>
    private sealed record ReturnFixture(
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
