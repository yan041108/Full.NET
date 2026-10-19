// 工作流节点类型目录端点；返回受支持节点元数据，供设计器消费。
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>工作流节点类型目录端点；返回受支持节点元数据，供设计器消费。</summary>
internal static class NodeTypeCatalogEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/node-type-catalog", () => CreateResponse())
            .WithName("workflowGetNodeTypeCatalog")
            .Produces<WorkflowNodeTypeCatalogResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));
    }

    private static WorkflowNodeTypeCatalogResponse CreateResponse()
    {
        var catalog = WorkflowNodeTypeCatalog.Current;
        return new WorkflowNodeTypeCatalogResponse(
            catalog.CatalogVersion,
            catalog.DefinitionSchemaVersion,
            catalog.NodeTypes.Select(node => new WorkflowNodeTypeResponse(
                node.NodeTypeKey,
                node.NodeSchemaVersion,
                node.Designable,
                node.Publishable,
                node.Executable,
                node.SupportsFieldPolicies)).ToArray());
    }
}
