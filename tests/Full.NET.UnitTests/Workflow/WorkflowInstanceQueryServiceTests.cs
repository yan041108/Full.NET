using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证工作流实例分页列表查询与端点授权。</summary>
[TestClass]
public sealed class WorkflowInstanceQueryServiceTests
{
    /// <summary>全局列表端点必须绑定独立 list 权限。</summary>
    [TestMethod]
    public async Task List_endpoint_requires_instances_list_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/instances",
            WorkflowPermissions.InstancesList,
            "workflowListInstances");
    }

    /// <summary>“我发起的”列表端点必须绑定 read 权限并由服务端固定发起人。</summary>
    [TestMethod]
    public async Task List_mine_endpoint_requires_instances_read_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/instances/mine",
            WorkflowPermissions.InstancesRead,
            "workflowListMyInstances");
    }

    /// <summary>全局列表应按筛选条件分页并映射轻量响应。</summary>
    [TestMethod]
    public async Task ListAsync_returns_paged_items_for_scope()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var record = new WorkflowInstanceListRecord(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "purchase",
            "purchase",
            "PO-001",
            "采购申请 PO-001",
            "active",
            Guid.CreateVersion7(),
            startedAt,
            null);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<long>(
                WorkflowSql.CountInstancesFiltered, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1L);
        query.QueryAsync<WorkflowInstanceListRecord>(
                WorkflowSql.PageInstancesFilteredSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([record]);
        var service = CreateService(query);

        var result = await service.ListAsync(
            1,
            20,
            "active",
            "purchase",
            null,
            startedAt.AddDays(-1),
            startedAt.AddDays(1),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.Total);
        Assert.AreEqual("purchase", result.Value.Items[0].DefinitionKey);
        Assert.AreEqual("PO-001", result.Value.Items[0].BusinessId);
    }

    /// <summary>“我发起的”查询必须把可信用户标识写入发起人筛选。</summary>
    [TestMethod]
    public async Task ListMineAsync_rejects_empty_actor_and_pages_initiated_instances()
    {
        var service = CreateService(Substitute.For<IQueryExecutor>());
        var invalid = await service.ListMineAsync(
            Guid.Empty,
            1,
            20,
            null,
            null,
            null,
            null,
            null,
            CancellationToken.None);
        Assert.IsFalse(invalid.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, invalid.Error!.Code);

        var actorId = Guid.CreateVersion7();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<long>(
                WorkflowSql.CountInstancesFiltered, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(0L);
        query.QueryAsync<WorkflowInstanceListRecord>(
                WorkflowSql.PageInstancesFilteredSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WorkflowInstanceListRecord>());
        service = CreateService(query);

        var result = await service.ListMineAsync(
            actorId,
            1,
            20,
            null,
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(0, result.Value!.Items);
        await query.Received(1).QuerySingleOrDefaultAsync<long>(
            WorkflowSql.CountInstancesFiltered,
            Arg.Is<object?>(parameters => parameters != null),
            Arg.Any<CancellationToken>());
    }

    /// <summary>非法筛选条件必须返回稳定校验错误。</summary>
    [TestMethod]
    public async Task ListAsync_rejects_invalid_filters()
    {
        var service = CreateService(Substitute.For<IQueryExecutor>());
        var result = await service.ListAsync(
            1,
            20,
            "running",
            null,
            Guid.Empty,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(-1),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, result.Error!.Code);
    }

    private static WorkflowInstanceQueryService CreateService(IQueryExecutor query)
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        return new WorkflowInstanceQueryService(
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
