using System.Text.Json;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageForms;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowFormComponentCatalogTests
{
    [TestMethod]
    public void Form_schema_json_rejects_client_capability_claims()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "adapterVersion": 1,
              "sections": [{
                "sectionKey": "main",
                "fields": [{
                  "fieldKey": "summary",
                  "fieldTypeKey": "text",
                  "required": true,
                  "constraints": {},
                  "publishable": true
                }]
              }]
            }
            """;

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(
            json,
            WorkflowJsonSerializerContext.Default.WorkflowFormSchema));
    }

    [TestMethod]
    public void Current_catalog_is_closed_and_matches_publishable_field_types()
    {
        var catalog = WorkflowFormComponentCatalog.Current;

        Assert.AreEqual(1, catalog.CatalogVersion);
        Assert.AreEqual(1, catalog.SchemaVersion);
        Assert.AreEqual(1, catalog.AdapterVersion);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "text", "textarea", "integer", "decimal", "money", "date",
                "time", "datetime", "radio", "checkbox", "select", "switch",
            },
            catalog.Components.Select(component => component.FieldTypeKey).ToArray());
        Assert.IsTrue(catalog.Components.All(component =>
            component.Designable && component.Publishable && component.Executable));
        CollectionAssert.AreEquivalent(
            new[] { "maximum", "minimum", "scale" },
            catalog.Components.Single(component => component.FieldTypeKey == "money")
                .ConstraintKeys.ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "options" },
            catalog.Components.Single(component => component.FieldTypeKey == "checkbox")
                .ConstraintKeys.ToArray());
    }

    [TestMethod]
    public async Task Catalog_endpoint_requires_forms_read_permission()
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
                "/api/v1/workflow/forms/component-catalog");
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(
            FullNetPermissionPolicies.For(WorkflowPermissions.FormsRead),
            authorization[0].Policy);
    }
}
