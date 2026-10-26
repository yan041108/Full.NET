using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageConnectorCalls;

/// <summary>运维入口只接受精确权限和服务器身份，不公开连接目标、凭据或原始业务载荷。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/definitions/connectors", (WorkflowConnectorManagementService service)=>service.Catalog())
            .WithName("workflowListConnectorCatalog").WithTags("WorkflowDefinitions").Produces<WorkflowConnectorCatalogResponse>()
            .ProducesProblem(401).ProducesProblem(403).RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.DefinitionsRead));
        endpoints.MapGet("/api/v1/workflow/instances/{instanceId:guid}/connector-calls", async (Guid instanceId,int? page,int? pageSize,
            WorkflowConnectorManagementService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.ListAsync(instanceId,page??1,pageSize??20,token).ConfigureAwait(false),context))
            .WithName("workflowListConnectorCalls").WithTags("WorkflowInstances").Produces<WorkflowConnectorCallPageResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.ConnectorCallsRead));
        endpoints.MapGet("/api/v1/workflow/instances/{instanceId:guid}/connector-calls/{callId:guid}", async (Guid instanceId,Guid callId,
            WorkflowConnectorManagementService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.GetAsync(instanceId,callId,token).ConfigureAwait(false),context))
            .WithName("workflowGetConnectorCall").WithTags("WorkflowInstances").Produces<WorkflowConnectorCallResponse>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.ConnectorCallsRead));
        endpoints.MapPost("/api/v1/workflow/instances/{instanceId:guid}/connector-calls/{callId:guid}/reconcile", async (Guid instanceId,Guid callId,
            ReconcileWorkflowConnectorRequest request,WorkflowConnectorManagementService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            Guid.TryParse(context.User.FindFirst("sub")?.Value,out var actor)
                ?mapper.Map(await service.ReconcileAsync(instanceId,callId,actor,request,token).ConfigureAwait(false),context):Results.Unauthorized())
            .WithName("workflowReconcileConnectorCall").WithTags("WorkflowInstances").Produces<WorkflowConnectorResolutionResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.ConnectorCallsReconcile));
    }
}
