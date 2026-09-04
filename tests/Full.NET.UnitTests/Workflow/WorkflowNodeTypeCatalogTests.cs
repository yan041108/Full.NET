using System.Text.Json;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowNodeTypeCatalogTests
{
    [TestMethod]
    public void Current_catalog_only_marks_nodes_with_real_runtime_support_as_publishable()
    {
        var catalog = WorkflowNodeTypeCatalog.Current;

        Assert.AreEqual(1, catalog.CatalogVersion);
        Assert.AreEqual(1, catalog.DefinitionSchemaVersion);
        CollectionAssert.AreEquivalent(
            new[] { "start", "human.approval", "notify.cc", "gateway.exclusive", "end" },
            catalog.NodeTypes.Select(item => item.NodeTypeKey).ToArray());
        Assert.IsTrue(catalog.NodeTypes.All(item => item.Designable && item.NodeSchemaVersion == 1));
        CollectionAssert.AreEquivalent(
            new[] { "start", "human.approval", "notify.cc", "end" },
            catalog.NodeTypes.Where(item => item.Publishable && item.Executable)
                .Select(item => item.NodeTypeKey).ToArray());
        Assert.IsTrue(catalog.NodeTypes.Single(item => item.NodeTypeKey == "notify.cc")
            is { Publishable: true, Executable: true });
        Assert.IsTrue(catalog.NodeTypes.Single(item => item.NodeTypeKey == "gateway.exclusive")
            is { Publishable: false, Executable: false });
        Assert.IsTrue(catalog.NodeTypes.Single(item => item.NodeTypeKey == "human.approval")
            .SupportsFieldPolicies);
        Assert.IsTrue(catalog.NodeTypes.Where(item => item.NodeTypeKey != "human.approval")
            .All(item => !item.SupportsFieldPolicies));
    }

    [TestMethod]
    public void Definition_json_rejects_client_capability_claims()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "nodes": [{
                "nodeKey": "start",
                "nodeTypeKey": "start",
                "nodeSchemaVersion": 1,
                "config": {},
                "executable": true
              }]
            }
            """;

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(
            json,
            WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft));
    }

    [TestMethod]
    public async Task Catalog_endpoint_requires_definitions_read_permission()
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
                "/api/v1/workflow/definitions/node-type-catalog");
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(
            FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead),
            authorization[0].Policy);
    }

    [TestMethod]
    public async Task Recipient_candidate_endpoint_requires_definitions_read_permission()
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
                "/api/v1/workflow/definitions/recipient-candidates");
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(
            FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead),
            authorization[0].Policy);
    }
}
