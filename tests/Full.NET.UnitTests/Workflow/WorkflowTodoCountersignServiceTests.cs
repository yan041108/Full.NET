using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证前加签、后加签、取消与幂等语义。</summary>
[TestClass]
public sealed class WorkflowTodoCountersignServiceTests
{
    [TestMethod]
    public async Task Countersign_endpoints_require_todos_countersign_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/countersign-chain",
            WorkflowPermissions.TodosCountersign);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/countersign",
            WorkflowPermissions.TodosCountersign);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/countersign/cancel",
            WorkflowPermissions.TodosCountersign);
    }

    [TestMethod]
    public async Task Countersign_invalid_assignee_fails_before_writes()
    {
        var fixture = CreateFixture(activeChain: null, hostUsers: []);
        var result = await fixture.Service.CountersignAsync(
            fixture.TodoId,
            fixture.ActorId,
            new CountersignWorkflowTodoRequest(
                "before",
                [Guid.CreateVersion7()],
                3,
                "请协助审批",
                "cs-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.TodoCountersignAssigneeInvalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Countersign_before_creates_first_countersign_todo_and_suspends_origin()
    {
        var assignee = Guid.CreateVersion7();
        var fixture = CreateFixture(activeChain: null, hostUsers: [assignee]);
        var result = await fixture.Service.CountersignAsync(
            fixture.TodoId,
            fixture.ActorId,
            new CountersignWorkflowTodoRequest(
                "before",
                [assignee],
                3,
                "请协助审批",
                "cs-001"));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.SuspendOriginTodoForBeforeCountersign, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.InsertTodo, Arg.Is<object?>(value => HasValue(value, "AssigneeUserId", assignee)),
            Arg.Any<CancellationToken>());
        Assert.AreEqual(1, fixture.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Countersign_same_idempotency_replays_without_writes()
    {
        var assignee = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"countersign\nbefore\n3\n请协助审批\n{assignee:D}")));
        var resultTodoId = Guid.CreateVersion7();
        var receipt = new WorkflowActionReceiptRecord(
            "countersign.before", actorId, 8, "cs-001", requestHash, resultTodoId);
        var fixture = CreateFixture(activeChain: null, hostUsers: [assignee], receipt: receipt, actorId: actorId);

        var result = await fixture.Service.CountersignAsync(
            fixture.TodoId,
            fixture.ActorId,
            new CountersignWorkflowTodoRequest(
                "before",
                [assignee],
                3,
                "请协助审批",
                "cs-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(resultTodoId, result.Value!.ActiveTodoId);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

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

    private static CountersignFixture CreateFixture(
        WorkflowCountersignChainRecord? activeChain,
        IReadOnlyList<Guid> hostUsers,
        WorkflowActionReceiptRecord? receipt = null,
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
                todoId, instanceId, stepId, resolvedActorId, "active", now, null, null, 3, "finance", 1));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", definitionVersionId, formVersionId,
                "purchase", "PO-1", "active", 7, resolvedActorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(receipt);
        query.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
                WorkflowSql.FindActiveCountersignChainByOriginTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(activeChain);
        query.QueryAsync<WorkflowCountersignItemRecord>(
                WorkflowSql.ListCountersignItemsByChain, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowCountersignItemRecord>>([]));

        var hostDirectory = Substitute.For<IHostUserBatchSelectionDirectory>();
        hostDirectory.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(hostUsers.ToDictionary(
                id => id,
                id => new HostUserDirectoryEntry(id, $"user-{id:N}".Substring(0, 8), $"User {id:N}".Substring(0, 12))));

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        var outbox = Substitute.For<IOutboxWriter>();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var service = new WorkflowTodoCountersignService(
            query,
            command,
            new TrackingTransaction(),
            tenant,
            Substitute.For<IClock>(),
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            hostDirectory,
            Substitute.For<ITenantUserSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(outbox),
            new WorkflowAutomaticTransitionWriter(command, ids, ccWriter));

        return new CountersignFixture(service, query, command, outbox, todoId, resolvedActorId);
    }

    private static bool HasValue(object? parameters, string name, object expected) =>
        parameters is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && actual?.Equals(expected) == true;

    private sealed class TrackingTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }

    private sealed record CountersignFixture(
        WorkflowTodoCountersignService Service,
        IQueryExecutor Query,
        ICommandExecutor Command,
        IOutboxWriter Outbox,
        Guid TodoId,
        Guid ActorId);
}
