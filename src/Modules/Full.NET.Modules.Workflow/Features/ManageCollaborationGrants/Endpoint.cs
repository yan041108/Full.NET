using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageCollaborationGrants;

/// <summary>双方各以自己的会话和精确权限同意；请求不能指定操作人或可信当前租户。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/workflow/collaboration-grants").WithTags("WorkflowCollaborationGrants");
        group.MapGet("/{grantId:guid}",async (Guid grantId,WorkflowCollaborationGrantService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.GetAsync(grantId,token).ConfigureAwait(false),context))
            .WithName("workflowGetCollaborationGrant").Produces<WorkflowCollaborationGrantResponse>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CollaborationGrantsRead));
        group.MapPost("/",async (ProposeWorkflowCollaborationGrantRequest request,WorkflowCollaborationGrantService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            Guid.TryParse(context.User.FindFirst("sub")?.Value,out var actor)
                ? mapper.Map(await service.ProposeAsync(actor,request,token).ConfigureAwait(false),context) : Results.Unauthorized())
            .WithName("workflowProposeCollaborationGrant").Produces<WorkflowCollaborationGrantResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CollaborationGrantsPropose));
        group.MapPost("/{grantId:guid}/accept",async (Guid grantId,AcceptWorkflowCollaborationGrantRequest request,WorkflowCollaborationGrantService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            Guid.TryParse(context.User.FindFirst("sub")?.Value,out var actor)
                ? mapper.Map(await service.AcceptAsync(grantId,actor,request,token).ConfigureAwait(false),context) : Results.Unauthorized())
            .WithName("workflowAcceptCollaborationGrant").Produces<WorkflowCollaborationGrantResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CollaborationGrantsAccept));
        group.MapPost("/{grantId:guid}/revoke",async (Guid grantId,RevokeWorkflowCollaborationGrantRequest request,WorkflowCollaborationGrantService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            Guid.TryParse(context.User.FindFirst("sub")?.Value,out var actor)
                ? mapper.Map(await service.RevokeAsync(grantId,actor,request,token).ConfigureAwait(false),context) : Results.Unauthorized())
            .WithName("workflowRevokeCollaborationGrant").Produces<WorkflowCollaborationGrantResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CollaborationGrantsRevoke));
    }
}
