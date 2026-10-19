// 工作流表单组件目录端点；返回允许的组件类型与安全属性。
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageForms;

/// <summary>工作流表单组件目录端点；返回允许的组件类型与安全属性。</summary>
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
