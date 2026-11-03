using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.CompletePersonalSchedules;

/// <summary>本人原操作建议/状态/只读对账；任何入口都不能把请求身份替换为其他主体。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/agent/runs/{runId:guid}/schedule-completion").WithTags("AiPersonalScheduleCompletion");
        var permissions = new[] {AiAgentRunPermissions.Read, CalendarPermissions.Read, PersonalScheduleCompletionKeys.Permission};
        group.MapGet("/proposal", async (Guid runId, AiAgentRunManagementService service, ICurrentTenant tenant,
            IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!AgentRunHttpBinding.TryCreate(http, tenant.Id, out var binding)) return Results.Unauthorized();
            return mapper.Map(await service.ScheduleProposalAsync(runId, binding, token).ConfigureAwait(false), http);
        }).WithName("aiGetPersonalScheduleCompletionProposal").Produces<AiPersonalScheduleCompletionProposal>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(409)
            .RequireAuthorization(permissions.Append(AiAgentApprovalPermissions.Request).Select(FullNetPermissionPolicies.For).ToArray());
        group.MapGet("", (Guid runId, AiScheduleCompletionService service, ICurrentTenant tenant, IApiResultMapper mapper,
            HttpContext http, CancellationToken token) => Read(runId, false, service, tenant, mapper, http, token))
            .WithName("aiGetPersonalScheduleCompletionStatus").Produces<AiPersonalScheduleCompletionStatus>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).RequireAuthorization(permissions.Select(FullNetPermissionPolicies.For).ToArray());
        group.MapPost("/reconcile", (Guid runId, AiScheduleCompletionService service, ICurrentTenant tenant, IApiResultMapper mapper,
            HttpContext http, CancellationToken token) => Read(runId, true, service, tenant, mapper, http, token))
            .WithName("aiReconcilePersonalScheduleCompletion").Produces<AiPersonalScheduleCompletionStatus>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).RequireAuthorization(permissions.Select(FullNetPermissionPolicies.For).ToArray());
    }
    private static async Task<IResult> Read(Guid runId, bool reconcile, AiScheduleCompletionService service, ICurrentTenant tenant,
        IApiResultMapper mapper, HttpContext http, CancellationToken token)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!AgentRunHttpBinding.TryCreate(http, tenant.Id, out var binding)) return Results.Unauthorized();
        var value = await service.ReadAsync(runId, binding, reconcile, token).ConfigureAwait(false);
        return mapper.Map(value is null ? Result<AiPersonalScheduleCompletionStatus>.Failure(new("ai.agent_run.not_found", "The owned operation is unavailable.", ErrorType.NotFound))
            : Result<AiPersonalScheduleCompletionStatus>.Success(value), http);
    }
}
