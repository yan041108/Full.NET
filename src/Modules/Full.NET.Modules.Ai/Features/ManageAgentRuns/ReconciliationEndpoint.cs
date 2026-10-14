using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>账本快照及显式人工核证端点；不提供远程自动重放。</summary>
internal static class ReconciliationEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/agent/runs").WithTags("AiAgentRuns");
        group.MapGet("/{runId:guid}/reconciliation", async (Guid runId, AiAgentReconciliationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.GetAsync(runId, token).ConfigureAwait(false), context))
            .WithName("aiGetAgentReconciliation").Produces<AiAgentReconciliationResponse>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Read));
        group.MapPost("/{runId:guid}/reconciliation", async (Guid runId, ReconcileAiAgentRunRequest request,
            AiAgentReconciliationService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.ReconcileAsync(runId, request, token).ConfigureAwait(false), context))
            .WithName("aiReconcileAgentRun").Produces<bool>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Reconcile));
    }
}
