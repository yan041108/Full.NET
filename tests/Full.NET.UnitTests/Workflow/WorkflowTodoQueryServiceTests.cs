using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流待办/已办分页查询与端点授权。</summary>
[TestClass]
public sealed class WorkflowTodoQueryServiceTests
{
    [TestMethod]
    public async Task Pending_endpoint_requires_todos_read_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/mine",
            WorkflowPermissions.TodosRead,
            "workflowListMyTodos");
    }

    [TestMethod]
    public async Task History_endpoint_requires_todos_read_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/mine/history",
            WorkflowPermissions.TodosRead,
            "workflowListMyTodoHistory");
    }

    [TestMethod]
    public async Task ListPendingAsync_returns_paged_items_for_actor()
    {
        var actorId = Guid.CreateVersion7();
        var record = new WorkflowTodoListRecord(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "active",
            DateTimeOffset.UtcNow,
            null,
            null,
            3,
            "purchase",
            "PO-001",
            "采购申请 PO-001",
            "active",
            "purchase-flow",
            "approve");
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<long>(
                WorkflowSql.CountPendingTodosFiltered, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1L);
        query.QueryAsync<WorkflowTodoListRecord>(
                WorkflowSql.PagePendingTodosFilteredSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([record]);
        var service = CreateService(query);

        var result = await service.ListPendingAsync(
            actorId, 1, 20, "purchase-flow", "purchase", null, null, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("approve", result.Value!.Items[0].NodeKey);
    }

    [TestMethod]
    public async Task ListHistoryAsync_rejects_invalid_filters()
    {
        var service = CreateService(Substitute.For<IQueryExecutor>());
        var result = await service.ListHistoryAsync(
            Guid.CreateVersion7(),
            1,
            20,
            null,
            null,
            "unknown",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(-1),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, result.Error!.Code);
    }

    private static WorkflowTodoQueryService CreateService(IQueryExecutor query)
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        return new WorkflowTodoQueryService(
            query,
            tenant,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
    }

    private static async Task AssertEndpointPermissionAsync(string route, string permission, string operationName)
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
            .Single(candidate =>
                candidate.RoutePattern.RawText == route &&
                string.Equals(
                    candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    operationName,
                    StringComparison.Ordinal));
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(FullNetPermissionPolicies.For(permission), authorization[0].Policy);
    }
}
