using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageWorkCalendars;

/// <summary>日历读写只使用认证作用域和实际操作人，独立精确权限保护每种管理动作。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/workflow/work-calendars").WithTags("WorkflowWorkCalendars");
        group.MapGet("/",async(Guid? afterId,WorkflowWorkCalendarService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.ListAsync(afterId,token).ConfigureAwait(false),context))
            .WithName("workflowListWorkCalendars").Produces<WorkflowWorkCalendarPageResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CalendarsRead));
        group.MapGet("/{calendarId:guid}",async(Guid calendarId,WorkflowWorkCalendarService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.GetAsync(calendarId,token).ConfigureAwait(false),context))
            .WithName("workflowGetWorkCalendar").Produces<WorkflowWorkCalendarResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CalendarsRead));
        group.MapGet("/{calendarId:guid}/versions",async(Guid calendarId,int? beforeVersionNumber,WorkflowWorkCalendarService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.ListVersionsAsync(calendarId,beforeVersionNumber,token).ConfigureAwait(false),context))
            .WithName("workflowListWorkCalendarVersions").Produces<WorkflowWorkCalendarVersionPageResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CalendarsRead));
        group.MapGet("/versions/{versionId:guid}",async(Guid versionId,WorkflowWorkCalendarService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>
            mapper.Map(await service.GetVersionAsync(versionId,token).ConfigureAwait(false),context))
            .WithName("workflowGetWorkCalendarVersion").Produces<WorkflowWorkCalendarVersionResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CalendarsRead));
        group.MapPost("/",async(CreateWorkflowWorkCalendarRequest request,WorkflowWorkCalendarService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>{
            if(!Actor(context,out var actor))return Results.Unauthorized();var result=await service.CreateAsync(actor,request,token).ConfigureAwait(false);
            return result.IsSuccess?Results.Created($"/api/v1/workflow/work-calendars/{result.Value!.Id:D}",result.Value):mapper.Map(result,context);
        }).WithName("workflowCreateWorkCalendar").Produces<WorkflowWorkCalendarResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CalendarsCreate));
        group.MapPut("/{calendarId:guid}/draft",async(Guid calendarId,UpdateWorkflowWorkCalendarRequest request,WorkflowWorkCalendarService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>{
            if(!Actor(context,out var actor))return Results.Unauthorized();return mapper.Map(await service.UpdateAsync(calendarId,actor,request,token).ConfigureAwait(false),context);
        }).WithName("workflowUpdateWorkCalendarDraft").Produces<WorkflowWorkCalendarResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CalendarsUpdate));
        group.MapPost("/{calendarId:guid}/publish",async(Guid calendarId,PublishWorkflowWorkCalendarRequest request,WorkflowWorkCalendarService service,IApiResultMapper mapper,HttpContext context,CancellationToken token)=>{
            if(!Actor(context,out var actor))return Results.Unauthorized();return mapper.Map(await service.PublishAsync(calendarId,actor,request,token).ConfigureAwait(false),context);
        }).WithName("workflowPublishWorkCalendar").Produces<WorkflowWorkCalendarVersionResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CalendarsPublish));
    }
    private static bool Actor(HttpContext context,out Guid actor)=>Guid.TryParse(context.User.FindFirst("sub")?.Value,out actor) && actor!=Guid.Empty;
}
