using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageForms;

internal static class FormComponentCatalogEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/component-catalog", () => CreateResponse())
            .WithName("workflowGetFormComponentCatalog")
            .Produces<WorkflowFormComponentCatalogResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.FormsRead));
    }

    private static WorkflowFormComponentCatalogResponse CreateResponse()
    {
        var catalog = WorkflowFormComponentCatalog.Current;
        return new WorkflowFormComponentCatalogResponse(
            catalog.CatalogVersion,
            catalog.SchemaVersion,
            catalog.AdapterVersion,
            catalog.Components.Select(component => new WorkflowFormComponentResponse(
                component.FieldTypeKey,
                component.Designable,
                component.Publishable,
                component.Executable,
                component.ConstraintKeys)).ToArray());
    }
}
