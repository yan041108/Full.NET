using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>仅按可信作用域返回实际注册动作，设计器不能声明或上传动作实现。</summary>
internal static class BusinessActionCatalogEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("/business-action-catalog", (
        [FromServices] WorkflowBusinessActionCatalog catalog, [FromServices] ICurrentTenant tenant) =>
        new WorkflowBusinessActionCatalogResponse(catalog.ForScope(WorkflowManagementScope.Resolve(tenant).ScopeKey)
            .Select(item => new WorkflowBusinessActionCatalogItemResponse(item.Definition.OwnerKey, item.Definition.ActionKey,
                item.Definition.ActionVersion, item.Definition.BusinessType, item.Definition.PlacementKey, item.Hash)).ToArray()))
        .WithName("workflowGetBusinessActionCatalog")
        .Produces<WorkflowBusinessActionCatalogResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));
}
