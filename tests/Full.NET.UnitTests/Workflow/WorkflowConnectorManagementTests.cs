using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Full.NET.Modules.Workflow.Features.ManageConnectorCalls;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>人工对账入口必须拥有独立权限，非法命令在读取和事务之前失败。</summary>
[TestClass]
public sealed class WorkflowConnectorManagementTests
{
    [TestMethod]
    [DataRow("/api/v1/workflow/definitions/connectors",WorkflowPermissions.DefinitionsRead)]
    [DataRow("/api/v1/workflow/instances/{instanceId:guid}/connector-calls",WorkflowPermissions.ConnectorCallsRead)]
    [DataRow("/api/v1/workflow/instances/{instanceId:guid}/connector-calls/{callId:guid}",WorkflowPermissions.ConnectorCallsRead)]
    [DataRow("/api/v1/workflow/instances/{instanceId:guid}/connector-calls/{callId:guid}/reconcile",WorkflowPermissions.ConnectorCallsReconcile)]
    public async Task Operator_endpoints_require_precise_permissions(string route,string permission)
    {
        var builder=WebApplication.CreateBuilder(); var module=new WorkflowModule();
        module.AddServices(builder.Services,builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app=builder.Build(); module.MapEndpoints(app);
        var endpoint=((IEndpointRouteBuilder)app).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>()
            .Single(item=>item.RoutePattern.RawText==route);
        var auth=endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(); Assert.HasCount(1,auth);
        Assert.AreEqual(FullNetPermissionPolicies.For(permission),auth[0].Policy);
    }

    [TestMethod]
    [DataRow("accept_result",false,"evidence")]
    [DataRow("confirm_no_effect",false,"")]
    [DataRow("retry",true,"evidence")]
    [DataRow("arbitrary",false,"evidence")]
    public async Task Invalid_resolution_does_not_read_or_write(string action,bool attempt,string evidence)
    {
        var query=Substitute.For<IQueryExecutor>(); var commands=Substitute.For<ICommandExecutor>();
        var transaction=Substitute.For<ICommandTransaction>();
        var service=new WorkflowConnectorManagementService(query,commands,transaction,Substitute.For<ICurrentTenant>(),Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),Options.Create(new DatabaseOptions()),new WorkflowConnectorCatalog([]),Substitute.For<ITenantFeatureEntitlementPort>());
        var result=await service.ReconcileAsync(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            new(1,1,action,attempt?Guid.NewGuid():null,evidence,"operator reason","resolution.1"),default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0,query.ReceivedCalls().Count());
        Assert.AreEqual(0,commands.ReceivedCalls().Count()); Assert.AreEqual(0,transaction.ReceivedCalls().Count());
    }
}
