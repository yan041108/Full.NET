using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.DataApproval.Features.ManageScenarios;

/// <summary>映射 DataApproval 场景目录与绑定管理端点。</summary>
internal static class Endpoint
{
    /// <summary>注册 DataApproval 场景 HTTP 路由。</summary>
    /// <param name="endpoints">应用端点构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/data-approvals/scenarios")
            .WithTags("DataApprovalScenarios");

        group.MapGet("", async (
            DataApprovalScenarioService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("dataApprovalsListScenarios")
        .Produces<IReadOnlyList<DataApprovalScenarioResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(DataApprovalPermissions.ScenariosRead));

        group.MapGet("/{scenarioKey}", async (
            string scenarioKey,
            DataApprovalScenarioService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(scenarioKey, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("dataApprovalsGetScenario")
        .Produces<DataApprovalScenarioResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(DataApprovalPermissions.ScenariosRead));

        group.MapPut("/{scenarioKey}", async (
            string scenarioKey,
            UpdateDataApprovalScenarioBindingBody request,
            DataApprovalScenarioService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateBindingAsync(scenarioKey, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("dataApprovalsUpdateScenarioBinding")
        .Produces<DataApprovalScenarioResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(DataApprovalPermissions.ScenariosManage));
    }
}
